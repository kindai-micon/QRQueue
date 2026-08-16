import { useState, useEffect } from "preact/hooks";
import type { HubConnection } from "@microsoft/signalr";
import Layout from "@/Shared/Layout";

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
export default function Index({ model }: { model: Model }) {
    const [ticketData, setTicketData] = useState<TicketStatus | null>(null);
    const [loaded, setLoaded] = useState(false);
    const [notifications, setNotifications] = useState<string[]>([]);
    const [notification, setNotification] = useState(false);

    useEffect(() => {
        try {
            const stored = localStorage.getItem("notifications");
            const list: string[] = stored ? JSON.parse(stored) : [];
            setNotifications(list);
            setNotification(list.includes(model.ticketId));

            if ("serviceWorker" in navigator) {
                navigator.serviceWorker.register("/service-worker.js");
                navigator.serviceWorker.ready.then((reg) => reg.pushManager.getSubscription()).then((sub) => {
                    if (!sub && list.includes(model.ticketId)) {
                        const updated = list.filter((v) => v !== model.ticketId);
                        setNotifications(updated);
                        setNotification(false);
                        localStorage.setItem("notifications", JSON.stringify(updated));
                    }
                });
            }
        } catch (err) {
            console.error("通知設定の読み込みに失敗:", err);
        }
    }, [model.ticketId]);

    async function getVapidPublicKey(): Promise<string> {
        const res = await fetch("/api/push-subscription/vapid-public-key");
        if (!res.ok) {
            throw new Error("Failed to get VAPID key");
        }
        const data = await res.json();
        return data.publicKey;
    }

    function urlBase64ToUint8Array(base64String: string): Uint8Array<ArrayBuffer> {
        const padding = "=".repeat((4 - (base64String.length % 4)) % 4);
        const base64 = (base64String + padding).replace(/-/g, "+").replace(/_/g, "/");
        const rawData = atob(base64);
        const buffer = new ArrayBuffer(rawData.length);
        const view = new Uint8Array(buffer);
        [...rawData].forEach((c, i) => { view[i] = c.charCodeAt(0); });
        return view;
    }

    async function subscribeNotification() {
        const reg = await navigator.serviceWorker.ready;
        let sub = await reg.pushManager.getSubscription();

        if (!notification || !sub) {
            if ("serviceWorker" in navigator) {
                if (Notification.permission === "default") {
                    await Notification.requestPermission();
                }

                if (Notification.permission === "granted") {
                    const publicKey = await getVapidPublicKey();
                    if (!sub) {
                        sub = await reg.pushManager.subscribe({
                            userVisibleOnly: true,
                            applicationServerKey: urlBase64ToUint8Array(publicKey),
                        });
                    }

                    try {
                        await fetch(`/api/push-subscription/${model.ticketId}`, {
                            method: "POST",
                            headers: { "Content-Type": "application/json" },
                            body: JSON.stringify(sub),
                        });
                    } catch (error) {
                        console.error("Error loading data:", error);
                    }

                    setNotification(true);
                    const updated = [...notifications, model.ticketId];
                    setNotifications(updated);
                    localStorage.setItem("notifications", JSON.stringify(updated));
                }
            }
        }
    }

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
        <Layout chrome="header">
            <link rel="stylesheet" href="/css/ticket.css" />
            {loaded ? (
                ticketData ? (
                    <div class="container">
                        <button
                            class={`notification-btn ${notification ? "notification-registration" : "notification-no-registration"}`}
                            onClick={subscribeNotification}
                        >
                            呼び出し通知{notification ? "登録済み✔" : "登録"}
                        </button>
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
        </Layout>
    );
}

