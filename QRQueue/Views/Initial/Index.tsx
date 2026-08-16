import { useState, useEffect } from "preact/hooks";

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
                    const data = await response.json();
                    if (data) {
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
            // IdentityError[] を読み取る
            const errors: { code: string; description: string }[] = await response.json();
            setError(errors.map((e) => e.description).join("\n"));
        } else {
            setError("不明なエラーが発生しました。");
            console.error(await response.text());
        }
    }

    return (
        <div>
            <style>{`
                body { margin: 0; font-family: sans-serif; }
                .form-container {
                    max-width: 400px; margin: 50px auto; padding: 20px;
                    background-color: white; border-radius: 10px;
                    box-shadow: 0 2px 10px rgba(0, 0, 0, 0.1); box-sizing: border-box;
                }
                .form-container h1 {
                    font-size: 1.5rem; font-weight: bold; text-align: center; margin-bottom: 20px;
                }
                .form-container label { display: block; font-size: 1rem; margin-bottom: 5px; }
                .required-mark::after { content: " *"; color: red; font-weight: bold; }
                .form-container input {
                    width: 100%; padding: 10px; margin-bottom: 15px;
                    border: 1px solid #ccc; border-radius: 5px;
                    font-size: 1rem; box-sizing: border-box;
                }
                .form-container .error { color: red; font-size: 0.875rem; margin-bottom: 10px; white-space: pre-wrap; }
                .form-container button {
                    width: 100%; padding: 12px; background-color: #007bff; color: white;
                    border: none; border-radius: 5px; font-size: 1rem; cursor: pointer;
                    box-sizing: border-box;
                }
                .form-container button:hover { background-color: #0056b3; }
            `}</style>
            <div class="form-container">
                <h1>初期ユーザー作成</h1>
                <form onSubmit={handleSubmit}>
                    <div>
                        <label for="passcode" class="required-mark">作成用パスコード</label>
                        <input id="passcode" type="text" value={passcode}
                            onInput={(e) => setPasscode(e.currentTarget.value)} />
                    </div>
                    <div>
                        <label for="username" class="required-mark">ユーザー名</label>
                        <input id="username" type="text" value={username}
                            onInput={(e) => setUsername(e.currentTarget.value)} />
                    </div>
                    <div>
                        <label for="email">メールアドレス(任意)</label>
                        <input id="email" type="text" value={email}
                            onInput={(e) => setEmail(e.currentTarget.value)} />
                    </div>
                    <div>
                        <label for="password" class="required-mark">パスワード</label>
                        <input id="password" type="password" value={password}
                            onInput={(e) => setPassword(e.currentTarget.value)} />
                    </div>
                    <div>
                        <label for="confirmPassword" class="required-mark">パスワードの確認</label>
                        <input id="confirmPassword" type="password" value={confirmPassword}
                            onInput={(e) => setConfirmPassword(e.currentTarget.value)} />
                    </div>
                    {error && <div class="error">{error}</div>}
                    <button type="submit">作成する</button>
                </form>
            </div>
        </div>
    );
}
