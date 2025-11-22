using System;
using System.Text.Json;

namespace Client.helper
{
    public static class JsonHelper
    {
        public static string ToJson<T>(T data)
        {
            return JsonSerializer.Serialize(data);
        }

        public static T FromJson<T>(string json)
        {
            return JsonSerializer.Deserialize<T>(json);
        }
    }
}