import { useState, useEffect } from "preact/hooks";
import Layout from "@/Shared/Layout";

type SendUser = {
    userName: string;
    roles: { name: string }[];
};

// SvelteKit routes/users/+page.svelte から移行
export default function Index() {
    const [users, setUsers] = useState<SendUser[] | null>(null);
    const [error, setError] = useState<string | null>(null);
    const [showPassword, setShowPassword] = useState(false);
    const [newUserName, setNewUserName] = useState("");
    const [newPassword, setNewPassword] = useState("");
    const [newEmail, setNewEmail] = useState("");
    const [registerError, setRegisterError] = useState<string | null>(null);
    const [errorMessages, setErrorMessages] = useState<string[]>([]);
    const [showModal, setShowModal] = useState(false);
    const [targetUserToDelete, setTargetUserToDelete] = useState<string | null>(null);
    const [modalError, setModalError] = useState<string | null>(null);

    useEffect(() => {
        loadUsers();
    }, []);

    async function loadUsers() {
        setError(null);
        try {
            const res = await fetch("/api/user/UserList");
            if (!res.ok) throw new Error(`Error ${res.status}`);
            setUsers(await res.json());
        } catch (e) {
            setError((e as Error).message);
        }
    }

    async function registerUser(e: Event) {
        e.preventDefault();
        setRegisterError(null);

        if (!newUserName || !newPassword) {
            setRegisterError("ユーザー名とパスワードは必須です。");
            return;
        }
        setErrorMessages([]);

        try {
            const response = await fetch("/api/user/register", {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({
                    userName: newUserName,
                    password: newPassword,
                    email: newEmail,
                }),
            });

            if (response.ok) {
                setNewUserName("");
                setNewPassword("");
                setNewEmail("");
                alert("ユーザーが追加されました。");
                loadUsers();
            } else {
                const data = await response.json();
                if (Array.isArray(data)) {
                    // IdentityError[] の形式で返ってきた場合
                    setErrorMessages(data.map((e: { description: string }) => e.description));
                } else {
                    setErrorMessages([data?.message || "ユーザー登録に失敗しました。"]);
                }
            }
        } catch (err) {
            console.log(err);
            setErrorMessages(["通信エラーが発生しました。"]);
        }
    }

    function confirmDelete(userName: string) {
        setTargetUserToDelete(userName);
        setShowModal(true);
        setModalError(null);
    }

    async function executeDelete() {
        if (!targetUserToDelete) return;

        try {
            const response = await fetch("/api/user/DeleteUser", {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify(targetUserToDelete),
            });

            if (response.ok) {
                setUsers((prev) => prev?.filter((u) => u.userName !== targetUserToDelete) ?? null);
                closeModal();
            } else {
                const data = await response.json();
                setModalError(data?.message || "削除に失敗しました。");
            }
        } catch (err) {
            setModalError("通信エラーが発生しました。");
        }
    }

    function closeModal() {
        setShowModal(false);
        setTargetUserToDelete(null);
        setModalError(null);
    }

    return (
        <Layout>
            <style>{`
                .container { max-width: 800px; margin: 20px auto; padding: 10px; }
                .title { font-size: 1.5rem; font-weight: bold; margin-bottom: 16px; }
                .user-list { list-style: none; padding: 0; }
                .user-item {
                    padding: 12px; border: 1px solid #ccc; border-radius: 6px;
                    margin-bottom: 10px; display: flex; justify-content: space-between;
                    align-items: center; transition: background-color 0.2s ease-in-out;
                }
                .user-item:hover { background-color: #f5f5f5; }
                .user-info { flex-grow: 1; cursor: pointer; }
                .username { font-weight: bold; }
                .roles { font-size: 0.9rem; color: #666; margin-top: 4px; }
                .error, .error-message { color: red; margin-top: 10px; margin-bottom: 10px; }
                .register-container {
                    border: 1px solid #ccc; border-radius: 5px; padding: 20px;
                    margin: 40px auto; max-width: 800px; background-color: #fafafa;
                }
                .register-container h2 { margin-bottom: 20px; }
                .form-group { margin-bottom: 15px; }
                .register-container label { display: block; margin-bottom: 5px; }
                .register-container input[type="text"],
                .register-container input[type="password"],
                .register-container input[type="email"] {
                    width: 100%; padding: 10px; border: 1px solid #ddd;
                    border-radius: 3px; box-sizing: border-box;
                }
                .register-container button {
                    padding: 10px 20px; background-color: #007bff; color: white;
                    border: none; border-radius: 3px; cursor: pointer;
                    transition: background-color 0.3s ease;
                }
                .register-container button:disabled { background-color: #ccc; cursor: not-allowed; }
                .password-input { position: relative; }
                .password-input input { width: 100%; padding-right: 80px; }
                .toggle-text {
                    position: absolute; right: 10px; top: 50%; transform: translateY(-50%);
                    background: none; border: none; color: #007bff; font-weight: bold;
                    cursor: pointer; font-size: 0.9rem; padding: 0;
                }
                .delete-button {
                    background-color: #dc3545; color: white; border: none;
                    padding: 6px 12px; border-radius: 4px; cursor: pointer;
                    transition: background-color 0.2s ease;
                }
                .delete-button:hover { background-color: #c82333; }
                .modal-overlay {
                    position: fixed; top: 0; left: 0; width: 100%; height: 100%;
                    background-color: rgba(0, 0, 0, 0.4); display: flex;
                    justify-content: center; align-items: center; z-index: 1000;
                }
                .modal {
                    background-color: #fff; padding: 20px; border-radius: 8px;
                    width: 90%; max-width: 400px; box-shadow: 0 2px 10px rgba(0,0,0,0.3);
                }
                .modal h3 { margin-top: 0; }
                .modal-buttons { margin-top: 20px; display: flex; justify-content: flex-end; gap: 10px; }
                .modal-error { color: red; margin-top: 10px; }
            `}</style>
            {showModal && (
                <div class="modal-overlay">
                    <div class="modal">
                        <h3>ユーザー削除の確認</h3>
                        <p>「{targetUserToDelete}」を削除してもよろしいですか？</p>
                        {modalError && <p class="modal-error">{modalError}</p>}
                        <div class="modal-buttons">
                            <button class="delete-button" onClick={executeDelete}>削除する</button>
                            <button onClick={closeModal}>キャンセル</button>
                        </div>
                    </div>
                </div>
            )}

            <div class="container">
                <div class="title">ユーザー一覧</div>
                <div class="register-container">
                    <h2>新規ユーザー追加</h2>
                    {registerError && <p class="error-message">{registerError}</p>}
                    <form onSubmit={registerUser}>
                        <div class="form-group">
                            <label for="newUserName">ユーザー名</label>
                            <input type="text" id="newUserName" value={newUserName}
                                onInput={(e) => setNewUserName(e.currentTarget.value)} />
                        </div>
                        <div class="form-group">
                            <label for="newPassword">パスワード</label>
                            <div class="password-input">
                                <input
                                    type={showPassword ? "text" : "password"}
                                    id="newPassword"
                                    value={newPassword}
                                    onInput={(e) => setNewPassword(e.currentTarget.value)}
                                />
                                <button type="button" class="toggle-text" onClick={() => setShowPassword(!showPassword)}>
                                    {showPassword ? "非表示" : "表示"}
                                </button>
                            </div>
                        </div>
                        <div class="form-group">
                            <label for="newEmail">メールアドレス（任意）</label>
                            <input type="email" id="newEmail" value={newEmail}
                                onInput={(e) => setNewEmail(e.currentTarget.value)} />
                        </div>
                        {errorMessages.length > 0 && (
                            <ul class="error-message">
                                {errorMessages.map((msg, i) => <li key={i}>{msg}</li>)}
                            </ul>
                        )}
                        <button type="submit">登録</button>
                    </form>
                </div>

                {users === null && !error && <p>読み込み中...</p>}
                {error && <p class="error">エラー: {error}</p>}
                {users !== null && users.length === 0 && <p>ユーザーが見つかりませんでした。</p>}
                {users !== null && users.length > 0 && (
                    <ul class="user-list">
                        {users.map((user) => (
                            <li class="user-item" key={user.userName}>
                                <div class="user-info">
                                    <a href={`/users/${encodeURIComponent(user.userName)}`} class="username">
                                        {user.userName}
                                    </a>
                                    <div class="roles">
                                        ロール: {user.roles.map((r) => r.name).join(", ")}
                                    </div>
                                </div>
                                <button class="delete-button" onClick={() => confirmDelete(user.userName)}>削除</button>
                            </li>
                        ))}
                    </ul>
                )}
            </div>
        </Layout>
    );
}
