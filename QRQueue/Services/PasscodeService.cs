using QRQueue.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace QRQueue.Services
{
    /// <summary>
    /// 初期管理者登録用パスコード(issue #77)。
    /// パスコードは未認証のAPIから取得できない。入手手段は次の2つのみ:
    /// 1. デプロイ時の設定: 構成 "InitialAdmin:Passcode" に設定した値(運用担当者が事前に決定)
    /// 2. 未設定の場合、起動時に1回だけサーバーコンソールへ出力する(サーバーにログインできる担当者のみ参照可能)
    /// </summary>
    public class PasscodeService : IPasscodeService
    {
        private readonly UserManager<ApplicationUser> userManager;
        private static string? Passcode;

        private readonly IConfiguration configuration;

        public PasscodeService(UserManager<ApplicationUser> userManager, IConfiguration configuration)
        {
            this.userManager = userManager;
            this.configuration = configuration;

            if (Passcode != null)
            {
                return;
            }

            // デプロイ時設定を優先し、未設定なら起動時に生成してサーバーコンソールへ一度だけ出力する
            Passcode = configuration["InitialAdmin:Passcode"];
            if (string.IsNullOrEmpty(Passcode))
            {
                Passcode = GenerateNewPasscode();
                Console.WriteLine("初期管理者登録用パスコード(サーバーコンソールでのみ確認できます): " + Passcode);
            }
        }

        /// <summary>
        /// 新しいパスコードを生成する
        /// </summary>
        private static string GenerateNewPasscode()
        {
            // 予測可能性を避けるため暗号学的に安全な乱数を使用する
            Span<char> chars = stackalloc char[10];
            for (int i = 0; i < chars.Length; i++)
            {
                chars[i] = (char)('0' + System.Security.Cryptography.RandomNumberGenerator.GetInt32(0, 10));
            }
            return new string(chars);
        }

        public async Task<bool> CheckPascodeAsync(string passcode)
        {
            var usersCount = await userManager.Users.CountAsync();
            if (usersCount != 0)
            {
                // 初期管理者が既に存在する場合は、どのような入力でも失敗扱いとする
                return false;
            }

            return FixedTimeEquals(passcode);
        }

        public bool CheckPascode(string passcode)
        {
            var usersCount = userManager.Users.Count();
            if (usersCount != 0)
            {
                return false;
            }

            return FixedTimeEquals(passcode);
        }

        /// <summary>恒時間側チャネルを避けるため固定長比較でパスコードを照合する</summary>
        private bool FixedTimeEquals(string? passcode)
        {
            return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
                System.Text.Encoding.UTF8.GetBytes(passcode ?? string.Empty),
                System.Text.Encoding.UTF8.GetBytes(Passcode ?? string.Empty));
        }
    }
}
