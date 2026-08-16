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
            <link rel="stylesheet" href="/css/users.css" />
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

