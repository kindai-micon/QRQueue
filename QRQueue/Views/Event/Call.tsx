import { useState, useEffect } from "preact/hooks";
import type { HubConnection } from "@microsoft/signalr";
import Layout from "@/Shared/Layout";
import { groupStatusLabel, readErrorMessage, type EventInfoView, type GroupView, type QueueView } from "@/Shared/api";

type Model = {
    eventId: string; // eventDisplayId
};

// 呼び出しコンソール(設計書 /event/[eventid]/call、旧 execute 置換)。
// 操作は「受付開閉」「次を呼ぶ」「再呼び出し」のみ。完了は参加者のチェックインで確定するため
// 完了ボタンは置かない。
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

    // スタッフによるグループ単位の操作(優先待機移動・棄権・直接呼び出し)(issue #73)。
    // 誤操作防止のため、実行前に確認ダイアログを表示する。
    async function staffGroupAction(g: GroupView, op: "interrupt" | "forfeit" | "call") {
        if (!g.displayId) return;
        const label = `${g.number}番(メンバー ${g.people} 人)`;
        const confirmText = op === "interrupt"
            ? `${label} を優先待機へ移動しますか?`
            : op === "call"
                ? `${label} を今すぐ呼び出しますか?\n優先待機のグループを直接呼び出します(通知も送られます)。`
                : `${label} を棄権扱いにして、チケットを無効化しますか?\nこの操作は取り消せません。`;
        if (!confirm(confirmText)) return;

        const okMessage = op === "interrupt"
            ? "優先待機へ移動しました"
            : op === "call"
                ? `${g.number}番を呼び出しました(通知を送りました)`
                : "棄権処理を行いました(チケットを無効化しました)";
        action(op, () => fetch(`/api/call/group/${g.displayId}/${op}`, { method: "PUT" }), okMessage);
    }

    const groupTable = (groups: GroupView[], callButton?: (g: GroupView) => void) => (
        <table class="data-table">
            <thead>
                <tr><th>番号</th><th>人数</th><th>状態</th>{callButton && <th>操作</th>}</tr>
            </thead>
            <tbody>
                {groups.map((g, i) => (
                    <tr key={`${g.number}-${i}`}>
                        <td>{g.number}</td>
                        <td>{g.people}</td>
                        <td>{groupStatusLabel(g.status)}</td>
                        {callButton && (
                            <td>
                                <button
                                    class="btn-secondary btn-sm"
                                    disabled={busy}
                                    onClick={() => callButton(g)}
                                >
                                    📣 呼び出す
                                </button>
                            </td>
                        )}
                    </tr>
                ))}
                {groups.length === 0 && <tr><td colSpan={callButton ? 4 : 3}>—</td></tr>}
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
                    「次を呼ぶ」を押すと、呼び出し中で未チェックインのグループは割り込みプールへ退避します。
                    チェックインしても次のグループは自動で呼び出されないため、前のグループのゲーム終了時に
                    「次を呼ぶ」で呼び出してください。
                    チェックインした時点でチケットは自動的に使用済みになります。
                </p>

                {message && <div class="call-message">{message}</div>}
                {error && <div class="call-message call-message-error">{error}</div>}

                <div class="call-panels">
                    <section class="call-panel">
                        <h2>ゲーム参加枠の到着状況</h2>
                        {(queue?.slots?.length ?? 0) === 0 ? (
                            <p class="call-pool" style={{ fontSize: "0.9rem", color: "#888" }}>処理中の枠はありません</p>
                        ) : (
                            queue!.slots!.map((slot) => (
                                <div class="slot-block" key={slot.slotId}>
                                    <div class="slot-title">
                                        {slot.allArrived ? "✅ 全グループ到着済み" : "⏳ 到着確認中"}
                                        <span class="slot-time">
                                            {slot.calledAt ? `(${new Date(slot.calledAt).toLocaleTimeString("ja-JP")} 呼び出し)` : ""}
                                        </span>
                                    </div>
                                    <table class="data-table">
                                        <thead>
                                            <tr><th>グループ番号</th><th>人数</th><th>到着状態</th><th>操作</th></tr>
                                        </thead>
                                        <tbody>
                                            {slot.groups.map((g, i) => (
                                                <tr key={`${slot.slotId}-${g.number}-${i}`}>
                                                    <td>{g.number}</td>
                                                    <td>{g.people}</td>
                                                    <td>{groupStatusLabel(g.status)}</td>
                                                    <td>
                                                        {(g.status === 2 || g.status === 4) && (
                                                            <button
                                                                class="btn-secondary btn-sm"
                                                                disabled={busy}
                                                                onClick={() => staffGroupAction(g, "interrupt")}
                                                            >
                                                                優先待機へ
                                                            </button>
                                                        )}
                                                        {g.status !== 5 && (
                                                            <button
                                                                class="btn-danger btn-sm"
                                                                disabled={busy}
                                                                onClick={() => staffGroupAction(g, "forfeit")}
                                                            >
                                                                棄権
                                                            </button>
                                                        )}
                                                    </td>
                                                </tr>
                                            ))}
                                        </tbody>
                                    </table>
                                </div>
                            ))
                        )}
                        <p class="call-hint">
                            未到着グループが一定時間(既定5分)を超えると優先待機へ移動します(QueueCall:SlotTimeoutMinutes で変更可)。
                        </p>
                    </section>
                    <section class="call-panel">
                        <h2>現在の呼び出し中</h2>
                        {groupTable(queue?.callingGroup ?? [])}
                    </section>
                    <section class="call-panel">
                        <h2>割り込みプール(代表者チェックインで優先)</h2>
                        {/* 優先プールのグループはスタッフが任意のタイミングで直接呼び出せる */}
                        {groupTable(queue?.interruptedGroup ?? [], (g) => staffGroupAction(g, "call"))}
                        <p class="call-hint">
                            「📣 呼び出す」で代表者のチェックインを待たずに直接呼び出せます(Web Push・LINE・電子券画面に通知が届きます)。
                        </p>
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
