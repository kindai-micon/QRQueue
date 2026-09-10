import Layout from "@/Shared/Layout";
import { useState, useEffect } from "preact/hooks";
import type { HubConnection } from "@microsoft/signalr";
import { readErrorMessage, type ApiMessage, type EventInfoView, type JoinConflict, type JoinResult, type RestoreResult } from "@/Shared/api";

type Model = {
    eventId: string;
};

// 受付状態の表示ラベル(issue #65)
const STATUS_LABELS: Record<string, string> = {
    Preparing: "受付開始前",
    Open: "受付中",
    Closed: "受付終了",
};

export default function Index({ model }: { model: Model }) {
    const [eventInfo, setEventInfo] = useState<EventInfoView | null>(null);         //イベント情報を入れる場所.
    const [loadError, setLoadError] = useState<string | null>(null);                //イベント情報の取得失敗時に出す文言.
    const [showExistingMenu, setShowExistingMenu] = useState(false);                //3択の画面を表示するか.
    const [existingTicketId, setExistingTicketId] = useState<string | null>(null);  //既存チケットのID.
    const [selectedMode, setSelectedMode] = useState<string>("");                   //最初に押した参加方法（solo など）.
    const [joinToken, setJoinToken] = useState<string | null>(null);                //グループ参加用の番号.
    const [groupNumber, setGroupNumber] = useState<number | null>(null);            //作成されたグループ番号.
    const [createdTicketId, setCreatedTicketId] = useState<string | null>(null);    //作成された自分のチケット番号.
    const [error, setError] = useState<string | null>(null);                        //画面内に表示するエラー文言.
    const [busyMode, setBusyMode] = useState<string | null>(null);                  //処理中の参加方法（ボタンの二重押下防止）.

    useEffect(() => {
        let disposed = false;
        let connection: HubConnection | null = null;
        let poll: number | undefined;

        async function loadEventInfo() {
            try {
                const response = await fetch(`/api/entry/${model.eventId}`);
                if (!response.ok) {
                    setLoadError("イベント情報を取得できませんでした。QRが古い可能性があります。");
                    return;
                }
                const data: EventInfoView = await response.json();
                if (!disposed) {
                    setEventInfo(data);
                    setLoadError(null);
                }
            } catch (err) {
                console.error("イベント情報の取得に失敗:", err);
                if (!disposed) setLoadError("通信エラーが発生しました");
            }
        }

        loadEventInfo();

        (async () => {
            try {
                // 受付開始・終了を参加登録画面に自動反映する(issue #65)
                const { HubConnectionBuilder, HttpTransportType } = await import("@microsoft/signalr");
                connection = new HubConnectionBuilder()
                    .withUrl("/api/queueHub", { skipNegotiation: true, transport: HttpTransportType.WebSockets })
                    .withAutomaticReconnect()
                    .build();
                connection.on("UpdateStatus", loadEventInfo);
                connection.onreconnected(async () => {
                    await connection?.invoke("SetEvent", model.eventId);
                    await loadEventInfo();
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
            // 通知を取りこぼした場合のバックストップ(15秒ごとに再取得)
            if (!disposed) {
                poll = window.setInterval(loadEventInfo, 15000);
            }
        })();

        return () => {
            disposed = true;
            if (poll) window.clearInterval(poll);
            connection?.stop().catch(() => { /* ignore */ });
        };
    }, [model.eventId]);

    async function handleJoin(mode: string) {
        setError(null);
        setBusyMode(mode);
        try {
            const response = await fetch("/api/entry/join", {
                method: "POST",
                headers: {
                    "Content-Type": "application/json",
                },
                body: JSON.stringify({
                    eventDisplayId: model.eventId,
                    mode: mode,
                    overwrite: false,
                }),
            });

            if (response.status === 409) {
                const isJson = response.headers.get("content-type")?.includes("application/json");
                const data: JoinConflict | ApiMessage = isJson ? await response.json() : { message: await response.text() };
                if ("ticketDisplayId" in data && data.ticketDisplayId) {
                    setExistingTicketId(data.ticketDisplayId);
                    setSelectedMode(mode);
                    setShowExistingMenu(true);
                } else {
                    setError(data.message ?? "参加登録できませんでした");
                }

                return;
            }

            if (!response.ok) {
                setError(await readErrorMessage(response));
                return;
            }

            const data: JoinResult = await response.json();

            if (mode === "group-create") {
                setJoinToken(data.joinToken ?? null);
                setGroupNumber(data.groupNumber);
                setCreatedTicketId(data.ticketDisplayId);
                return;
            }

            window.location.href = `/ticket/${data.ticketDisplayId}`;
        } catch (err) {
            console.error("参加登録に失敗:", err);
            setError("通信エラーが発生しました");
        } finally {
            setBusyMode(null);
        }
    }

    async function handleJoinOverwrite(mode: string) {
        setError(null);
        setBusyMode(mode);
        try {
            const response = await fetch("/api/entry/join", {
                method: "POST",
                headers: {
                    "Content-Type": "application/json",
                },
                body: JSON.stringify({
                    eventDisplayId: model.eventId,
                    mode: mode,
                    overwrite: true,
                }),
            });

            if (!response.ok) {
                setError(await readErrorMessage(response));
                return;
            }

            const data: JoinResult = await response.json();

            // 受付確定(メンバー追加・人数・同時参加可否の変更)は電子券画面で行う(issue #66)。
            // group-create の場合も代表者はまず電子券画面へ遷移する。
            window.location.href = `/ticket/${data.ticketDisplayId}`;
        } catch (err) {
            console.error("参加登録(上書き)に失敗:", err);
            setError("通信エラーが発生しました");
        } finally {
            setBusyMode(null);
        }
    }

    const busy = busyMode !== null;
    const closed = !!eventInfo && !eventInfo.isOpen;

    return (
        <Layout chrome="header" title={eventInfo?.eventName ? `参加登録: ${eventInfo.eventName} | QRQueue` : "参加登録 | QRQueue"}>
            <link rel="stylesheet" href="/css/entry.css" />
            <div class="entry-container">
                <div class="entry-card">
                    <div class="entry-kind">イベント参加</div>

                    <h1 class="entry-event-name">
                        {eventInfo?.eventName ?? (loadError ? "イベント" : "読み込み中...")}
                    </h1>
                    <p class="entry-event-id">イベントID: {model.eventId}</p>

                    {eventInfo && (
                        <p class="entry-status">
                            受付状態:{" "}
                            <strong style={{ color: eventInfo.isOpen ? "green" : "#c62828" }}>
                                {STATUS_LABELS[eventInfo.status] ?? eventInfo.status}
                            </strong>
                        </p>
                    )}

                    {loadError && (
                        <div class="entry-error">{loadError}</div>
                    )}

                    {eventInfo && !eventInfo.isOpen && (
                        <div class="entry-closed">
                            現在、受付を行っていません。
                        </div>
                    )}

                    {joinToken ? (
                        <div class="entry-group-created">
                            <h2>グループを作成しました</h2>
                            <div class="entry-group-label">グループ番号</div>
                            <div class="entry-group-number">{groupNumber}</div>
                            <p class="entry-group-desc">
                                一緒に参加する人は、以下のQRコードを読み取って参加してください。
                            </p>
                            <div class="entry-qr">
                                <img
                                    src={`/api/entry/group/${joinToken}/qrcode`}
                                    alt="グループ参加用QRコード"
                                />
                            </div>
                            <button
                                class="entry-btn entry-btn-primary"
                                onClick={() => {
                                    window.location.href = `/ticket/${createdTicketId}`;
                                }}
                            >
                                チケットを見る
                            </button>
                        </div>
                    ) : (
                        <>
                            <h2 class="entry-mode-title">参加方法を選択してください</h2>

                            {error && <div class="entry-error">{error}</div>}

                            {showExistingMenu && (
                                <div class="entry-existing">
                                    <h3>既に参加登録されています</h3>
                                    <p class="entry-existing-desc">
                                        この端末にはすでに参加登録があります。現在のチケットを見るか、参加を取り消して選び直せます。
                                    </p>
                                    <button
                                        class="entry-btn entry-btn-primary"
                                        onClick={() => {
                                            window.location.href = `/ticket/${existingTicketId}`;
                                        }}
                                    >
                                        既存のチケットを見る
                                    </button>
                                    <button
                                        class="entry-btn entry-btn-secondary"
                                        disabled={busy}
                                        onClick={() => {
                                            setShowExistingMenu(false);
                                            handleJoinOverwrite(selectedMode);
                                        }}
                                    >
                                        取り消して参加し直す
                                    </button>
                                </div>
                            )}

                            <div class="entry-modes">
                                <button
                                    class="entry-mode"
                                    disabled={!eventInfo?.isOpen || busy}
                                    onClick={() => handleJoin("solo")}
                                >
                                    <span class="entry-mode-name">{busyMode === "solo" ? "登録中..." : "1人で参加"}</span>
                                    <span class="entry-mode-desc">すぐに呼び出し番号が発行されます</span>
                                </button>

                                <button
                                    class="entry-mode"
                                    disabled={!eventInfo?.isOpen || busy}
                                    onClick={() => handleJoin("pool")}
                                >
                                    <span class="entry-mode-name">{busyMode === "pool" ? "登録中..." : "おまかせグループ"}</span>
                                    <span class="entry-mode-desc">仲間と自動でグループになり、成立次第番号が確定します</span>
                                </button>

                                <button
                                    class="entry-mode"
                                    disabled={!eventInfo?.isOpen || busy}
                                    onClick={() => handleJoin("group-create")}
                                >
                                    <span class="entry-mode-name">{busyMode === "group-create" ? "登録中..." : "グループを作成"}</span>
                                    <span class="entry-mode-desc">代表者が作成し、QRでメンバーを招待します(2〜3人)</span>
                                </button>
                            </div>

                            {closed && (
                                <p class="entry-note">受付開始までお待ちください。</p>
                            )}
                        </>
                    )}
                </div>
            </div>

            {eventInfo && !eventInfo.isOpen && (
                <p>現在、受付を行っていません。</p>
            )}

            <p style={{ fontSize: "0.85rem" }}>
                別の端末から引き継ぐ(引き継ぎコードをお持ちの方は)
                <a href="/transfer">こちら</a>
            </p>

        </Layout>
    );
}
