namespace QRQueue.Models.API
{
    /// <summary>
    /// POST /api/user/ResetPassword のリクエスト(管理者による他ユーザーのパスワード再設定、issue #83)。
    /// 新しいパスワードの設定に失敗しても既存パスワードは無効化されない。
    /// </summary>
    public class ResetPasswordModel
    {
        public string UserName { get; set; }
        public string NewPassword { get; set; }
        public string ConfirmPassword { get; set; }
    }
}
