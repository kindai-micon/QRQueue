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
                    <a class="link-card" href={`/event/${model.eventId}/publishing`}>
                        チケットの発行
                        <div class="desc">チケットを発行できます</div>
                    </a>

                    <a class="link-card" href={`/event/${model.eventId}/tickets`}>
                        チケット一覧
                        <div class="desc">発行済みのチケットの一覧を確認できます</div>
                    </a>
                </div>
            </div>
        </Layout>
    );
}

