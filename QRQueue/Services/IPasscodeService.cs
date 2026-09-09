namespace QRQueue.Services
{
    /// <summary>
    /// 初期管理者登録用パスコードの検証(issue #77)。
    /// パスコードそのものを外部へ返す API・メソッドは提供しない。
    /// </summary>
    public interface IPasscodeService
    {
        public Task<bool> CheckPascodeAsync(string pascode);
        public bool CheckPascode(string pascode);
    }
}
