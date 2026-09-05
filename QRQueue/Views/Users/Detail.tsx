import { useState, useEffect } from "preact/hooks";
import Layout from "@/Shared/Layout";
import type { SendRole, SendUser } from "@/Shared/api";

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

