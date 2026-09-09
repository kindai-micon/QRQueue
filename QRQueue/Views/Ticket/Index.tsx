import { useState, useEffect } from "preact/hooks";
import type { HubConnection } from "@microsoft/signalr";
import Layout from "@/Shared/Layout";
import { readErrorMessage, TICKET_STATUS_LABEL, type TicketView, type VapidPublicKeyView } from "@/Shared/api";

type Model = {
    ticketId: string;
};

// 電子券画面(参加証そのもの、設計書 /ticket/[ticketid] 改造)
export default function Index({ model }: { model: Model }) {
    const [ticketData, setTicketData] = useState<TicketView | null>(null);
    const [loaded, setLoaded] = useState(false);
    const [notifications, setNotifications] = useState<string[]>([]);
    const [notification, setNotification] = useState(false);
    const [notice, setNotice] = useState<string | null>(null);
    const [homeHintHidden, setHomeHintHidden] = useState(true);

    // 「ホーム画面に追加」導線:  standalone で開いていないときだけ案内
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
                // 「登録」済みの端末は表示のたびにサーバーへ再同期する。
                // 過去の不具合で購読が保存されていない端末を、ボタンの再操作なしに自己修復する
                navigator.serviceWorker.ready
                    .then(() => syncNotification(true))
                    .catch((err) => console.error("通知設定の同期に失敗:", err));
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
        const data: VapidPublicKeyView = await res.json();
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

    function arrayBufferToBase64(buffer: ArrayBuffer): string {
        const bytes = new Uint8Array(buffer);
        let binary = "";
        bytes.forEach((b) => { binary += String.fromCharCode(b); });
        return btoa(binary);
    }

    // 購読を作成/修復してサーバーへ upsert する。
    // auto = true はページ表示時の自己修復で、許可を求めず「登録」済みの端末だけ対象にする
    async function syncNotification(auto: boolean) {
        if (!("serviceWorker" in navigator)) {
            if (!auto) {
                setNotice("お使いのブラウザは通知に対応していません(iOS Safariはホーム画面に追加すると使えます)");
            }
            return;
        }
        if (auto) {
            // state はマウント直後でまだ古いので localStorage を直接見る
            const stored: string[] = JSON.parse(localStorage.getItem("notifications") ?? "[]");
            if (!stored.includes(model.ticketId)) {
                return; // 「登録」したことのない端末で勝手に購読しない
            }
        }
        const reg = await navigator.serviceWorker.ready;
        if (!reg.pushManager) {
            if (!auto) {
                setNotice("お使いのブラウザは通知に対応していません(iOS Safariはホーム画面に追加すると使えます)");
            }
            return;
        }
        let sub = await reg.pushManager.getSubscription();

        if (Notification.permission === "default") {
            if (auto) {
                return; // 許可を求めるのはボタン操作のときだけ
            }
            await Notification.requestPermission();
        }
        if (Notification.permission !== "granted") {
            if (!auto) {
                setNotice("通知が許可されていません。ブラウザの設定でこのサイトの通知を許可してください");
            }
            return;
        }

        const publicKey = await getVapidPublicKey();

        // サーバーの鍵と購読時の鍵が違う(サーバー側で鍵が再生成された等)場合、
        // その購読では送信が必ず失敗するため作り直す
        const subKey = sub?.options?.applicationServerKey;
        if (sub && subKey && arrayBufferToBase64(subKey as ArrayBuffer) !== publicKey) {
            await sub.unsubscribe();
            sub = null;
        }

        if (!sub) {
            sub = await reg.pushManager.subscribe({
                userVisibleOnly: true,
                applicationServerKey: urlBase64ToUint8Array(publicKey),
            });
        }

        // PushSubscription を直接 stringify すると endpoint しか送られないため、
        // 鍵(p256dh / auth)を明示的に取り出してサーバーと同じ形式で送る
        const p256dh = sub.getKey("p256dh");
        const auth = sub.getKey("auth");
        if (!p256dh || !auth) {
            console.error("購読の鍵が取得できませんでした");
            if (!auto) {
                setNotice("通知の登録に失敗しました(購読の鍵が取得できませんでした)");
            }
            return;
        }

        try {
            const res = await fetch(`/api/push-subscription/${model.ticketId}`, {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({
                    endpoint: sub.endpoint,
                    keys: {
                        p256dh: arrayBufferToBase64(p256dh),
                        auth: arrayBufferToBase64(auth),
                    },
                }),
            });
            if (!res.ok) {
                console.error("通知の登録に失敗:", res.status, await readErrorMessage(res));
                if (!auto) {
                    setNotice(`通知の登録に失敗しました(${res.status})`);
                }
                return;
            }
        } catch (error) {
            console.error("通知の登録に失敗:", error);
            if (!auto) {
                setNotice("通知の登録に失敗しました(通信エラー)");
            }
            return;
        }

        if (!auto) {
            setNotice(null);
        }
        setNotification(true);
        const updated = [...notifications.filter((v) => v !== model.ticketId), model.ticketId];
        setNotifications(updated);
        localStorage.setItem("notifications", JSON.stringify(updated));
    }

    // サーバーから実際にプッシュを送らせて届くかを確かめる。
    // 届かない場合、登録は成功しているのでサーバー側(送信処理)の問題と切り分けられる
    async function sendTestNotification() {
        setNotice(null);
        try {
            const res = await fetch(`/api/push-subscription/${model.ticketId}/test`, { method: "POST" });
            const data = await res.json().catch(() => null);
            setNotice(data?.message ?? (res.ok ? "テスト通知を送信しました" : `テスト通知の送信に失敗しました(${res.status})`));
            if (!res.ok) {
                console.error("テスト通知の送信に失敗:", res.status, data);
            }
        } catch (error) {
            console.error("テスト通知の送信に失敗:", error);
            setNotice("テスト通知の送信に失敗しました(通信エラー)");
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
                const data: TicketView = await res.json();
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

                // 新イベント名(設計書): UpdateStatus(参加変動) / QueueChanged(キュー変動) / Called(呼び出し)
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

    const statusLabel = ticketData ? TICKET_STATUS_LABEL[ticketData.status] ?? ticketData.status : null;
    const displayNumber = ticketData ? (ticketData.groupNumber ?? ticketData.number) : null;
    const isCalling = ticketData?.status === "Calling";
    const isInterrupted = ticketData?.status === "Interrupted";
    const isWaiting = ticketData?.status === "Waiting";

    return (
        <Layout chrome="header" title={ticketData?.eventName ? `${ticketData.eventName} 電子券 | QRQueue` : "電子券 | QRQueue"}>
            <link rel="stylesheet" href="/css/ticket.css" />
            {loaded ? (
                ticketData ? (
                    <div class="container">
                        <div class="notification-actions">
                            <button
                                class={`notification-btn ${notification ? "notification-registration" : "notification-no-registration"}`}
                                onClick={() => syncNotification(false)}
                            >
                                呼び出し通知{notification ? "登録済み✔" : "登録"}
                            </button>
                            <button class="notification-test-btn" onClick={sendTestNotification}>
                                テスト通知
                            </button>
                        </div>
                        {notice && <div class="notification-notice">{notice}</div>}
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
                                    受付に掲示された<strong>チェックインQR</strong>を読み取って、
                                    到着を確認してください(issue #68: 電子券画面からは直接チェックインできません)。
                                </div>
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
