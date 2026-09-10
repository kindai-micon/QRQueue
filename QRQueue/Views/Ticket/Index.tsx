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
    // notice の種別(成功=緑/失敗=赤)。既定は赤(従来のエラー表示と同じ)
    const [noticeKind, setNoticeKind] = useState<"error" | "success">("error");
    const [lineLinked, setLineLinked] = useState(false);
    // LINE通知のデバッグ用: サーバー側のLINE設定が有効か(未設定なら連携ボタンを出さず無効旨を表示)
    const [lineConfigured, setLineConfigured] = useState<boolean | null>(null);
    const [lineTestSending, setLineTestSending] = useState(false);
    // テスト通知の診断結果(画面に表示する。スマホは console が見れないため画面表示が基本)
    const [lineDebug, setLineDebug] = useState<{ label: string; value: string }[] | null>(null);
    const [homeHintHidden, setHomeHintHidden] = useState(true);
    const [transferCode, setTransferCode] = useState<string | null>(null);
    const [transferring, setTransferring] = useState(false);
    const [cancelling, setCancelling] = useState(false);

    // グループ全体の受付取消(issue #67)
    // 呼び出し前の代表者のみ実行でき、確認ダイアログで対象グループ全体が
    // 取り消されることを明示する。取り消したチケットは再利用できない。
    async function cancelGroup() {
        if (!ticketData) return;
        const confirmed = confirm(
            `本当にグループの受付を取り消しますか?\n\n` +
            `・グループのメンバー全員の受付が取り消されます\n` +
            `・現在のチケットは無効になり、再利用できません\n` +
            `・再参加する場合は、参加登録から新しいチケットを発行してください`
        );
        if (!confirmed || !ticketData.eventId) return;

        setCancelling(true);
        try {
            const res = await fetch("/api/entry/group/cancel", {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({ eventDisplayId: ticketData.eventId }),
            });
            if (res.ok) {
                window.location.href = `/entry/${ticketData.eventId}`;
                return;
            }
            const message = await readErrorMessage(res);
            alert(message || "取り消しに失敗しました");
        } catch (err) {
            console.error("受付取消に失敗:", err);
            alert("通信エラーが発生しました");
        } finally {
            setCancelling(false);
        }
    }

    // 別端末への引き継ぎ(issue #75):
    // 元端末で引き継ぎコードを発行し、新しい端末の /transfer で入力すると
    // チケットが新しい端末へ移る(元端末では以後使用できない)
    async function startTransfer() {
        if (!ticketData?.eventId) return;
        if (!confirm("別の端末へ引き継ぐためのコードを発行しますか?\n引き継ぎ後は、この端末ではこのチケットを使用できなくなります。")) return;
        setTransferring(true);
        try {
            const res = await fetch("/api/entry/transfer/start", {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({ eventDisplayId: ticketData.eventId }),
            });
            if (res.ok) {
                const data = await res.json();
                setTransferCode(data.code);
            } else {
            const message = await readErrorMessage(res);
                alert(message || "引き継ぎコードの発行に失敗しました");
            }
        } catch (err) {
            console.error("引き継ぎコードの発行に失敗:", err);
            alert("通信エラーが発生しました");
        } finally {
            setTransferring(false);
        }
    }

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
        // LINE連携のコールバックで戻ってきた場合の完了表示(URLからはパラメータを消しておく)
        if (new URLSearchParams(window.location.search).get("line") === "linked") {
            setNotice("✅ LINE連携が完了しました。順番が来るとLINEにも通知が届きます(連携先のトークに確認メッセージを送りました)");
            setNoticeKind("success");
            window.history.replaceState(null, "", window.location.pathname);
        }
        // LINE連携が失敗した場合もログイン画面などへは飛ばされず、この画面に戻される。
        // サーバー渡しの reason コードから原因を画面に表示する(スマホは console を見れないため)
        if (new URLSearchParams(window.location.search).get("line") === "error") {
            const reason = new URLSearchParams(window.location.search).get("reason") ?? "unknown";
            const reasonText: Record<string, string> = {
                cancelled: "LINE連携がキャンセルされました。もう一度「LINEで通知を受け取る」からお試しください",
                config: "サーバー側のLINE設定が未完了のため連携できませんでした。スタッフに「LINE設定が未完了」とお伝えください",
                state: "連携の状態が不正でした。もう一度お試しください(繰り返す場合はスタッフに「state形式不正」とお伝えください)",
                sign: "連携の署名検証に失敗しました。もう一度お試しください(繰り返す場合はスタッフに「署名不一致」とお伝えください)",
                expired: "連携の有効期限が切れました。電子券ページに戻ってからもう一度「LINEで通知を受け取る」を押してください",
                token: "LINEの認証に失敗しました。時間を置いてもう一度お試しください(繰り返す場合はスタッフに「トークン交換失敗」とお伝えください)",
                idtoken: "LINEからユーザー情報を取得できませんでした。もう一度お試しください",
                ticket: "チケットが見つかりませんでした。受付取消・引き継ぎされていないか確認してください",
                invalid_request: "連携のパラメータが不正でした。電子券ページからもう一度やり直してください",
                exception: "連携処理中にエラーが発生しました。もう一度お試しください",
            };
            setNotice(`❌ LINE連携に失敗しました: ${reasonText[reason] ?? reasonText.invalid_request} [原因コード: ${reason}]`);
            setNoticeKind("error");
            window.history.replaceState(null, "", window.location.pathname);
        }
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

    // ===== 受付確定フロー(issue #66) =====
    const isDraft = ticketData?.status === "Draft";
    const draftMemberCount = ticketData?.memberCount ?? 1;

    // 同時参加可否の変更(受付確定前のみ、3人では変更不可)
    async function setCoJoin(allow: boolean) {
        if (!ticketData?.eventId) return;
        try {
            const res = await fetch("/api/entry/group/cojoin", {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({ eventDisplayId: ticketData.eventId, allowCoJoin: allow }),
            });
            if (!res.ok) {
            const message = await readErrorMessage(res);
                alert(message || "変更できませんでした");
                window.location.reload();
            } else {
                window.location.reload();
            }
        } catch (err) {
            console.error("同時参加可否の変更に失敗:", err);
            alert("通信エラーが発生しました");
        }
    }

    // 受付確定: 代表者が「受付」を押すと呼び出し番号が採番され、待機キューに追加される
    async function confirmGroup() {
        if (!ticketData?.eventId) return;
        if (!confirm("受付を確定しますか?\n確定後は人数と同時参加可否を変更できません。")) return;
        try {
            const res = await fetch("/api/entry/group/confirm", {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({ eventDisplayId: ticketData.eventId }),
            });
            if (res.ok) {
                window.location.reload();
                return;
            }
            const message = await readErrorMessage(res);
            alert(message || "受付を確定できませんでした");
        } catch (err) {
            console.error("受付確定に失敗:", err);
            alert("通信エラーが発生しました");
        }
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

    // サーバー側のLINE設定とこのチケットの連携状態を取得する(デバッグ用)。
    // 設定未完了の環境では連携ボタンを無効化して利用者に分かるようにする
    async function loadLineStatus() {
        try {
            const res = await fetch(`/api/line/status/${model.ticketId}`);
            if (!res.ok) {
                console.error("LINE連携状態の取得に失敗:", res.status, await readErrorMessage(res));
                return;
            }
            const data: { configured: boolean; lineLinked: boolean } = await res.json();
            setLineConfigured(data.configured);
            if (data.lineLinked) {
                setLineLinked(true);
            }
        } catch (error) {
            console.error("LINE連携状態の取得に失敗:", error);
        }
    }

    // 実際にLINEへテスト通知を送って結果を診断する。
    // 結果は専用のデバッグボックスに画面表示する(スマホは console を見れないため)
    async function sendLineTestNotification() {
        setNotice(null);
        setLineDebug(null);
        setLineTestSending(true);
        try {
            const res = await fetch(`/api/line/test/${model.ticketId}`, { method: "POST" });
            const data = await res.json().catch(() => null);
            const debugRows = [
                { label: "日時", value: new Date().toLocaleString() },
                { label: "HTTP", value: String(res.status) },
                ...(data != null ? [
                    { label: "サーバー設定", value: data.configured ? "設定済み" : "未設定(管理者対応が必要)" },
                    { label: "LINE連携", value: data.lineLinked ? "連携済み" : "未連携" },
                    ...(data.pushStatus != null ? [{ label: "LINE API応答", value: `HTTP ${data.pushStatus}` }] : []),
                    ...(data.reason ? [{ label: "原因", value: String(data.reason) }] : []),
                    ...(data.error ? [{ label: "LINE APIエラー詳細", value: String(data.error) }] : []),
                ] : []),
            ];
            setLineDebug(debugRows);
            if (res.ok && data?.ok) {
                setNotice(`✅ ${data.message ?? "テスト通知を送信しました"}`);
                setNoticeKind("success");
            } else {
                const reason = data?.reason ?? (res.ok ? "送信に失敗しました" : `HTTP ${res.status}`);
                setNotice(`❌ LINEテスト通知に失敗しました。下の診断結果を確認してください(${reason})`);
                setNoticeKind("error");
            }
        } catch {
            setLineDebug([{ label: "日時", value: new Date().toLocaleString() }, { label: "結果", value: "サーバーと通信できませんでした(ネットワークエラー)" }]);
            setNotice("❌ LINEテスト通知の送信に失敗しました(通信エラー)");
        } finally {
            setLineTestSending(false);
        }
    }

    // LINE連携の解除
    async function unlinkLine() {
        try {
            const res = await fetch(`/api/line/unlink/${model.ticketId}`, { method: "POST" });
            if (!res.ok) {
                console.error("LINE連携の解除に失敗:", res.status, await readErrorMessage(res));
                return;
            }
            setLineLinked(false);
            setNotice("LINE連携を解除しました");
            setNoticeKind("success");
        } catch (error) {
            console.error("LINE連携の解除に失敗:", error);
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
                setLineLinked(data.lineLinked ?? false);
                await loadLineStatus();

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
                        {notice && (
                            <div class={`notification-notice ${noticeKind === "success" ? "notice-success" : ""}`}>{notice}</div>
                        )}

                        <div class="line-actions">
                            {lineLinked ? (
                                <>
                                    <div class="line-linked-label">LINE通知 連携済み</div>
                                    {/* テスト通知: 実際にLINEへ送り、失敗原因(友だち未追加/トークン無効等)を表示する */}
                                    <button
                                        class="line-test-btn"
                                        onClick={sendLineTestNotification}
                                        disabled={lineTestSending}
                                    >
                                        {lineTestSending ? "送信中..." : "テスト通知"}
                                    </button>
                                    <button class="line-unlink-btn" onClick={unlinkLine}>解除</button>
                                </>
                            ) : lineConfigured === false ? (
                                // サーバー側でLINE設定が未完了: 押しても動かないボタンより、無効である旨を明示する
                                <div class="line-disabled-label">
                                    ⚠️ LINE通知は現在サーバー側で無効です(管理者の設定待ち)。
                                    ブラウザの「呼び出し通知」はご利用いただけます
                                </div>
                            ) : (
                                <a class="line-btn" href={`/api/line/authorize/${model.ticketId}`}>
                                    LINEで通知を受け取る
                                </a>
                            )}
                        </div>
                        {/* テスト通知の診断結果(スマホは console を見れないため画面にそのまま出す) */}
                        {lineDebug && (
                            <div class="line-debug-box">
                                <div class="line-debug-title">LINE通知の診断結果</div>
                                {lineDebug.map((row) => (
                                    <div class="line-debug-row" key={row.label}>
                                        <span class="line-debug-label">{row.label}</span>
                                        <span class="line-debug-value">{row.value}</span>
                                    </div>
                                ))}
                                <div class="line-debug-note">
                                    スクリーンショットを撮ってスタッフにお見せください
                                </div>
                            </div>
                        )}
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
                            {isDraft && (
                                <div class="ticket-number-sub">「受付」を押すと番号が確定します</div>
                            )}
                        </div>

                        {/* 受付確定パネル(代表者・受付確定前のみ、issue #66) */}
                        {isDraft && ticketData.isRepresentative && (
                            <div class="draft-panel">
                                <div class="draft-members">
                                    現在の人数: <strong>{draftMemberCount}</strong> / 3 人
                                </div>
                                <label class="draft-cojoin">
                                    <input
                                        type="checkbox"
                                        checked={ticketData.allowCoJoin === true}
                                        disabled={draftMemberCount >= 3}
                                        onChange={(e) => setCoJoin(e.currentTarget.checked)}
                                    />
                                    他のグループと一緒に参加してもよい(3人未満の場合のみ)
                                </label>
                                {draftMemberCount >= 3 && (
                                    <div class="draft-note">3人に達したため、同時参加はオフで固定です。</div>
                                )}
                                <button class="confirm-btn" onClick={confirmGroup}>
                                    ✅ 受付を確定する
                                </button>
                                <div class="draft-note">
                                    受付を確定するまで呼び出されることはありません。
                                    確定後は人数・同時参加可否を変更できません。
                                </div>
                            </div>
                        )}

                        <div class="ticket-info">
                            <div class="heading">ステータス</div>
                            <div class="status-badge">{statusLabel}</div>
                        </div>

                        {/* 受付取消(呼び出し前の代表者のみ、issue #67) */}
                        {ticketData.isRepresentative && (isWaiting || ticketData.status === "Matching") && (
                            <div class="cancel-box">
                                <button
                                    class="cancel-group-btn"
                                    onClick={cancelGroup}
                                    disabled={cancelling}
                                >
                                    {cancelling ? "処理中..." : "グループの受付を取り消す"}
                                </button>
                                <div class="cancel-note">
                                    取り消した場合、メンバー全員の受付がキャンセルになり、
                                    現在のチケットは使えなくなります。再参加は新しいチケットで行います。
                                </div>
                            </div>
                        )}

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

                        {ticketData.status === "Completed" && !ticketData.used && (
                            <div class="alert-box alert-completed">
                                <div style={{ fontSize: "1.3rem", marginBottom: "0.5rem" }}>✓</div>
                                <div>受付完了</div>
                                <div class="alert-sub">受付が完了しました</div>
                            </div>
                        )}

                        {ticketData.used && (
                            <div class="alert-box alert-used">
                                <div style={{ fontSize: "1.3rem", marginBottom: "0.5rem" }}>🏁</div>
                                <div>使用済み</div>
                                <div class="alert-sub">
                                    このチケットのゲーム参加は終了しました。
                                    同じイベントに再度参加する場合は、参加登録から新しいチケットを発行してください。
                                </div>
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

                        {transferCode ? (
                            <div class="transfer-box">
                                <div class="heading">引き継ぎコード</div>
                                <div class="transfer-code">{transferCode}</div>
                                <p class="transfer-note">
                                    新しい端末で <strong>/transfer</strong> を開き、このコードを入力してください。
                                    <br />有効期限は10分・1回のみ使用できます。
                                    <br />引き継ぎ後、この端末ではチケットを表示できなくなります。
                                </p>
                            </div>
                        ) : (
                            <div class="transfer-box">
                                <button
                                    class="transfer-btn"
                                    onClick={startTransfer}
                                    disabled={transferring}
                                >
                                    {transferring ? "発行中..." : "📱 別の端末へ引き継ぐ"}
                                </button>
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
