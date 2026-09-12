import { useState } from "preact/hooks";
import Layout from "@/Shared/Layout";
import MessageModal from "@/Shared/Modal";
import { readErrorMessage } from "@/Shared/api";
import "/css/account.css";

// ログイン中ユーザー自身のパスワード変更ページ
export default function Password() {
    const [currentPassword, setCurrentPassword] = useState("");
    const [newPassword, setNewPassword] = useState("");
    const [confirmPassword, setConfirmPassword] = useState("");
    const [showPassword, setShowPassword] = useState(false);
    const [error, setError] = useState<string | null>(null);
    const [submitting, setSubmitting] = useState(false);
    const [notice, setNotice] = useState<string | null>(null);

    async function changePassword(e: Event) {
        e.preventDefault();
        setError(null);

        if (!currentPassword || !newPassword || !confirmPassword) {
            setError("すべての項目を入力してください。");
            return;
        }
        if (newPassword !== confirmPassword) {
            setError("新しいパスワードが一致しません。");
            return;
        }
        setSubmitting(true);

        try {
            const response = await fetch("/api/user/ChangePassword", {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({
                    currentPassword,
                    newPassword,
                    confirmPassword,
                }),
            });

            if (response.ok) {
                setNotice("パスワードを変更しました。");
                setCurrentPassword("");
                setNewPassword("");
                setConfirmPassword("");
            } else {
                // 統一エラー形式 ApiMessage(IdentityError もサーバー側で結合済み)
                setError(await readErrorMessage(response));
            }
        } catch (err) {
            console.log(err);
            setError("通信エラーが発生しました。");
        } finally {
            setSubmitting(false);
        }
    }

    return (
        <Layout title="パスワード変更 | QRQueue">
            <MessageModal message={notice} onClose={() => setNotice(null)} />
            <div class="container">
                <div class="password-form-container">
                    <h2>パスワード変更</h2>
                    {error && <p class="error">{error}</p>}
                    <form onSubmit={changePassword}>
                        <div class="form-group">
                            <label for="currentPassword">現在のパスワード</label>
                            <div class="password-input">
                                <input
                                    type={showPassword ? "text" : "password"}
                                    id="currentPassword"
                                    value={currentPassword}
                                    autoComplete="current-password"
                                    onInput={(e) => setCurrentPassword(e.currentTarget.value)}
                                />
                                <button type="button" class="password-toggle" onClick={() => setShowPassword(!showPassword)}>
                                    {showPassword ? "非表示" : "表示"}
                                </button>
                            </div>
                        </div>
                        <div class="form-group">
                            <label for="newPassword">新しいパスワード</label>
                            <div class="password-input">
                                <input
                                    type={showPassword ? "text" : "password"}
                                    id="newPassword"
                                    value={newPassword}
                                    autoComplete="new-password"
                                    onInput={(e) => setNewPassword(e.currentTarget.value)}
                                />
                            </div>
                        </div>
                        <div class="form-group">
                            <label for="confirmPassword">新しいパスワード（確認）</label>
                            <div class="password-input">
                                <input
                                    type={showPassword ? "text" : "password"}
                                    id="confirmPassword"
                                    value={confirmPassword}
                                    autoComplete="new-password"
                                    onInput={(e) => setConfirmPassword(e.currentTarget.value)}
                                />
                            </div>
                        </div>
                        <button type="submit" class="btn-primary" disabled={submitting}>
                            {submitting ? "変更中..." : "変更する"}
                        </button>
                    </form>
                </div>
            </div>
        </Layout>
    );
}
