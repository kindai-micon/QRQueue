import { useState } from "preact/hooks";
import Layout from "@/Shared/Layout";
import { readErrorMessage } from "@/Shared/api";

// SvelteKit routes/admin/delete-data/+page.svelte から移行
export default function DeleteData() {
    const [password, setPassword] = useState("");
    const [showPassword, setShowPassword] = useState(false);
    const [error, setError] = useState<string | null>(null);
    const [isDeleting, setIsDeleting] = useState(false);
    const [deleteSuccess, setDeleteSuccess] = useState(false);

    async function handleDeleteAllData(e: Event) {
        e.preventDefault();
        setError(null);

        if (!password) {
            setError("パスワードを入力してください。");
            return;
        }

        if (!confirm("⚠️ 本当にすべてのデータを削除しますか？\n\nこの操作は取り消せません。")) {
            return;
        }

        setIsDeleting(true);

        try {
            const response = await fetch("/api/admin/DeleteAllData", {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({ password }),
            });

            if (response.ok) {
                setDeleteSuccess(true);
                // 3秒後にログイン画面にリダイレクト
                setTimeout(() => {
                    window.location.href = "/login";
                }, 3000);
            } else {
                setError(await readErrorMessage(response));
            }
        } catch (err) {
            setError("通信エラーが発生しました。");
            console.error(err);
        } finally {
            setIsDeleting(false);
        }
    }

    function handleCancel() {
        window.location.href = "/";
    }

    return (
        <Layout title="データ削除 | QRQueue">
            <link rel="stylesheet" href="/css/admin-delete-data.css" />
            <div class="delete-container">
                <div class="page-title">⚠️ すべてのデータを削除</div>

                {deleteSuccess ? (
                    <div class="success">
                        ✓ すべてのデータが削除されました。<br />
                        3秒後にログイン画面に移動します...
                    </div>
                ) : (
                    <>
                        <div class="warning-box">
                            <div class="warning-icon">⚠️</div>
                            <div class="warning-text">削除前に必ずお読みください</div>
                            <div class="warning-description">
                                <p>この操作により、以下のすべてのデータが<strong>完全に削除</strong>されます：</p>
                                <ul>
                                    <li>全ユーザーアカウント</li>
                                    <li>全ロール及び権限設定</li>
                                    <li>全イベント情報</li>
                                    <li>全参加グループ</li>
                                    <li>全チケット</li>
                                    <li>その他すべてのシステムデータ</li>
                                </ul>
                                <p><strong>注意：この操作は取り消すことができません。</strong></p>
                                <p>削除後、システムは初期状態にリセットされ、新しいユーザーを登録してください。</p>
                            </div>
                        </div>

                        {error && <div class="error">{error}</div>}

                        <form onSubmit={handleDeleteAllData}>
                            <div class="form-group">
                                <label for="password">確認用パスワード</label>
                                <div class="password-input">
                                    <input
                                        type={showPassword ? "text" : "password"}
                                        id="password"
                                        value={password}
                                        placeholder="パスワードを入力してください"
                                        disabled={isDeleting}
                                        onInput={(e) => setPassword(e.currentTarget.value)}
                                    />
                                    <button
                                        type="button"
                                        class="password-toggle"
                                        onClick={() => setShowPassword(!showPassword)}
                                        disabled={isDeleting}
                                    >
                                        {showPassword ? "非表示" : "表示"}
                                    </button>
                                </div>
                            </div>

                            <div class="button-group">
                                <button type="button" class="btn-secondary" onClick={handleCancel} disabled={isDeleting}>
                                    キャンセル
                                </button>
                                <button type="submit" class="btn-danger" disabled={isDeleting || !password}>
                                    {isDeleting ? (
                                        <span class="loading-text">
                                            <span class="loading"></span>
                                            削除中...
                                        </span>
                                    ) : (
                                        "すべてのデータを削除する"
                                    )}
                                </button>
                            </div>
                        </form>
                    </>
                )}
            </div>
        </Layout>
    );
}

