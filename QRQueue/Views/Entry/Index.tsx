import Layout from "@/Shared/Layout";
import { useState, useEffect } from "preact/hooks";
import { readErrorMessage, type ApiMessage, type EventInfoView, type JoinConflict, type JoinResult, type RestoreResult } from "@/Shared/api";

type Model = {
    eventId: string;
};

export default function Index({ model }: { model: Model }) {
const [eventInfo, setEventInfo] = useState<EventInfoView | null>(null);         //イベント情報を入れる場所.
const [showExistingMenu, setShowExistingMenu] = useState(false);                //3択の画面を表示するか.
const [existingTicketId, setExistingTicketId] = useState<string | null>(null);  //既存チケットのID.
const [selectedMode, setSelectedMode] = useState<string>("");                   //最初に押した参加方法（solo など）.
const [joinToken, setJoinToken] = useState<string | null>(null);                //グループ参加用の番号.
const [groupNumber, setGroupNumber] = useState<number | null>(null);            //作成されたグループ番号.
const [createdTicketId, setCreatedTicketId] = useState<string | null>(null);    //作成された自分のチケット番号.

useEffect(() => {
    async function loadEventInfo() {
        const response = await fetch(`/api/entry/${model.eventId}`);
        if (!response.ok) return;
        const data: EventInfoView = await response.json();

        setEventInfo(data);
    }

    loadEventInfo();
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
        const isJson = response.headers.get("content-type")?.includes("application/json");
        const data: JoinConflict | ApiMessage = isJson ? await response.json() : { message: await response.text() };
        if ("ticketDisplayId" in data && data.ticketDisplayId)
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
        alert(await readErrorMessage(response));
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
        alert(await readErrorMessage(response));
        return;
    }

    const data: RestoreResult = await response.json();

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
        alert(await readErrorMessage(response));
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

}

    return (
        <Layout chrome="header" title={eventInfo?.eventName ? `参加登録: ${eventInfo.eventName} | QRQueue` : "参加登録 | QRQueue"}>
        <div>
            
            <h1>イベント参加</h1>

            {eventInfo && (
                <h2>{eventInfo.eventName}</h2>
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