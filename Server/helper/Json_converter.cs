using System.Text.Json;

namespace Server.helper
{
    public static class JsonHelper
    {
        // Khai báo cấu hình JSON chuẩn (camelCase) dùng chung cho cả Project
        private static readonly JsonSerializerOptions _options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase, // Tự động đổi AppName -> appName
            WriteIndented = false // Tắt format xuống dòng để tiết kiệm dung lượng
        };

        public static string ToJson<T>(T data)
        {
            // Tự động áp dụng options vào mọi dữ liệu
            return JsonSerializer.Serialize(data, _options);
        }

        public static T FromJson<T>(string json)
        {
            return JsonSerializer.Deserialize<T>(json, _options) ??
                   throw new JsonException("Invalid JSON payload.");
        }
    }
}
