import { useState } from "preact/hooks";

// SvelteKit routes/login/+page.svelte から移行
export default function Index() {
    const [username, setUsername] = useState("");
    const [password, setPassword] = useState("");
    const [errorMessage, setErrorMessage] = useState("");
    const [isLoading, setIsLoading] = useState(false);

    async function handleSubmit(e: Event) {
        e.preventDefault();
        if (!username) {
            setErrorMessage("ユーザー名を入力してください。");
            return;
        }
        if (!password) {
            setErrorMessage("パスワードを入力してください。");
            return;
        }

        setIsLoading(true);
        setErrorMessage("");

        try {
            const response = await fetch("/api/user/LoginByUserName", {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({ UserName: username, Password: password }),
            });

            if (response.ok) {
                window.location.href = "/";
                return;
            }

            const errorData = await response.json();
            setErrorMessage(errorData?.message || "ログインに失敗しました。");
        } catch (error) {
            console.error("ログインリクエストエラー:", error);
            setErrorMessage("ネットワークエラーが発生しました。");
        } finally {
            setIsLoading(false);
        }
    }

    return (
        <div>
            <style>{`
                .login-container {
                    display: flex;
                    flex-direction: column;
                    align-items: center;
                    padding: 20px;
                    border: 1px solid #ccc;
                    border-radius: 5px;
                    width: 300px;
                    margin: 50px auto;
                    position: relative;
                }
                .login-container h2 { margin-bottom: 20px; }
                .form-group { margin-bottom: 15px; width: 100%; }
                .form-group label { display: block; margin-bottom: 5px; }
                .form-group input {
                    width: 100%;
                    padding: 10px;
                    border: 1px solid #ddd;
                    border-radius: 3px;
                    box-sizing: border-box;
                }
                .login-container button {
                    padding: 10px 20px;
                    background-color: #007bff;
                    color: white;
                    border: none;
                    border-radius: 3px;
                    cursor: pointer;
                    transition: background-color 0.3s ease;
                }
                .login-container button:disabled { background-color: #ccc; cursor: not-allowed; }
                .error-message { color: red; margin-top: 10px; }
            `}</style>
            <div class="login-container">
                <h2>ログイン</h2>
                {errorMessage && <p class="error-message">{errorMessage}</p>}
                <form onSubmit={handleSubmit}>
                    <div class="form-group">
                        <label for="username">ユーザー名</label>
                        <input
                            type="text"
                            id="username"
                            value={username}
                            onInput={(e) => setUsername(e.currentTarget.value)}
                        />
                    </div>
                    <div class="form-group">
                        <label for="password">パスワード</label>
                        <input
                            type="password"
                            id="password"
                            value={password}
                            onInput={(e) => setPassword(e.currentTarget.value)}
                        />
                    </div>
                    <button type="submit" disabled={isLoading}>
                        {isLoading ? "ログイン中..." : "ログイン"}
                    </button>
                </form>
            </div>
        </div>
    );
}
