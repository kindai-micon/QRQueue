import { useState } from "preact/hooks";
import Layout from "@/Shared/Layout";

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
        <Layout chrome="header">
            <link rel="stylesheet" href="/css/login.css" />
            <div class="login-container">
                <h2>ログイン</h2>
                {errorMessage && <p class="error">{errorMessage}</p>}
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
                    <button type="submit" class="btn-primary" disabled={isLoading}>
                        {isLoading ? "ログイン中..." : "ログイン"}
                    </button>
                </form>
            </div>
        </Layout>
    );
}

