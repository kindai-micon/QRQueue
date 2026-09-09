namespace QRQueue.Models.API
{
    /// <summary>POST /api/user/ChangePassword のリクエスト</summary>
    public class ChangePasswordModel
    {
        public string CurrentPassword { get; set; }
        public string NewPassword { get; set; }
        public string ConfirmPassword { get; set; }
    }
}
