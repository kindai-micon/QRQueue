import { useState, useEffect } from "preact/hooks";
import type { HubConnection } from "@microsoft/signalr";
import Layout from "@/Shared/Layout";

type TicketStatus = {
    number: number;
    status: string;
    eventId: string | null;
    // 設計§6.1 拡張項目(PR #7)
    eventName?: string | null;
    groupNumber?: number | null;
    currentCallingNumber?: number | null;
    aheadCount?: number | null;
    // 電子券画面用 接着項目(§9.1)
    joinToken?: string | null;
    isRepresentative?: boolean;
};

type Model = {
    ticketId: string;
};

const STATUS_LABELS: Record<string, string> = {
    Waiting: "呼び出し待ち",
    Calling: "呼び出し中",
    Interrupted: "割り込み待ち",
    Completed: "受付完了 ✓",
    Matching: "グループ編成中",
    Cancelled: "無効",
    Registered: "参加登録済み",
};

// 電子券画面(参加証そのもの、設計§9.1 /ticket/[ticketid] 改造)
export default function Index({ model }: { model: Model }) {
    const [ticketData, setTicketData] = useState<TicketStatus | null>(null);
    const [loaded, setLoaded] = useState(false);
    const [notifications, setNotifications] = useState<string[]>([]);
    const [notification, setNotification] = useState(false);
    const [homeHintHidden, setHomeHintHidden] = useState(true);

    // 「ホーム画面に追加」導線(§9.1):  standalone で開いていないときだけ案内
    useEffect(() => {
        const standalone =
            (navigator as any).standalone === true ||
            window.matchMedia?.("(display-mode: standalone)").matches;
        if (!standalone && !localStorage.getItem("hide-home-hint")) {
            setHomeHintHidden(false);
        }
    }, []);

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
        let poll: number | undefined;

        async function load() {
            try {
                const res = await fetch(`/api/ticket/${model.ticketId}`);
                if (!res.ok) {
                    if (res.status === 404 && !disposed) {
                        setTicketData(null);
                    }
                    return;
                }
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

                // 新イベント名(設計§7): UpdateStatus(参加変動) / QueueChanged(キュー変動) / Called(呼び出し)
                connection.on("UpdateStatus", load);
                connection.on("QueueChanged", load);
                connection.on("Called", load);
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
            } catch (err) {
                console.error("SignalR connection setup error:", err);
            }
            await load();
            if (!disposed) {
                setLoaded(true);
                // 通知を取りこぼした場合のバックストップ(15秒)
                poll = window.setInterval(load, 15000);
            }
        })();

        return () => {
            disposed = true;
            if (poll) window.clearInterval(poll);
            connection?.stop().catch((err) => console.error("Error stopping SignalR connection:", err));
        };
    }, [model.ticketId]);

    const statusLabel = ticketData ? STATUS_LABELS[ticketData.status] ?? ticketData.status : null;
    const displayNumber = ticketData ? (ticketData.groupNumber ?? ticketData.number) : null;
    const isCalling = ticketData?.status === "Calling";
    const isInterrupted = ticketData?.status === "Interrupted";
    const isWaiting = ticketData?.status === "Waiting";

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
                            <h1>{ticketData.eventName ?? "電子券"}</h1>
                            <p>あなたの参加証(この画面が唯一の参加証です)</p>
                        </div>

                        <div class="ticket-number-box">
                            <div class="ticket-number-label">呼び出し番号</div>
                            <div class="ticket-number">{displayNumber}</div>
                            {ticketData.status === "Matching" && (
                                <div class="ticket-number-sub">グループが揃い次第、番号が確定します</div>
                            )}
                        </div>

                        <div class="ticket-info">
                            <div class="heading">ステータス</div>
                            <div class="status-badge">{statusLabel}</div>
                        </div>

                        {isWaiting && (
                            <div class="queue-info">
                                {ticketData.currentCallingNumber != null && (
                                    <div class="queue-row">
                                        <span class="queue-label">いま呼び出し中</span>
                                        <span class="queue-value">{ticketData.currentCallingNumber} 番</span>
                                    </div>
                                )}
                                {ticketData.aheadCount != null && (
                                    <div class="queue-row">
                                        <span class="queue-label">あなたの前を待っている組</span>
                                        <span class="queue-value">{ticketData.aheadCount} 組</span>
                                    </div>
                                )}
                            </div>
                        )}

                        {isCalling && ticketData.isRepresentative && (
                            <div class="alert-box alert-calling">
                                <div style={{ fontSize: "1.3rem", marginBottom: "0.5rem" }}>🔔</div>
                                <div>呼び出されました！</div>
                                <div class="alert-sub">
                                    メンバーがそろうと受付のチェックインQRを読み取ってください。
                                </div>
                                {ticketData.eventId && (
                                    <a class="alert-link" href={`/checkin/${ticketData.eventId}`}>
                                        チェックイン画面へ(受付掲示QRの代わりにここからでも可)
                                    </a>
                                )}
                            </div>
                        )}
                        {isCalling && !ticketData.isRepresentative && (
                            <div class="alert-box alert-calling">
                                <div>呼び出されました！</div>
                                <div class="alert-sub">
                                    受付では<strong>代表者</strong>がチェックインQRを読み取ります。代表者と一緒に向かってください。
                                </div>
                            </div>
                        )}

                        {isInterrupted && (
                            <div class="alert-box alert-interrupted">
                                <div>割り込み待ち(退避)中です</div>
                                <div class="alert-sub">
                                    メンバーがそろったら代表者が受付のチェックインQRを読み取ると、
                                    次の呼び出しに<strong>割り込んで</strong>優先的に処理されます。
                                </div>
                                {ticketData.eventId && ticketData.isRepresentative && (
                                    <a class="alert-link" href={`/checkin/${ticketData.eventId}`}>
                                        チェックイン画面へ
                                    </a>
                                )}
                            </div>
                        )}

                        {ticketData.status === "Completed" && (
                            <div class="alert-box alert-completed">
                                <div style={{ fontSize: "1.3rem", marginBottom: "0.5rem" }}>✓</div>
                                <div>受付完了</div>
                                <div class="alert-sub">受付が完了しました</div>
                            </div>
                        )}

                        {ticketData.joinToken && (
                            <div class="group-qr">
                                <div class="heading">グループ参加QR</div>
                                <p class="group-qr-desc">
                                    メンバーはこのQRを読み取ると <strong>{displayNumber}</strong> 番のグループに参加できます。
                                </p>
                                <img
                                    src={`/api/entry/group/${ticketData.joinToken}/qrcode`}
                                    alt="グループ参加QR"
                                    width={260}
                                    height={260}
                                />
                            </div>
                        )}

                        {!homeHintHidden && (
                            <div class="home-hint">
                                <div>
                                    <strong>📱 後で見るには</strong>
                                    ：ブラウザメニューの「ホーム画面に追加」でこの電子券を再訪できます
                                    (URLを紛失しても、この端末なら自動で復元されます)。
                                </div>
                                <button
                                    class="home-hint-close"
                                    onClick={() => {
                                        localStorage.setItem("hide-home-hint", "1");
                                        setHomeHintHidden(true);
                                    }}
                                >
                                    ✕ 閉じる
                                </button>
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
