import { useState, useEffect } from "preact/hooks";
import type { HubConnection } from "@microsoft/signalr";
import Layout from "@/Shared/Layout";
import type { QueueView } from "@/Shared/queue";

type Model = {
    eventId: string; // eventDisplayId
};

// 投影用画面(設計§9.1 /display/[eventid]、旧 view 置換)。
// 現在呼び出し中を大型表示 + 直近履歴。呼び出し時に強調アニメーションのみ。
export default function Display({ model }: { model: Model }) {
    const [queue, setQueue] = useState<QueueView | null>(null);
    const [eventName, setEventName] = useState<string>("");
    const [history, setHistory] = useState<number[]>([]);
    const [flash, setFlash] = useState(false);
    const [denied, setDenied] = useState(false);

    useEffect(() => {
        (async () => {
            try {
                const res = await fetch(`/api/entry/${model.eventId}`);
                if (res.ok) {
                    const data = await res.json();
                    setEventName(data.eventName ?? "");
                }
            } catch {
                // イベント名は表示上の補助のみ
            }
        })();
    }, [model.eventId]);

    useEffect(() => {
        let connection: HubConnection | null = null;
        let disposed = false;
        let poll: number | undefined;
        let lastCalling: number | null = null;

        async function load() {
            try {
                const res = await fetch(`/api/call/queue/${model.eventId}`);
                if (res.status === 401 || res.status === 403) {
                    setDenied(true);
                    return;
                }
                if (!res.ok) return;
                setDenied(false);
                const data: QueueView = await res.json();
                setQueue(data);

                const current = data.callingGroup[0]?.number ?? null;
                if (current !== lastCalling) {
                    if (current != null) {
                        // 新しい呼び出しに切り替わった → 直近履歴へ積み、強調アニメ再生
                        setHistory((h) => [current, ...h.filter((n) => n !== current)].slice(0, 6));
                        setFlash(true);
                        setTimeout(() => setFlash(false), 1600);
                    }
                    lastCalling = current;
                }
            } catch (err) {
                console.error("キュー情報の取得に失敗:", err);
            }
        }

        (async () => {
            try {
                const { HubConnectionBuilder, HttpTransportType } = await import("@microsoft/signalr");
                connection = new HubConnectionBuilder()
                    .withUrl("/api/queueHub", { skipNegotiation: true, transport: HttpTransportType.WebSockets })
                    .withAutomaticReconnect()
                    .build();
                connection.on("Called", load);
                connection.on("QueueChanged", load);
                connection.onreconnected(async () => {
                    await connection?.invoke("SetEvent", model.eventId);
                    await load();
                });
                await connection.start();
                if (disposed) {
                    await connection.stop();
                    return;
                }
                await connection.invoke("SetEvent", model.eventId);
            } catch (err) {
                console.error("SignalR connection setup error:", err);
            }
            await load();
            if (!disposed) {
                // CallController 側は SignalR を送らない運用でも追従できるようポーリング併用
                poll = window.setInterval(load, 10000);
            }
        })();

        return () => {
            disposed = true;
            if (poll) window.clearInterval(poll);
            connection?.stop().catch(() => { /* ignore */ });
        };
    }, [model.eventId]);

    const calling = queue?.callingGroup[0] ?? null;

    if (denied) {
        return (
            <Layout chrome="header">
                <link rel="stylesheet" href="/css/display.css" />
                <div class="display-denied">
                    <h1>表示できません</h1>
                    <p>
                        この画面は <code>CallView</code> 権限を持つアカウントでログインすると見られます。
                        <a href="/login">ログインへ</a>
                    </p>
                </div>
            </Layout>
        );
    }

    return (
        <Layout chrome="header">
            <link rel="stylesheet" href="/css/display.css" />
            <div class="display-screen">
                <div class="display-event">{eventName}</div>

                <div class={`display-now ${flash ? "display-flash" : ""}`}>
                    <div class="display-now-label">いま呼び出し中</div>
                    {calling ? (
                        <div class="display-now-number">{calling.number}</div>
                    ) : (
                        <div class="display-now-empty">——</div>
                    )}
                    {calling && (
                        <div class="display-now-people">{calling.people} 人の方、受付までお越しください</div>
                    )}
                </div>

                <div class="display-side">
                    <div class="display-block">
                        <div class="display-block-title">待ち</div>
                        <div class="display-block-value">{(queue?.waitingGroup.length ?? 0)}</div>
                    </div>
                    <div class="display-block">
                        <div class="display-block-title">割り込み待ち</div>
                        <div class="display-block-value">{(queue?.interruptedGroup.length ?? 0)}</div>
                    </div>
                    <div class="display-block">
                        <div class="display-block-title">プール人数</div>
                        <div class="display-block-value">{(queue?.peoplePool ?? 0)}</div>
                    </div>
                </div>

                {history.length > 0 && (
                    <div class="display-history">
                        <span class="display-history-label">直近の呼び出し</span>
                        {history.map((n, i) => (
                            <span key={`${n}-${i}`} class="display-history-item">{n}</span>
                        ))}
                    </div>
                )}
            </div>
        </Layout>
    );
}
