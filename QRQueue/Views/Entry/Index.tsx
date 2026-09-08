import Layout from "@/Shared/Layout";
import { useState, useEffect } from "preact/hooks";
import type { HubConnection } from "@microsoft/signalr";

type Model = {
    eventId: string;
};

type EventInfo = {
    eventName: string;
    status: string;
    isOpen: boolean;
    maxGroupSize: number;
};

// 受付状態の表示ラベル(issue #65)
const STATUS_LABELS: Record<string, string> = {
    Preparing: "受付開始前",
    Open: "受付中",
    Closed: "受付終了",
};

export default function Index({ model }: { model: Model }) {
const [eventInfo, setEventInfo] = useState<EventInfo | null>(null);             //イベント情報を入れる場所.
const [showExistingMenu, setShowExistingMenu] = useState(false);                //3択の画面を表示するか.
const [existingTicketId, setExistingTicketId] = useState<string | null>(null);  //既存チケットのID.
const [selectedMode, setSelectedMode] = useState<string>("");                   //最初に押した参加方法（solo など）.
const [joinToken, setJoinToken] = useState<string | null>(null);                //グループ参加用の番号.
const [groupNumber, setGroupNumber] = useState<number | null>(null);            //作成されたグループ番号.
const [createdTicketId, setCreatedTicketId] = useState<string | null>(null);    //作成された自分のチケット番号.

useEffect(() => {
    let disposed = false;
    let connection: HubConnection | null = null;
    let poll: number | undefined;

    async function loadEventInfo() {
        try {
            const response = await fetch(`/api/entry/${model.eventId}`);
            if (!response.ok) return;
            const data = await response.json();
            if (!disposed) setEventInfo(data);
        } catch (err) {
            console.error("イベント情報の取得に失敗:", err);
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
            connection.on("QueueChanged", loadEventInfo);
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
        const isJson = response.headers.get("context-type")?.includes("application/json");
        const data = isJson ? await response.json() : { message: await response.text()} ;
        if ( data.ticketDisplayId ) 
        {
            setExistingTicketId(data.ticketDisplayId);
            setSelectedMode(mode);
            setShowExistingMenu(true);
        } else {
            alert(data.message ??  "参加登録できませんでした" );
        }

        return;
    }
    

    if (!response.ok) {
        const errorMessage = await response.text();
        alert(errorMessage);
        return;
    }

    const data = await response.json();

    if (mode === "group-create") {
        setJoinToken(data.joinToken);
        setGroupNumber(data.groupNumber);
        setCreatedTicketId(data.ticketDisplayId);
        return;
    }

    window.location.href = `/ticket/${data.ticketDisplayId}`;

}

async function handleRestore() {
    const response = await fetch("/api/entry/restore", {
        method: "POST",
        headers: {
            "Content-Type": "application/json",
        },
        body: JSON.stringify({
            eventDisplayId: model.eventId,
        }),
    });

    if (!response.ok) {
        const errorMessage = await response.text();
        alert(errorMessage);
        return;
    }

    const data = await response.json();

    window.location.href = `/ticket/${data.ticketDisplayId}`;
}

async function handleJoinOverwrite(mode: string) {
    
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
        const errorMessage = await response.text();
        alert(errorMessage);
        return;
    }
    
    const data = await response.json();

    if (mode === "group-create") {
        setJoinToken(data.joinToken);
        setGroupNumber(data.groupNumber);
        setCreatedTicketId(data.ticketDisplayId);
        return;
    }

    window.location.href = `/ticket/${data.ticketDisplayId}`;

}

    return (
        <Layout chrome="header">
        <div>
            
            <h1>イベント参加</h1>

            {eventInfo && (
                <>
                    <h2>{eventInfo.eventName}</h2>
                    <p>
                        受付状態:{" "}
                        <strong style={{ color: eventInfo.isOpen ? "green" : "#c62828" }}>
                            {STATUS_LABELS[eventInfo.status] ?? eventInfo.status}
                        </strong>
                    </p>
                </>
            )}

            <p>イベントID：{model.eventId}</p>

            <h2>参加方法を選択してください</h2>

            {showExistingMenu && (
                <div>
                    <h3>既に参加登録されています</h3>

                        <button
                            onClick={() => {
                            window.location.href = `/ticket/${existingTicketId}`;
                        }}
                        >
                            既存のチケットを見る
                        </button>
                </div>
           )}

           {joinToken && (
                <div>
                    <h2>グループを作成しました</h2>

                    <p>グループ番号：{groupNumber}</p>

                    <p>
                        一緒に参加する人に、以下のQRコードを読み取ってもらってください。
                    </p>

                    <img
                        src={`/api/entry/group/${joinToken}/qrcode`}
                        alt="グループ参加用QRコード"
                    />

                    <br />

                    <button
                        onClick={() => {
                            window.location.href = `/ticket/${createdTicketId}`;
                        }}
                    >
                        チケットを見る
                    </button>
                </div>
            )}


            {eventInfo && !eventInfo.isOpen && (
                <p>現在、受付を行っていません。</p>
            )}

            <button
                disabled={!eventInfo?.isOpen}
                onClick={() => handleJoin("solo")}
            >
                1人で参加
            </button>

            <button
                disabled={!eventInfo?.isOpen}
                onClick={() => handleJoin("pool")}
            >
                おまかせグループ
            </button>

            <button
                disabled={!eventInfo?.isOpen}
                onClick={() => handleJoin("group-create")}
            >
                グループを作成
            </button>

        </div>
        </Layout>
    ); 
}