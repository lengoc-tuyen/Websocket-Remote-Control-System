namespace Server.Shared
{
    public static class ConnectionStatus
    {
        public const string RegistrationRequired = "REGISTRATION_REQUIRED"; 
        public const string Authenticated = "AUTHENTICATED"; 
        public const string LoginRequired = "LOGIN_REQUIRED"; 
    }

    public static class StatusType
    {
        public const string Auth = "AUTH";
        public const string App = "APP";
        public const string Keylog = "KEYLOG";
        public const string Screen = "SCREENSHOT";
        public const string Webcam = "WEBCAM";
        public const string System = "SYSTEM";
    }

    /// <summary>
    /// Các thông báo lỗi tiêu chuẩn.
    /// </summary>
    public static class ErrorMessages
    {
        public const string SetupCodeInvalid = "Mã Master Code không đúng.";
        public const string RegistrationNotAllowed = "Bạn cần nhập Master Code trước khi đăng ký tài khoản mới.";
        public const string RegistrationExpired = "Phiên đăng ký đã hết hạn. Vui lòng nhập lại Master Code.";
        public const string InvalidUsername = "Tên đăng nhập không hợp lệ (3–32 ký tự, chỉ chữ/số và . _ -).";
        public const string InvalidPassword = "Mật khẩu không hợp lệ (tối thiểu 8 ký tự).";
        public const string RegistrationFailed = "Đăng ký thất bại. Vui lòng thử lại.";
        public const string UsernameTaken = "Tên đăng nhập đã tồn tại.";
        public const string InvalidCredentials = "Tên đăng nhập hoặc mật khẩu không đúng.";
    }
}
