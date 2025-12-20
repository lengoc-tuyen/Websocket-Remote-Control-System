using System.Collections.Concurrent;

namespace Server.Services
{
    public sealed class WebcamStreamManager
    {
        private sealed class Session
        {
            public required string ConnectionId { get; init; }
            public required int Fps { get; init; }
            public required TimeSpan ProofDuration { get; init; }
            public required DateTime StartedUtc { get; init; }
            public required CancellationTokenSource Cts { get; init; }
            public required List<byte[]> ProofFrames { get; init; }
            public Task? Worker { get; set; }
            public Task<WebcamProofMeta?>? ProofSaveTask { get; set; }
            public bool ProofSaveStarted { get; set; }
        }

        private readonly ILogger<WebcamStreamManager> _logger;
        private readonly WebcamService _webcamService;
        private readonly WebcamProofStore _proofStore;
        private readonly ConcurrentDictionary<string, Session> _sessions = new();

        public WebcamStreamManager(
            ILogger<WebcamStreamManager> logger,
            WebcamService webcamService,
            WebcamProofStore proofStore)
        {
            _logger = logger;
            _webcamService = webcamService;
            _proofStore = proofStore;
        }

        public bool IsActive(string connectionId) => _sessions.ContainsKey(connectionId);

        public async Task<bool> StartAsync(
            string connectionId,
            int fps,
            TimeSpan proofDuration,
            Func<byte[], CancellationToken, Task> onFrame,
            Func<WebcamProofMeta, Task>? onProofSaved,
            CancellationToken connectionAborted)
        {
            if (string.IsNullOrWhiteSpace(connectionId)) return false;
            if (_sessions.ContainsKey(connectionId)) return true;

            fps = Math.Clamp(fps, 1, 30);
            if (proofDuration <= TimeSpan.Zero) proofDuration = TimeSpan.FromSeconds(10);

            var cts = CancellationTokenSource.CreateLinkedTokenSource(connectionAborted);
            var session = new Session
            {
                ConnectionId = connectionId,
                Fps = fps,
                ProofDuration = proofDuration,
                StartedUtc = DateTime.UtcNow,
                Cts = cts,
                ProofFrames = new List<byte[]>(fps * (int)Math.Ceiling(proofDuration.TotalSeconds))
            };

            if (!_sessions.TryAdd(connectionId, session))
            {
                cts.Dispose();
                return false;
            }

            session.Worker = Task.Run(async () =>
            {
                try
                {
                    await _webcamService.RunLiveWebcam(
                        fps,
                        session.Cts.Token,
                        async (frame, ct) =>
                        {
                            try
                            {
                                var now = DateTime.UtcNow;
                                if (!session.ProofSaveStarted && now - session.StartedUtc <= session.ProofDuration)
                                {
                                    session.ProofFrames.Add(frame);
                                }
                                else if (!session.ProofSaveStarted && session.ProofFrames.Count > 0)
                                {
                                    session.ProofSaveStarted = true;
                                    var snapshot = session.ProofFrames.ToArray();
                                    session.ProofFrames.Clear();
                                    session.ProofSaveTask = _proofStore.SaveAsync(snapshot, session.Fps, CancellationToken.None);

                                    if (onProofSaved != null)
                                    {
                                        _ = session.ProofSaveTask.ContinueWith(async t =>
                                        {
                                            try
                                            {
                                                var meta = t.Status == TaskStatus.RanToCompletion ? t.Result : null;
                                                if (meta != null) await onProofSaved(meta);
                                            }
                                            catch
                                            {
                                                // ignore
                                            }
                                        }, CancellationToken.None);
                                    }
                                }

                                await onFrame(frame, ct);
                            }
                            catch
                            {
                                // ignore send errors
                            }
                        });
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Webcam live stream crashed for {ConnectionId}", connectionId);
                }
            }, CancellationToken.None);

            return true;
        }

        public async Task<WebcamProofMeta?> StopAndSaveAsync(string connectionId, CancellationToken ct)
        {
            if (!_sessions.TryRemove(connectionId, out var session)) return null;

            try
            {
                session.Cts.Cancel();
            }
            catch { }

            try
            {
                if (session.Worker != null)
                    await session.Worker.WaitAsync(TimeSpan.FromSeconds(3), CancellationToken.None);
            }
            catch { }
            finally
            {
                session.Cts.Dispose();
            }

            if (session.ProofSaveTask != null)
            {
                try { return await session.ProofSaveTask; } catch { return null; }
            }

            if (session.ProofFrames.Count == 0) return null;
            return await _proofStore.SaveAsync(session.ProofFrames, session.Fps, CancellationToken.None);
        }
    }
}
