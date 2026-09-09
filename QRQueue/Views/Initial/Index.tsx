import { useState, useEffect } from "preact/hooks";
import Layout from "@/Shared/Layout";
import { readErrorMessage } from "@/Shared/api";

// SvelteKit routes/initial/+page.svelte から移行
export default function Index() {
    const [passcode, setPasscode] = useState("");
    const [username, setUsername] = useState("");
    const [password, setPassword] = useState("");
    const [confirmPassword, setConfirmPassword] = useState("");
    const [email, setEmail] = useState("");
    const [error, setError] = useState("");

    useEffect(() => {
        (async () => {
            try {
                const response = await fetch("/api/user/HasUser", { method: "GET" });
                if (response.ok) {
                    const hasUser: boolean = await response.json();
                    if (hasUser) {
                        window.location.href = "/login";
                    } else {
                        // パスコードをバックエンドのコンソールに出力
                        await fetch("/api/user/GetPasscode");
                    }
                }
            } catch (err) {
                console.error(err);
            }
        })();
    }, []);

    async function handleSubmit(e: Event) {
        e.preventDefault();
        setError("");

        if (password !== confirmPassword) {
            setError("パスワードが一致しません。");
            return;
        }
        if (!passcode || !username || !password || !confirmPassword) {
            setError("すべての項目を入力してください。");
            return;
        }

        const response = await fetch("/api/user/InitialRegister", {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({
                Passcode: passcode,
                UserName: username,
                Password: password,
                ConfirmPassword: confirmPassword,
                Email: email,
            }),
        });

        if (response.ok) {
            window.location.href = "/login";
        } else if (response.status === 400) {
            // 統一エラー形式 ApiMessage { message }
            setError(await readErrorMessage(response));
        } else {
            setError("不明なエラーが発生しました。");
            console.error(await response.text());
        }
    }

    return (
        <Layout chrome="header" title="初期ユーザー登録 | QRQueue">
            <link rel="stylesheet" href="/css/initial.css" />
            <div class="form-container">
                <h1>初期ユーザー作成</h1>
                <form onSubmit={handleSubmit}>
                    <div class="form-group">
                        <label for="passcode" class="required-mark">作成用パスコード</label>
                        <input id="passcode" type="text" value={passcode}
                            onInput={(e) => setPasscode(e.currentTarget.value)} />
                    </div>
                    <div class="form-group">
                        <label for="username" class="required-mark">ユーザー名</label>
                        <input id="username" type="text" value={username}
                            onInput={(e) => setUsername(e.currentTarget.value)} />
                    </div>
                    <div class="form-group">
                        <label for="email">メールアドレス(任意)</label>
                        <input id="email" type="text" value={email}
                            onInput={(e) => setEmail(e.currentTarget.value)} />
                    </div>
                    <div class="form-group">
                        <label for="password" class="required-mark">パスワード</label>
                        <input id="password" type="password" value={password}
                            onInput={(e) => setPassword(e.currentTarget.value)} />
                    </div>
                    <div class="form-group">
                        <label for="confirmPassword" class="required-mark">パスワードの確認</label>
                        <input id="confirmPassword" type="password" value={confirmPassword}
                            onInput={(e) => setConfirmPassword(e.currentTarget.value)} />
                    </div>
                    {error && <div class="error">{error}</div>}
                    <button type="submit" class="btn-primary btn-block">作成する</button>
                </form>
            </div>
        </Layout>
    );
}

