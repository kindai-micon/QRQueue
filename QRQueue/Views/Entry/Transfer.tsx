import { useState } from "preact/hooks";
import Layout from "@/Shared/Layout";

// チケット引き継ぎ画面(issue #75)。
// cookieを失った端末(機種変更・別ブラウザ等)で、元の端末に表示された
// ワンタイム引き継ぎコードを入力して既存の電子券を復元する。
// 引き継ぎが完了すると participantToken が新しい端末に付け替わるため、
// 元の端末では同じチケットを使えなくなる(重複利用の防止)。
export default function Transfer() {
    const [code, setCode] = useState("");
    const [error, setError] = useState<string | null>(null);
    const [busy, setBusy] = useState(false);

    async function handleSubmit(e: Event) {
        e.preventDefault();
        setError(null);

        if (!code.trim()) {
            setError("引き継ぎコードを入力してください。");
            return;
        }

        setBusy(true);
        try {
            const response = await fetch("/api/entry/transfer/complete", {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({ code: code.trim().toUpperCase() }),
            });

            if (response.ok) {
                const data = await response.json();
                window.location.href = `/ticket/${data.ticketDisplayId}`;
                return;
            }
            const message = await response.text();
            setError(message || "引き継ぎに失敗しました。");
        } catch (err) {
            console.error("チケット引き継ぎに失敗:", err);
            setError("通信エラーが発生しました。");
        } finally {
            setBusy(false);
        }
    }

    return (
        <Layout chrome="header">
            <link rel="stylesheet" href="/css/initial.css" />
            <div class="form-container">
                <h1>チケットの引き継ぎ</h1>
                <p>
                    別の端末(元の端末)で電子券画面の「別の端末へ引き継ぐ」から
                    <strong>引き継ぎコード</strong>を発行し、ここに入力してください。
                </p>
                <p style={{ fontSize: "0.85rem", color: "#666" }}>
                    ・引き継ぎコードの有効期限は10分です。<br />
                    ・引き継ぎ後は<strong>元の端末ではチケットを表示できなくなります</strong>。<br />
                    ・コードは1回だけ使用できます。
                </p>
                <form onSubmit={handleSubmit}>
                    <div class="form-group">
                        <label for="code" class="required-mark">引き継ぎコード</label>
                        <input
                            id="code"
                            type="text"
                            value={code}
                            placeholder="例: AB12CD34"
                            maxLength={8}
                            autoCapitalize="characters"
                            onInput={(e) => setCode(e.currentTarget.value.toUpperCase())}
                        />
                    </div>
                    {error && <div class="error">{error}</div>}
                    <button type="submit" class="btn-primary btn-block" disabled={busy}>
                        {busy ? "確認中..." : "引き継ぐ"}
                    </button>
                </form>
            </div>
        </Layout>
    );
}
