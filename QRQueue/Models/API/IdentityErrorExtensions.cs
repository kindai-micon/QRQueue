using Microsoft.AspNetCore.Identity;

namespace QRQueue.Models.API
{
    /// <summary>IdentityResult のエラーを { message } 統一レスポンスへ変換するヘルパー</summary>
    public static class IdentityErrorExtensions
    {
        /// <summary>IdentityError[] の Description を連結して ApiMessage にする</summary>
        public static ApiMessage ToApiMessage(this IEnumerable<IdentityError> errors)
        {
            return new ApiMessage(string.Join(" ", errors.Select(e => e.Description)));
        }
    }
}
