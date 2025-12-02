using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using static System.Runtime.InteropServices.RuntimeInformation;


namespace Server.Services
{
    public class SystemService
    {

        public List<ProcessInfo> ListRunningProcesses(bool isAppOnly = false)
        {
            List<ProcessInfo> list = new List<ProcessInfo>(); // 1 danh sách để chứa cái tiến trình
            Process[] allProcesses = Process.GetProcesses();  // lấy các tiến trình
            
            bool isWindows = IsOSPlatform(OSPlatform.Windows); // check coi phải Win ko

            if (isAppOnly && !isWindows) // nếu là mac hoặc linux
            {
                return GetAppsOnUnix(allProcesses);
            }

            foreach (Process p in allProcesses)
            {
                try
                {
                    if (p.Id == 0) continue;
                    
                    string windowTitle = p.MainWindowTitle;

                    if (isAppOnly)
                    {
                        if (string.IsNullOrEmpty(windowTitle)) // nếu ko có title thì bỏ qua
                        {
                            continue; 
                        }
                    }
                    
                    // Đóng gói dữ liệu
                    list.Add(new ProcessInfo
                    {
                        Id = p.Id,
                        Name = p.ProcessName,
                        Title = windowTitle,
                        MemoryUsage = p.WorkingSet64
                    });
                }
                catch {}
            }

            return list.OrderBy(p => p.Name).ToList();
        }

        private List<ProcessInfo> GetAppsOnUnix(Process[] allProcesses)
        {
            List<ProcessInfo> apps = new List<ProcessInfo>();
            
            string psOutput = ExecuteShellCommand("ps -axco pid", "sh"); // dùng lệnh ps.. để liệt kê tiến trình đang chạy
            
            //chúng ta sẽ chỉ lọc các tiến trình có tên App 
            // mà không phải là các daemon hệ thống.
            
            foreach (Process p in allProcesses)
            {   
                try
                {
                    if (string.IsNullOrEmpty(p.MainWindowTitle)) continue; // bỏ mấy cái ko có title như win

                    // này kiểu lọc mấy cái tiến trình hệ thống
                    if (p.MainModule?.FileName.StartsWith("/usr/bin/", StringComparison.OrdinalIgnoreCase) == true ||
                        p.MainModule?.FileName.StartsWith("/sbin/", StringComparison.OrdinalIgnoreCase) == true)
                    {
                        continue;
                    }

                    // 3. Đóng gói dữ liệu
                    apps.Add(new ProcessInfo
                    {
                        Id = p.Id,
                        Name = p.ProcessName,
                        Title = p.MainWindowTitle,
                        MemoryUsage = p.WorkingSet64
                    });
                }
                catch {}
            }
            return apps.OrderBy(p => p.Name).ToList();
        }

        private string ExecuteShellCommand(string command, string shell = "/bin/bash")
        {
            var process = new Process()
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = shell,
                    Arguments = $"-c \"{command}\"",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                }
            };
            process.Start();
            string result = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            return result;
        }
    }
};

