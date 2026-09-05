import { useState, useEffect } from "preact/hooks";
import Layout from "@/Shared/Layout";

type Model = {
    eventId: string;
};

// SvelteKit routes/event/[eventid]/+page.svelte から移行
export default function Detail({ model }: { model: Model }) {
    const [eventName, setEventName] = useState("");

    useEffect(() => {
        (async () => {
            const res = await fetch(`/api/event/Name?id=${model.eventId}`);
            setEventName(await res.text());
        })();
    }, [model.eventId]);

    return (
        <Layout>
            <link rel="stylesheet" href="/css/event-detail.css" />
            <div class="detail-container">
                <div class="page-title">イベント: {eventName}</div>

                <div class="nav">
                    <a class="link-card" href={`/event/${model.eventId}/call`}>
                        呼び出しコンソール
                        <div class="desc">受付開閉・次を呼ぶ・再呼び出し(§4.6)</div>
                    </a>

                    <a class="link-card" href={`/event/${model.eventId}/queue`}>
                        キュー一覧
                        <div class="desc">番号・人数・待ち状況の確認(旧チケット一覧の置換)</div>
                    </a>

                    <a class="link-card" href={`/event/${model.eventId}/publishing`}>
                        QR掲示PDFの発行
                        <div class="desc">参加登録QR・チェックインQRのA4掲示物</div>
                    </a>

                    <a class="link-card" href={`/display/${model.eventId}`} target="_blank" rel="noreferrer">
                        投影画面
                        <div class="desc">現在呼び出し中を大きく表示(新しいタブ)</div>
                    </a>
                </div>
            </div>
        </Layout>
    );
}

