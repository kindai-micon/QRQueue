import { useState, useEffect } from "preact/hooks";
import type { HubConnection } from "@microsoft/signalr";
import Layout from "@/Shared/Layout";
import { groupStatusLabel, readErrorMessage, type EventInfoView, type GroupView, type QueueView } from "@/Shared/api";

type Model = {
    eventId: string; // eventDisplayId
};

// 呼び出しコンソール(設計§9.1 /event/[eventid]/call、旧 execute 置換)。
// 操作は「受付開閉」「次を呼ぶ」「再呼び出し」のみ。完了は参加者のチェックインで確定するため
// 完了ボタンは置かない(§4.6)。
export default function Call({ model }: { model: Model }) {
    const [ev, setEv] = useState<EventInfoView | null>(null);
    const [queue, setQueue] = useState<QueueView | null>(null);
    const [message, setMessage] = useState<string | null>(null);
    const [error, setError] = useState<string | null>(null);
    const [busy, setBusy] = useState(false);
    const [denied, setDenied] = useState(false);

    async function loadEvent() {
        try {
            const res = await fetch(`/api/entry/${model.eventId}`);
            if (res.ok) setEv(await res.json() as EventInfoView);
        } catch (err) {
            console.error("イベント情報の取得に失敗:", err);
        }
    }

    async function loadQueue() {
        try {
            const res = await fetch(`/api/call/queue/${model.eventId}`);
            if (res.status === 401 || res.status === 403) {
                setDenied(true);
                return;
            }
            if (res.ok) {
                setDenied(false);
                setQueue(await res.json());
            } else {
                setError(await readErrorMessage(res));
            }
        } catch (err) {
            console.error("キュー情報の取得に失敗:", err);
        }
    }

    useEffect(() => {
        loadEvent();

        let connection: HubConnection | null = null;
        let disposed = false;
        let poll: number | undefined;

        (async () => {
            try {
                const { HubConnectionBuilder, HttpTransportType } = await import("@microsoft/signalr");
                connection = new HubConnectionBuilder()
                    .withUrl("/api/queueHub", { skipNegotiation: true, transport: HttpTransportType.WebSockets })
                    .withAutomaticReconnect()
                    .build();
                connection.on("QueueChanged", () => { loadQueue(); loadEvent(); });
                connection.on("Called", loadQueue);
                connection.on("UpdateStatus", () => { loadQueue(); loadEvent(); });
                connection.onreconnected(async () => {
                    await connection?.invoke("SetEvent", model.eventId);
                    await loadQueue();
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
            await loadQueue();
            if (!disposed) {
                poll = window.setInterval(loadQueue, 5000);
            }
        })();

        return () => {
            disposed = true;
            if (poll) window.clearInterval(poll);
            connection?.stop().catch(() => { /* ignore */ });
        };
        // eslint-disable-next-line react-hooks/exhaustive-deps
    }, [model.eventId]);

    async function action(name: string, fn: () => Promise<Response>, okMessage?: string) {
        setBusy(true);
        setError(null);
        setMessage(null);
        try {
            const res = await fn();
            if (res.ok) {
                if (name === "next" && res.status === 204) {
                    setMessage("呼び出せるグループはありません");
                } else {
                    setMessage(okMessage ?? "反映しました");
                }
            } else if (res.status === 404 && name === "again") {
                setError("現在呼び出し中のグループがありません");
            } else {
                setError(await readErrorMessage(res));
            }
            await Promise.all([loadQueue(), loadEvent()]);
        } catch (err) {
            console.error("操作に失敗:", err);
            setError("通信エラーが発生しました");
        } finally {
            setBusy(false);
        }
    }

    const groupTable = (groups: GroupView[]) => (
        <table class="data-table">
            <thead>
                <tr><th>番号</th><th>人数</th><th>状態</th></tr>
            </thead>
            <tbody>
                {groups.map((g, i) => (
                    <tr key={`${g.number}-${i}`}>
                        <td>{g.number}</td>
                        <td>{g.people}</td>
                        <td>{groupStatusLabel(g.status)}</td>
                    </tr>
                ))}
                {groups.length === 0 && <tr><td colSpan={3}>—</td></tr>}
            </tbody>
        </table>
    );

    return (
        <Layout title="呼び出しコンソール | QRQueue">
            <link rel="stylesheet" href="/css/call.css" />
            <div class="call-container">
                <div class="page-title">呼び出しコンソール: {ev?.eventName ?? "..."}</div>

                {denied && (
                    <div class="call-denied">
                        このイベントのキューを見るには <code>CallView</code> 権限が必要です。
                    </div>
                )}

                <div class="call-status-row">
                    <span class={`call-reception ${ev?.isOpen ? "call-reception-open" : "call-reception-closed"}`}>
                        受付: {ev?.status ?? "..."}
                    </span>
                    <button
                        class="btn-primary btn-sm"
                        disabled={busy || !model.eventId}
                        onClick={() => action("open", () => fetch(`/api/call/open/${model.eventId}`, { method: "PUT" }), "受付を開始しました")}
                    >
                        受付開始
                    </button>
                    <button
                        class="btn-danger btn-sm"
                        disabled={busy}
                        onClick={() => action("close", () => fetch(`/api/call/close/${model.eventId}`, { method: "PUT" }), "受付を終了しました")}
                    >
                        受付終了
                    </button>
                </div>

                <div class="call-actions">
                    <button
                        class="call-next"
                        disabled={busy}
                        onClick={() => action("next", () => fetch(`/api/call/next/${model.eventId}`, { method: "PUT" }))}
                    >
                        ▶ 次を呼ぶ
                    </button>
                    <button
                        class="call-again"
                        disabled={busy}
                        onClick={() => action("again", () => fetch(`/api/call/again/${model.eventId}`, { method: "PUT" }), "再呼び出しを送信しました")}
                    >
                        🔁 再呼び出し
                    </button>
                </div>
                <p class="call-hint">
                    「次を呼ぶ」を押すと、呼び出し中で未チェックインのグループは割り込みプールへ退避します(§4.6)。
                </p>

                {message && <div class="call-message">{message}</div>}
                {error && <div class="call-message call-message-error">{error}</div>}

                <div class="call-panels">
                    <section class="call-panel">
                        <h2>現在の呼び出し中</h2>
                        {groupTable(queue?.callingGroup ?? [])}
                    </section>
                    <section class="call-panel">
                        <h2>割り込みプール(代表者チェックインで優先)</h2>
                        {groupTable(queue?.interruptedGroup ?? [])}
                    </section>
                    <section class="call-panel">
                        <h2>呼び出し待ち(正常キュー)</h2>
                        {groupTable(queue?.waitingGroup ?? [])}
                    </section>
                    <section class="call-panel">
                        <h2>マッチングプール人数</h2>
                        <div class="call-pool">{queue?.peoplePool ?? 0} 人</div>
                    </section>
                </div>
            </div>
        </Layout>
    );
}
