import { useState, useEffect } from "preact/hooks";
import type { HubConnection } from "@microsoft/signalr";
import Layout from "@/Shared/Layout";
import { groupStatusLabel, type GroupView, type QueueView } from "@/Shared/api";

type Model = {
    eventId: string; // eventDisplayId
};

// 管理用キュー一覧(設計§9.1 /event/[eventid]/queue、旧 tickets 画面の置換)。
// 番号・人数・待ち状況の読み取り専用。操作は呼び出しコンソールから。
export default function Queue({ model }: { model: Model }) {
    const [queue, setQueue] = useState<QueueView | null>(null);
    const [denied, setDenied] = useState(false);

    useEffect(() => {
        let connection: HubConnection | null = null;
        let disposed = false;
        let poll: number | undefined;

        async function load() {
            try {
                const res = await fetch(`/api/call/queue/${model.eventId}`);
                if (res.status === 401 || res.status === 403) {
                    setDenied(true);
                    return;
                }
                if (res.ok) {
                    setDenied(false);
                    setQueue(await res.json());
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
                const refresh = () => { load(); };
                connection.on("QueueChanged", refresh);
                connection.on("Called", refresh);
                connection.on("UpdateStatus", refresh);
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
                poll = window.setInterval(load, 10000);
            }
        })();

        return () => {
            disposed = true;
            if (poll) window.clearInterval(poll);
            connection?.stop().catch(() => { /* ignore */ });
        };
    }, [model.eventId]);

    const rows: (GroupView & { order: string })[] = [
        ...(queue?.callingGroup.map((g) => ({ ...g, order: "呼び出し中" })) ?? []),
        ...(queue?.interruptedGroup.map((g) => ({ ...g, order: "割り込み待ち" })) ?? []),
        ...(queue?.waitingGroup.map((g, i) => ({ ...g, order: `待ち ${i + 1}番目` })) ?? []),
    ];

    return (
        <Layout title="キュー一覧 | QRQueue">
            <link rel="stylesheet" href="/css/queue.css" />
            <div class="queue-container">
                <div class="page-title">キュー一覧</div>

                {denied && (
                    <div class="queue-denied">
                        この画面の表示には <code>CallView</code> 権限が必要です。
                    </div>
                )}

                <table class="data-table">
                    <thead>
                        <tr><th>グループ番号</th><th>人数</th><th>状態</th><th>待ち位置</th></tr>
                    </thead>
                    <tbody>
                        {rows.map((g, i) => (
                            <tr key={`${g.number}-${i}`} class={g.status === "Calling" ? "queue-row-calling" : ""}>
                                <td>{g.number}</td>
                                <td>{g.people}</td>
                                <td>{groupStatusLabel(g.status)}</td>
                                <td>{g.order}</td>
                            </tr>
                        ))}
                        {rows.length === 0 && <tr><td colSpan={4}>— 参加者はまだいません —</td></tr>}
                    </tbody>
                </table>

                <p class="queue-pool">マッチングプール(方式②で番号待ち): <strong>{queue?.peoplePool ?? 0}</strong> 人</p>
            </div>
        </Layout>
    );
}
