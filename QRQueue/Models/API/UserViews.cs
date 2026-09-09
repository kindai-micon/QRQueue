namespace QRQueue.Models.API
{
    /// <summary>GET /api/user/GetPasscode のレスポンス(初期管理者登録用パスコード)</summary>
    public record PasscodeView(string Passcode);
}
