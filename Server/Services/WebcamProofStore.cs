using System.Text.Json;

namespace Server.Services
{
    public sealed class WebcamProofMeta
    {
        public string Id { get; init; } = string.Empty;
        public DateTime CreatedUtc { get; init; }
        public int Fps { get; init; }
        public int FrameCount { get; init; }
        public double DurationSeconds { get; init; }
    }

    public sealed class WebcamProofStore
    {
        private readonly ILogger<WebcamProofStore> _logger;
        private readonly string _rootDir;
        private readonly string _legacyRootDir;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };

        public WebcamProofStore(ILogger<WebcamProofStore> logger, IHostEnvironment env)
        {
            _logger = logger;
            _legacyRootDir = Path.Combine(env.ContentRootPath, "ServerData", "webcam_proofs");

            // Default: store outside project folder to avoid dotnet-watch/hotreload restarts when proofs change.
            var externalBase = Environment.GetEnvironmentVariable("WEBCAM_PROOF_DIR");
            if (string.IsNullOrWhiteSpace(externalBase))
            {
                externalBase = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            }
            _rootDir = Path.Combine(externalBase, "RemoteControlSystem", "webcam_proofs");

            Directory.CreateDirectory(_rootDir);
        }

        public async Task<WebcamProofMeta?> SaveAsync(IReadOnlyList<byte[]> frames, int fps, CancellationToken ct)
        {
            if (frames.Count == 0) return null;
            fps = Math.Clamp(fps, 1, 30);

            var id = $"{DateTime.UtcNow:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}"[..24];
            var dir = Path.Combine(_rootDir, id);
            Directory.CreateDirectory(dir);

            try
            {
                for (var i = 0; i < frames.Count; i++)
                {
                    var file = Path.Combine(dir, $"frame_{i:0000}.jpg");
                    await File.WriteAllBytesAsync(file, frames[i], ct);
                }

                var meta = new WebcamProofMeta
                {
                    Id = id,
                    CreatedUtc = DateTime.UtcNow,
                    Fps = fps,
                    FrameCount = frames.Count,
                    DurationSeconds = frames.Count / (double)fps
                };

                var metaPath = Path.Combine(dir, "meta.json");
                await File.WriteAllTextAsync(metaPath, JsonSerializer.Serialize(meta, JsonOptions), ct);

                return meta;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save webcam proof {Id}", id);
                return null;
            }
        }

        public IReadOnlyList<WebcamProofMeta> List()
        {
            var results = new List<WebcamProofMeta>();

            foreach (var baseDir in new[] { _rootDir, _legacyRootDir })
            {
                if (!Directory.Exists(baseDir)) continue;

                foreach (var dir in Directory.GetDirectories(baseDir))
                {
                    try
                    {
                        var metaPath = Path.Combine(dir, "meta.json");
                        if (!File.Exists(metaPath)) continue;

                        var json = File.ReadAllText(metaPath);
                        var meta = JsonSerializer.Deserialize<WebcamProofMeta>(json, JsonOptions);
                        if (meta != null && !string.IsNullOrWhiteSpace(meta.Id))
                            results.Add(meta);
                    }
                    catch
                    {
                        // ignore bad proof folders
                    }
                }
            }

            return results
                .GroupBy(p => p.Id, StringComparer.Ordinal)
                .Select(g => g.OrderByDescending(x => x.CreatedUtc).First())
                .OrderByDescending(p => p.CreatedUtc)
                .ToList();
        }

        public async Task<IReadOnlyList<byte[]>> LoadFramesAsync(string proofId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(proofId)) return Array.Empty<byte[]>();

            var safeId = proofId.Trim();
            if (safeId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return Array.Empty<byte[]>();

            string? dir = null;
            var primary = Path.Combine(_rootDir, safeId);
            if (Directory.Exists(primary)) dir = primary;
            else
            {
                var legacy = Path.Combine(_legacyRootDir, safeId);
                if (Directory.Exists(legacy)) dir = legacy;
            }
            if (dir == null) return Array.Empty<byte[]>();

            var files = Directory.GetFiles(dir, "frame_*.jpg").OrderBy(f => f).ToList();
            if (files.Count == 0) return Array.Empty<byte[]>();

            var frames = new List<byte[]>(files.Count);
            foreach (var file in files)
            {
                frames.Add(await File.ReadAllBytesAsync(file, ct));
            }
            return frames;
        }
    }
}
