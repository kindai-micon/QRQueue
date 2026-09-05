import { useState, useEffect } from "preact/hooks";
import Layout from "@/Shared/Layout";
import type { EventInfoView } from "@/Shared/api";

type Model = {
    eventId: string; // eventDisplayId
};

// 掲示物発行画面(設計§9.1 /event/[eventid]/publishing 改造)。
// 旧: 紙券PDFのバルク発行 → 新: 参加登録QR / チェックインQR の A4 掲示用PDF発行(§8 / PR#9)。
export default function Publishing({ model }: { model: Model }) {
    const [ev, setEv] = useState<EventInfoView | null>(null);

    useEffect(() => {
        (async () => {
            try {
                const res = await fetch(`/api/entry/${model.eventId}`);
                if (res.ok) setEv(await res.json() as EventInfoView);
            } catch (err) {
                console.error("イベント情報の取得に失敗:", err);
            }
        })();
    }, [model.eventId]);

    async function download(path: string, filename: string) {
        try {
            const res = await fetch(path);
            if (!res.ok) {
                alert("PDFの発行に失敗しました(TicketPublish 権限が必要です)");
                return;
            }
            const blob = await res.blob();
            const url = window.URL.createObjectURL(blob);
            const a = document.createElement("a");
            a.href = url;
            a.download = filename;
            document.body.appendChild(a);
            a.click();
            document.body.removeChild(a);
            window.URL.revokeObjectURL(url);
        } catch (err) {
            console.error("PDF発行エラー:", err);
            alert("予期せぬエラーが発生しました");
        }
    }

    return (
        <Layout title={ev?.eventName ? `QR掲示PDF発行: ${ev.eventName} | QRQueue` : "QR掲示PDF発行 | QRQueue"}>
            <link rel="stylesheet" href="/css/event-publishing.css" />
            <div class="publishing-container">
                <div class="page-title">イベント: {ev?.eventName ?? "..."}</div>

                <p class="publishing-note">
                    印刷して会場に掲示してください。QRは<strong>掲示物であり参加証ではありません</strong>(§8)。
                </p>

                <div class="publishing-cards">
                    <section class="publishing-card">
                        <h2>参加登録QR</h2>
                        <p class="publishing-desc">
                            読み取ると参加登録ページ(<code>/entry/{model.eventId}</code>)へ。
                            すべての参加者はまずここから登録します。
                        </p>
                        <button
                            class="btn-primary"
                            onClick={() => download(`/api/pdf/entry/${model.eventId}`, "参加登録QR.pdf")}
                        >
                            A4掲示PDFを発行
                        </button>
                    </section>

                    <section class="publishing-card">
                        <h2>チェックインQR</h2>
                        <p class="publishing-desc">
                            受付に掲示。呼び出されたグループの<strong>代表者</strong>が読むと受付が確定し、
                            次の呼び出しが自動で進みます(§4.6)。
                        </p>
                        <button
                            class="btn-primary"
                            onClick={() => download(`/api/pdf/checkin/${model.eventId}`, "チェックインQR.pdf")}
                        >
                            A4掲示PDFを発行
                        </button>
                    </section>
                </div>
            </div>
        </Layout>
    );
}
