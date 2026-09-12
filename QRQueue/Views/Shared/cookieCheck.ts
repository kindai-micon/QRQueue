// cookie への書き込みが実際に可能かを試して判定する。
// iOS の「コードスキャン」等のアプリ内ビューアでは cookie が永続化されず、
// 参加情報(participant cookie)が保持できないため、画面上で警告を出すために使う。
export function isCookieStorageAvailable(): boolean {
    try {
        document.cookie = "cqjsprobe=1; path=/; max-age=10; SameSite=Lax";
        const ok = document.cookie.includes("cqjsprobe=1");
        // 掃除
        document.cookie = "cqjsprobe=; path=/; max-age=0";
        return ok;
    } catch {
        return false;
    }
}
