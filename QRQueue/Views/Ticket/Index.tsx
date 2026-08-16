import { useState, useEffect } from "preact/hooks";
import type { HubConnection } from "@microsoft/signalr";

type TicketStatus = {
    number: number;
    status: string;
    eventId: string | null;
};

type Model = {
    ticketId: string;
};

const STATUS_LABELS: Record<string, string> = {
    Waiting: "呼び出し待ち",
    Calling: "呼び出し中",
    Interrupted: "割り込み待ち",
    Completed: "受付済み ✓",
    Matching: "マッチング中",
    Cancelled: "無効",
};

// SvelteKit routes/ticket/[ticketid]/+page.svelte から移行
// (プッシュ通知登録は未移行)
export default function Index({ model }: { model: Model }) {
    const [ticketData, setTicketData] = useState<TicketStatus | null>(null);
    const [loaded, setLoaded] = useState(false);

    useEffect(() => {
        let connection: HubConnection | null = null;
        let disposed = false;
        let joinedEventId: string | null = null;

        async function load() {
            try {
                const res = await fetch(`/api/ticket/${model.ticketId}`);
                const data: TicketStatus = await res.json();
                if (disposed) return;
                setTicketData(data);

                // チケットのイベントが確定したら SignalR グループへ参加
                if (data.eventId && data.eventId !== joinedEventId && connection?.state === "Connected") {
                    if (joinedEventId) {
                        await connection.invoke("RemoveEvent", joinedEventId);
                    }
                    joinedEventId = data.eventId;
                    await connection.invoke("SetEvent", joinedEventId);
                }
            } catch (error) {
                console.error("Error loading ticket data:", error);
            }
        }

        (async () => {
            try {
                // SignalR クライアントはブラウザ専用のため effect 内で動的 import
                const { HubConnectionBuilder, HttpTransportType } = await import("@microsoft/signalr");
                connection = new HubConnectionBuilder()
                    .withUrl("/api/queueHub", { skipNegotiation: true, transport: HttpTransportType.WebSockets })
                    .withAutomaticReconnect()
                    .build();

                connection.on("UpdateStatus", load);
                connection.on("SetTarget", load);
                connection.on("SubmitLottery", load);
                connection.on("ViewStop", load);
                connection.on("ExchangeStop", load);
                connection.onreconnected(async () => {
                    if (joinedEventId) {
                        await connection?.invoke("SetEvent", joinedEventId);
                    }
                    await load();
                });

                await connection.start();
                if (disposed) {
                    await connection.stop();
                    return;
                }
                await load();
            } catch (err) {
                console.error("SignalR connection setup error:", err);
                await load();
            } finally {
                if (!disposed) {
                    setLoaded(true);
                }
            }
        })();

        return () => {
            disposed = true;
            connection?.stop().catch((err) => console.error("Error stopping SignalR connection:", err));
        };
    }, [model.ticketId]);

    const statusLabel = ticketData ? STATUS_LABELS[ticketData.status] ?? ticketData.status : null;

    return (
        <div>
            <style>{`
                body { margin: 0; padding: 0; background-color: #f5f5f5; }
                .container {
                    display: flex; flex-direction: column; align-items: center;
                    padding: 1.5rem 1rem; min-height: 100vh;
                    background: linear-gradient(135deg, #f5f5f5 0%, #efefef 100%);
                }
                .header { text-align: center; margin-bottom: 2rem; width: 100%; }
                .header h1 { margin: 0 0 0.5rem; font-size: 1.8rem; color: #333; }
                .header p { margin: 0; font-size: 0.9rem; color: #666; }
                .ticket-number-box {
                    background: linear-gradient(135deg, #3a7d7c 0%, #2d6360 100%);
                    color: white; padding: 2rem; border-radius: 12px; margin-bottom: 1rem;
                    text-align: center; box-shadow: 0 4px 12px rgba(58, 125, 124, 0.3);
                }
                .ticket-number-label { font-size: 0.85rem; opacity: 0.9; margin-bottom: 0.5rem; font-weight: 500; }
                .ticket-number {
                    font-size: 2.5rem; font-weight: bold; letter-spacing: 2px;
                    font-family: 'Courier New', monospace;
                }
                .ticket-info {
                    background-color: #ffffff; padding: 1.5rem; border-radius: 12px;
                    margin-bottom: 1rem; box-shadow: 0 2px 8px rgba(0, 0, 0, 0.08);
                    width: 100%; max-width: 450px; box-sizing: border-box;
                }
                .heading {
                    font-weight: 600; font-size: 0.95rem; margin-bottom: 0.8rem;
                    text-transform: uppercase; letter-spacing: 0.5px; color: #666;
                }
                .status-badge {
                    display: inline-block; padding: 0.6rem 1.2rem; border-radius: 20px;
                    font-weight: 600; font-size: 1rem; margin-top: 0.5rem;
                    background-color: #e8f5e9; color: #2e7d32;
                }
                .alert-box {
                    margin-top: 1.5rem; padding: 1.5rem; border-radius: 12px; text-align: center;
                    width: 100%; max-width: 450px; box-sizing: border-box; font-weight: 600;
                }
                .alert-calling { background-color: #fff3e0; border: 2px solid #ff9800; color: #e65100; }
                .alert-completed { background-color: #e8f5e9; border: 2px solid #4caf50; color: #2e7d32; }
                .alert-sub { font-size: 0.9rem; margin-top: 0.5rem; font-weight: 400; }
                .loading { text-align: center; padding: 3rem 1rem; font-size: 1.1rem; color: #666; }
            `}</style>
            {loaded ? (
                ticketData ? (
                    <div class="container">
                        <div class="header">
                            <h1>チケット確認</h1>
                            <p>QRコード読み込み完了</p>
                        </div>

                        <div class="ticket-number-box">
                            <div class="ticket-number-label">呼び出し番号</div>
                            <div class="ticket-number">{ticketData.number}</div>
                        </div>

                        <div class="ticket-info">
                            <div class="heading">ステータス</div>
                            <div class="status-badge">{statusLabel}</div>
                        </div>

                        {ticketData.status === "Calling" && (
                            <div class="alert-box alert-calling">
                                <div style={{ fontSize: "1.3rem", marginBottom: "0.5rem" }}>🔔</div>
                                <div>呼び出されました！</div>
                                <div class="alert-sub">代表者の方は受付のチェックインQRを読み取ってください</div>
                            </div>
                        )}

                        {ticketData.status === "Completed" && (
                            <div class="alert-box alert-completed">
                                <div style={{ fontSize: "1.3rem", marginBottom: "0.5rem" }}>✓</div>
                                <div>受付完了</div>
                                <div class="alert-sub">受付が完了しました</div>
                            </div>
                        )}
                    </div>
                ) : (
                    <div class="container">
                        <div class="loading">
                            <div style={{ fontSize: "1.5rem", marginBottom: "1rem" }}>❌</div>
                            <p>チケット情報が見つかりません</p>
                            <p style={{ fontSize: "0.9rem", marginTop: "1rem", color: "#999" }}>
                                QRコードをもう一度読み込んでください
                            </p>
                        </div>
                    </div>
                )
            ) : (
                <div class="container">
                    <div class="loading">
                        <p>チケット情報を読み込み中...</p>
                    </div>
                </div>
            )}
        </div>
    );
}
