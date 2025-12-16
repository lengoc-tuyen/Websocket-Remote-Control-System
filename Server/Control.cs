using Microsoft.AspNetCore.SignalR;
using Server.Services;
using Server.helper;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Threading;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using System;


namespace Server.Hubs
{
    public class ControlHub : Hub
    {
        private readonly SystemService _systemService;
        private readonly WebcamService _webcamService;
        private readonly InputService _inputService;
        private readonly IHubContext<ControlHub> _hubContext;
        private readonly IConfiguration _configuration;
        private readonly AuthService _authService; 

        public ControlHub(
            SystemService systemService, 
            WebcamService webcamService, 
            InputService inputService,
            IHubContext<ControlHub> hubContext,
            IConfiguration configuration,
            AuthService authService)
        {
            _systemService = systemService;
            _webcamService = webcamService;
            _inputService = inputService;
            _hubContext = hubContext;
            _configuration = configuration;
            _authService = authService;

        }

        // Hàm bảo vệ (Guard): Kiểm tra xem user có quyền không
        private async Task<bool> IsAuthenticated()
        {
            if (_authService.IsAuthenticated(Context.ConnectionId)) return true;
            await Clients.Caller.SendAsync("ReceiveStatus", "AUTH_FAIL", false, "Vui lòng đăng nhập để thực hiện lệnh.");
            return false;
        }
        
        // Client gọi hàm này đầu tiên để biết nên hiện form nào (Setup, Register hay Login)
        public string GetServerStatus()
        {
            if (_authService.IsAuthenticated(Context.ConnectionId)) return "AUTHENTICATED";
            if (!_authService.IsAnyUserRegistered())
            {
                if (_authService.IsRegistrationAllowed(Context.ConnectionId)) return "SETUP_REGISTER";
                return "SETUP_REQUIRED"; 
            }
            return "LOGIN_REQUIRED"; 
        }

        // Bước 1: Nộp mã khóa chủ (Master Code)
        public async Task SubmitSetupCode(string code)
        {
            if (_authService.IsAnyUserRegistered())
            {
                await Clients.Caller.SendAsync("ReceiveStatus", "SETUP", false, "Server đã cài đặt rồi.");
                return;
            }
            if (_authService.ValidateSetupCode(Context.ConnectionId, code))
            {
                await Clients.Caller.SendAsync("ReceiveStatus", "SETUP", true, "Mã đúng! Hãy tạo tài khoản Admin.");
            }
            else
            {
                await Clients.Caller.SendAsync("ReceiveStatus", "SETUP", false, "Mã Khóa Chủ sai.");
            }
        }

        // Bước 2: Đăng ký tài khoản Admin đầu tiên
        public async Task RegisterUser(string username, string password)
        {
            if (!_authService.IsRegistrationAllowed(Context.ConnectionId))
            {
                await Clients.Caller.SendAsync("ReceiveStatus", "REGISTER", false, "Chưa nhập Mã Khóa Chủ.");
                return;
            }
            if (_authService.IsUsernameTaken(username))
            {
                await Clients.Caller.SendAsync("ReceiveStatus", "REGISTER", false, "Tên tài khoản đã tồn tại.");
                return;
            }
            if (await _authService.TryRegisterAsync(Context.ConnectionId, username, password))
            {
                _authService.TryAuthenticate(Context.ConnectionId, username, password); // Tự động login sau khi đăng ký
                await Clients.Caller.SendAsync("ReceiveStatus", "REGISTER", true, $"Tạo tài khoản {username} thành công!");
            }
            else
            {
                await Clients.Caller.SendAsync("ReceiveStatus", "REGISTER", false, "Lỗi lưu tài khoản.");
            }
        }

        // Bước 3: Đăng nhập
        public async Task<bool> Login(string username, string password)
        {
            bool success = _authService.TryAuthenticate(Context.ConnectionId, username, password);
            if (success) await Clients.Caller.SendAsync("ReceiveStatus", "LOGIN", true, $"Chào mừng trở lại, {username}!");
            else await Clients.Caller.SendAsync("ReceiveStatus", "LOGIN", false, "Sai thông tin đăng nhập.");
            return success;
        }

        // Tự động đăng xuất khi mất kết nối
        public override Task OnDisconnectedAsync(Exception exception)
        {
            _authService.Logout(Context.ConnectionId);
            return base.OnDisconnectedAsync(exception);
        }

        // --- NHÓM 1: HỆ THỐNG (LIST, START, KILL, SHUTDOWN) ---

        public async Task GetProcessList(bool isAppOnly)
        {
            //if (!await IsAuthenticated()) return;
            var list = _systemService.ListProcessOrApp(isAppOnly);
            // Gửi kết quả về cho người gọi (Caller)
            string json = JsonHelper.ToJson(list);
            await Clients.Caller.SendAsync("ReceiveProcessList", json);
        }

        public async Task StartProcess(string path)
        {
            //if (!await IsAuthenticated()) return;
            bool result = _systemService.startProcessOrApp(path);
            await Clients.Caller.SendAsync("ReceiveStatus", "START", result, result ? "Đã gửi lệnh mở" : "Lỗi mở file");
        }

        public async Task KillProcess(int id)
        {
            //if (!await IsAuthenticated()) return;   
            bool result = _systemService.killProcessOrApp(id);
            await Clients.Caller.SendAsync("ReceiveStatus", "KILL", result, result ? "Đã diệt thành công" : "Không thể diệt");
        }

        public async Task ShutdownServer(bool isRestart)
        {
           // if (!await IsAuthenticated()) return;
            bool result = _systemService.shutdownOrRestart(isRestart);
            await Clients.Caller.SendAsync("ReceiveStatus", "POWER", result, "Đang thực hiện lệnh nguồn...");
        }

        // --- NHÓM 2: MÀN HÌNH & WEBCAM ---

        public async Task GetScreenshot()
        {
            //if (!await IsAuthenticated()) return;
            byte[] image = _webcamService.captureScreen();
            // Gửi ảnh về Client
            await Clients.Caller.SendAsync("ReceiveImage", "SCREENSHOT", image);
        }

        // Lệnh: Mở Webcam -> Quay 3s -> Gửi về -> Giữ cam mở
        public async Task RequestWebcam()
        {
            await Clients.Caller.SendAsync("ReceiveStatus", "WEBCAM", true, "Đang quay video 10 giây...");

            var token = Context.ConnectionAborted;

            try
            {
                // Gọi Service để quay (chờ khoảng 3s)
                var frames = await _webcamService.RequestWebcam(10, token);

                if (frames == null || frames.Count == 0)
                {
                    await Clients.Caller.SendAsync("ReceiveStatus", "WEBCAM", false, "Lỗi: Không quay được frame nào (Cam lỗi hoặc bị chiếm).");
                    return;
                }

                await Clients.Caller.SendAsync("ReceiveStatus", "WEBCAM", true, $"Đang gửi {frames.Count} khung hình...");

                // Gửi từng frame về Client
                foreach (var frame in frames)
                {
                    await Clients.Caller.SendAsync("ReceiveImage", "WEBCAM_FRAME", frame);
                    // Delay nhẹ để Client kịp hiển thị (tạo cảm giác như đang phát video)
                    await Task.Delay(100); 
                }
                
                await Clients.Caller.SendAsync("ReceiveStatus", "WEBCAM", true, "Đã gửi xong video.");
            }
            catch (Exception ex)
            {
                await Clients.Caller.SendAsync("ReceiveStatus", "WEBCAM", false, "Lỗi Server: " + ex.Message);
            }
        }
        public async Task CloseWebcam()
        {
            //if (!await IsAuthenticated()) return;
            _webcamService.closeWebcam();
            await Clients.Caller.SendAsync("ReceiveStatus", "WEBCAM", true, "Đã đóng Webcam.");
        }

        // --- NHÓM 3: KEYLOGGER (INPUT) ---

        public async Task StartKeyLogger()
        {
            string connectionId = Context.ConnectionId;
            
            _inputService.StartKeyLogger((keyData) => 
            {
                // Fire-and-forget - không await
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await _hubContext.Clients.Client(connectionId).SendAsync("ReceiveKeyLog", keyData);
                    }
                    catch
                    {
                        // Bỏ qua lỗi network
                    }
                });
                
                return Task.CompletedTask;
            });

            await Clients.Caller.SendAsync("ReceiveStatus", "KEYLOG", true, "Keylogger đã bắt đầu.");
        }

        public async Task StopKeyLogger()
        {
            //if (!await IsAuthenticated()) return;
            _inputService.StopKeyLogger();
            await Clients.Caller.SendAsync("ReceiveStatus", "KEYLOG", false, "Keylogger đã dừng.");
        }

    }
}