import { useState, useEffect } from "preact/hooks";
import Layout from "@/Shared/Layout";
import { readErrorMessage, type SendRole, type SendUser } from "@/Shared/api";

type Model = {
    username: string;
};

// SvelteKit routes/users/[username]/+page.svelte から移行
export default function Detail({ model }: { model: Model }) {
    const [user, setUser] = useState<SendUser | null>(null);
    const [loading, setLoading] = useState(true);
    const [error, setError] = useState<string | null>(null);
    const [newRoleName, setNewRoleName] = useState("");
    const [availableRoles, setAvailableRoles] = useState<SendRole[]>([]);
    const [showModal, setShowModal] = useState(false);
    const [roleToRemove, setRoleToRemove] = useState<SendRole | null>(null);
    const [resetPassword, setResetPassword] = useState("");
    const [resetConfirmPassword, setResetConfirmPassword] = useState("");
    const [showResetPassword, setShowResetPassword] = useState(false);
    const [resetError, setResetError] = useState<string | null>(null);
    const [resetSubmitting, setResetSubmitting] = useState(false);

    useEffect(() => {
        (async () => {
            if (!model.username) {
                setError("ユーザー名が取得できませんでした");
                setLoading(false);
                return;
            }

            try {
                const res = await fetch(`/api/user/UserInfo?username=${encodeURIComponent(model.username)}`);
                if (!res.ok) throw new Error(`Error ${res.status}`);
                setUser(await res.json() as SendUser);

                const rolesRes = await fetch("/api/Role/RoleList");
                if (!rolesRes.ok) throw new Error(`ロール一覧取得失敗: ${rolesRes.status}`);
                setAvailableRoles(await rolesRes.json() as SendRole[]);
            } catch (e) {
                setError((e as Error).message);
            } finally {
                setLoading(false);
            }
        })();
    }, [model.username]);

    function getUnassignedRoles(): SendRole[] {
        if (!user) return [];
        const assignedNames = new Set(user.roles.map((r) => r.name));
        return availableRoles.filter((r) => !assignedNames.has(r.name));
    }

    async function addRole(e: Event) {
        e.preventDefault();
        if (!newRoleName || !user) return;

        const response = await fetch("/api/User/AddRole", {
            method: "PUT",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({ userName: user.userName, roleName: newRoleName }),
        });

        if (response.ok) {
            const addedRole = availableRoles.find((r) => r.name === newRoleName);
            if (addedRole) {
                setUser({ ...user, roles: [...user.roles, addedRole] });
            }
            setNewRoleName("");
        } else {
            const text = await response.text();
            alert("ロール追加失敗: " + text);
        }
    }

    async function removeConfirmedRole() {
        if (!user || !roleToRemove) return;

        const response = await fetch("/api/User/RemoveRole", {
            method: "PUT",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({ userName: user.userName, roleName: roleToRemove.name }),
        });

        if (response.ok) {
            setUser({ ...user, roles: user.roles.filter((r) => r.name !== roleToRemove.name) });
        } else {
            const text = await response.text();
            alert("ロール削除失敗: " + text);
        }

        setRoleToRemove(null);
        setShowModal(false);
    }

    // 管理者によるパスワード再設定(現在のパスワードは不要)
    async function resetPasswordSubmit(e: Event) {
        e.preventDefault();
        if (!user) return;
        setResetError(null);

        if (!resetPassword || !resetConfirmPassword) {
            setResetError("すべての項目を入力してください。");
            return;
        }
        if (resetPassword !== resetConfirmPassword) {
            setResetError("パスワードが一致しません。");
            return;
        }
        setResetSubmitting(true);

        try {
            const response = await fetch("/api/user/ResetPassword", {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({
                    userName: user.userName,
                    newPassword: resetPassword,
                    confirmPassword: resetConfirmPassword,
                }),
            });

            if (response.ok) {
                setResetPassword("");
                setResetConfirmPassword("");
                alert("パスワードを変更しました。");
            } else {
                // 統一エラー形式 ApiMessage(IdentityError もサーバー側で結合済み)
                setResetError(await readErrorMessage(response));
            }
        } catch (err) {
            console.log(err);
            setResetError("通信エラーが発生しました。");
        } finally {
            setResetSubmitting(false);
        }
    }

    return (
        <Layout title="ユーザー詳細 | QRQueue">
            <link rel="stylesheet" href="/css/users-detail.css" />
            {showModal && roleToRemove && (
                <div class="modal-overlay">
                    <div class="modal">
                        <div class="modal-title">確認</div>
                        <div class="modal-content">
                            ロール「<strong>{roleToRemove.name}</strong>」を削除してもよろしいですか？
                        </div>
                        <div class="modal-buttons">
                            <button class="btn-danger btn-sm" onClick={removeConfirmedRole}>削除</button>
                            <button class="btn-secondary btn-sm" onClick={() => { setShowModal(false); setRoleToRemove(null); }}>キャンセル</button>
                        </div>
                    </div>
                </div>
            )}

            <div class="container">
                <a href="/users" class="back-link">← ユーザー一覧に戻る</a>
                <h2>{model.username} の設定</h2>

                {loading ? (
                    <p>読み込み中...</p>
                ) : error ? (
                    <p class="error">エラー: {error}</p>
                ) : !user ? (
                    <p>ユーザー情報が取得できませんでした。</p>
                ) : (
                    <>
                        <div class="section">
                            <div class="label">ユーザー名:</div>
                            <div>{user.userName}</div>
                        </div>
                        <div class="section">
                            <div class="label">パスワード再設定:</div>
                            {resetError && <p class="error">{resetError}</p>}
                            <form style={{ marginTop: "8px" }} onSubmit={resetPasswordSubmit}>
                                <div class="form-group">
                                    <label for="resetPassword">新しいパスワード</label>
                                    <div class="password-input">
                                        <input
                                            type={showResetPassword ? "text" : "password"}
                                            id="resetPassword"
                                            value={resetPassword}
                                            autoComplete="new-password"
                                            onInput={(e) => setResetPassword(e.currentTarget.value)}
                                        />
                                        <button type="button" class="password-toggle" onClick={() => setShowResetPassword(!showResetPassword)}>
                                            {showResetPassword ? "非表示" : "表示"}
                                        </button>
                                    </div>
                                </div>
                                <div class="form-group">
                                    <label for="resetConfirmPassword">新しいパスワード（確認）</label>
                                    <div class="password-input">
                                        <input
                                            type={showResetPassword ? "text" : "password"}
                                            id="resetConfirmPassword"
                                            value={resetConfirmPassword}
                                            autoComplete="new-password"
                                            onInput={(e) => setResetConfirmPassword(e.currentTarget.value)}
                                        />
                                    </div>
                                </div>
                                <button type="submit" class="btn-primary btn-sm" disabled={resetSubmitting}>
                                    {resetSubmitting ? "変更中..." : "パスワードを変更"}
                                </button>
                            </form>
                        </div>
                        <div class="section">
                            <div class="label">ロール一覧:</div>

                            {user.roles.length === 0 ? (
                                <div>ロールなし</div>
                            ) : (
                                user.roles.map((role) => (
                                    <div class="role" key={role.name}>
                                        <div class="role-name">{role.name}</div>
                                        <div class="authority-list">
                                            権限:
                                            <ul>
                                                {role.authorities.map((authority) => (
                                                    <li key={authority.name}>{authority.name}</li>
                                                ))}
                                            </ul>
                                        </div>
                                        <button class="btn-danger btn-sm" onClick={() => { setRoleToRemove(role); setShowModal(true); }}>削除</button>
                                    </div>
                                ))
                            )}

                            {getUnassignedRoles().length > 0 ? (
                                <form style={{ marginTop: "16px" }} onSubmit={addRole}>
                                    <select
                                        value={newRoleName}
                                        onChange={(e) => setNewRoleName(e.currentTarget.value)}
                                        style={{ padding: "6px", marginRight: "8px", border: "1px solid #ccc", borderRadius: "4px" }}
                                    >
                                        <option value="" disabled>ロールを選択</option>
                                        {getUnassignedRoles().map((role) => (
                                            <option value={role.name} key={role.name}>{role.name}</option>
                                        ))}
                                    </select>
                                    <button
                                        type="submit"
                                        class="btn-primary btn-sm"
                                        disabled={!newRoleName}
                                    >
                                        ロール追加
                                    </button>
                                </form>
                            ) : (
                                <div style={{ marginTop: "16px" }}>追加可能なロールはありません</div>
                            )}
                        </div>
                    </>
                )}
            </div>
        </Layout>
    );
}

