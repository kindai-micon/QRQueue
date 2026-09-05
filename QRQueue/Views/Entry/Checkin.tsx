import { useState, useEffect } from "preact/hooks";
import Layout from "@/Shared/Layout";
import { readErrorMessage, type CheckinResult, type EventInfoView, type RestoreResult } from "@/Shared/api";

type Model = {
    eventDisplayId: string;
};

// チェックインQRの飛び先(設計§9.1 /checkin/[eventid] §4.6)。
// 参加者cookie を添えて POST /api/entry/checkin を呼ぶ。
// 失敗時は「まだ確定できません」を表示し、グループの状態は一切変化しない。
export default function Checkin({ model }: { model: Model }) {
    const [ev, setEv] = useState<EventInfoView | null>(null);
    const [notFound, setNotFound] = useState(false);
    const [result, setResult] = useState<CheckinResult | null>(null);
    const [error, setError] = useState<string | null>(null);
    const [busy, setBusy] = useState(false);
    const [ticketUrl, setTicketUrl] = useState<string | null>(null);

    useEffect(() => {
        (async () => {
            try {
                const res = await fetch(`/api/entry/${model.eventDisplayId}`);
                if (res.ok) {
                    const data: EventInfoView = await res.json();
                    setEv(data);
                } else {
                    setNotFound(true);
                }
            } catch (err) {
                console.error("イベント情報の取得に失敗:", err);
                setNotFound(true);
            }
        })();
    }, [model.eventDisplayId]);

    async function checkin() {
        setBusy(true);
        setError(null);
        try {
            const res = await fetch("/api/entry/checkin", {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({ eventDisplayId: model.eventDisplayId }),
            });
            if (res.ok) {
                const data: CheckinResult = await res.json();
                setResult(data);
                // 成功時は restore で自分の電子券へ戻る導線を付ける(§6.1)
                try {
                    const restore = await fetch("/api/entry/restore", {
                        method: "POST",
                        headers: { "Content-Type": "application/json" },
                        body: JSON.stringify({ eventDisplayId: model.eventDisplayId }),
                    });
                    if (restore.ok) {
                        const r: RestoreResult = await restore.json();
                        setTicketUrl(`/ticket/${r.ticketDisplayId}`);
                        setTimeout(() => {
                            window.location.href = `/ticket/${r.ticketDisplayId}`;
                        }, 5000);
                    }
                } catch {
                    // 復元失敗は導線なしのまま(完了表示は維持)
                }
                return;
            }
            setError(await readErrorMessage(res));
        } catch (err) {
            console.error("チェックインに失敗:", err);
            setError("通信エラーが発生しました");
        } finally {
            setBusy(false);
        }
    }

    return (
        <Layout chrome="header" title="チェックイン | QRQueue">
            <link rel="stylesheet" href="/css/checkin.css" />
            <div class="checkin-container">
                {notFound && (
                    <div class="checkin-card checkin-card-error">
                        <h1>❌</h1>
                        <h2>イベントが見つかりません</h2>
                        <p>掲示されているQRが古い可能性があります。受付のスタッフにお尋ねください。</p>
                    </div>
                )}

                {ev && !result && (
                    <div class="checkin-card">
                        <div class="checkin-kind">チェックイン</div>
                        <h1 class="checkin-event">{ev.eventName}</h1>
                        <p class="checkin-desc">
                            メンバーがそろった代表者の方は、下のボタンで受付を確定してください。
                        </p>

                        {error && (
                            <div class="checkin-error">
                                <h2>まだ確定できません</h2>
                                <p>{error}</p>
                            </div>
                        )}

                        <button class="checkin-btn" onClick={checkin} disabled={busy}>
                            {busy ? "送信中..." : "受付に到着したことを伝える"}
                        </button>
                        <p class="checkin-note">
                            この操作は呼び出されているグループの代表者本人にのみ有効です。
                        </p>
                    </div>
                )}

                {ev && result && (
                    <div class="checkin-card checkin-card-done">
                        <div class="checkin-done-icon">✓</div>
                        <h1>受付完了</h1>
                        <div class="checkin-done-label">グループ番号</div>
                        <div class="checkin-done-number">{result.groupNumber}</div>
                        <p>受け渡し場所までお越しください。</p>
                        {ticketUrl && (
                            <a class="checkin-ticket-link" href={ticketUrl}>
                                電子券を見る(5秒後に自動で移動します)
                            </a>
                        )}
                    </div>
                )}
            </div>
        </Layout>
    );
}
