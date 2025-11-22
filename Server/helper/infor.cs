using System;
using System.Diagnostics;
public class ProcessInfo
{
    public int Id { get; set; } 
    public string Name { get; set; } // Này là tên tiến trình
    public string Title { get; set; } // Này là tên cửa sổ
    public long MemoryUsage { get; set; } 
    
    public bool isApp => !string.IsNullOrEmpty(Title);
}