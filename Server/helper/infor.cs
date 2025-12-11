using System;
using System.Diagnostics;
public class ProcessInfo
{
    // Ở file này thì dùng tên viết hoa, còn bên client thì JavaScript tìm bằng viết thường nên 
    //sẽ không tìm được, nhưng sửa bên đây sẽ vi phạm chuẩn của C# -> cần bộ chuyển đổi JSON (chứ đáng lẽ sửa file này vẫn chạy nha))
    public int Id { get; set; } 
    public string Name { get; set; } // Này là tên tiến trình
    public string Title { get; set; } // Này là tên cửa sổ
    public long MemoryUsage { get; set; } 
    
    public bool isApp => !string.IsNullOrEmpty(Title);
}