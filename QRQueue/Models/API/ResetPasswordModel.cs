namespace QRQueue.Models.API
{
    /// <summary>POST /api/user/ResetPassword のリクエスト(管理者による他ユーザーのパスワード再設定)</summary>
    public class ResetPasswordModel
    {
        public string UserName { get; set; }
        public string NewPassword { get; set; }
        public string ConfirmPassword { get; set; }
    }
}
