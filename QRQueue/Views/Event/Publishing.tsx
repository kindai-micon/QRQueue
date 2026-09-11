import { useState, useEffect } from "preact/hooks";
import Layout from "@/Shared/Layout";
import MessageModal from "@/Shared/Modal";
import type { EventInfoView } from "@/Shared/api";

type Model = {
    eventId: string; // eventDisplayId
};

// 掲示物発行画面(設計書 /event/[eventid]/publishing 改造)。
// 旧: 紙券PDFのバルク発行 → 新: 参加登録QR / チェックインQR の A4 掲示用PDF発行(/ PR#9)。
export default function Publishing({ model }: { model: Model }) {
    const [ev, setEv] = useState<EventInfoView | null>(null);
    const [notice, setNotice] = useState<string | null>(null);

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
                setNotice("PDFの発行に失敗しました(TicketPublish 権限が必要です)");
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
            setNotice("予期せぬエラーが発生しました");
        }
    }

    return (
        <Layout title={ev?.eventName ? `QR掲示PDF発行: ${ev.eventName} | QRQueue` : "QR掲示PDF発行 | QRQueue"}>
            <link rel="stylesheet" href="/css/event-publishing.css" />
            <MessageModal message={notice} onClose={() => setNotice(null)} />
            <div class="publishing-container">
                <div class="page-title">イベント: {ev?.eventName ?? "..."}</div>

                <p class="publishing-note">
                    印刷して会場に掲示してください。QRは<strong>掲示物であり参加証ではありません</strong>。
                </p>

                <div class="publishing-cards">
                    <section class="publishing-card">
                        <h2>参加登録QR</h2>
                        <p class="publishing-desc">
                            読み取ると参加登録ページ(<code>/entry/{model.eventId}</code>)へ。
                            すべての参加者はまずここから登録します。
                            PDFのQRは<strong>固定コード</strong>付き(失効なし)。Web掲示画面のQRは
                            <strong>30秒で回転</strong>し、撮影・共有された古いQRからは登録できません(チェックインQRと同じ仕様)。
                        </p>
                        <button
                            class="btn-primary"
                            onClick={() => download(`/api/pdf/entry/${model.eventId}`, "参加登録QR.pdf")}
                        >
                            A4掲示PDFを発行
                        </button>
                        <a
                            class="btn-primary"
                            style={{ textDecoration: "none", display: "inline-block", padding: "0.6rem 1.2rem", marginTop: "0.5rem" }}
                            href={`/entry-qr/${model.eventId}`}
                            target="_blank"
                            rel="noopener"
                        >
                            参加登録QR画面を開く
                        </a>
                    </section>

                    <section class="publishing-card">
                        <h2>チェックインQR(自動更新表示)</h2>
                        <p class="publishing-desc">
                            受付に設置したタブレット等で表示します。呼び出されたグループの<strong>代表者</strong>が
                            読み取ると受付が確定します。
                            QRには<strong>30秒で回転する到着確認コード</strong>が含まれており、受付掲示QRからのみ
                            チェックインが完了します。撮影・共有された古いQRは使用できません。
                            チェックインしても次のグループは自動で呼び出されません。次の呼び出しは
                            呼び出しコンソールの「次を呼ぶ」から行ってください。
                        </p>
                        <button
                            class="btn-primary"
                            onClick={() => download(`/api/pdf/checkin/${model.eventId}`, "チェックインQR.pdf")}
                        >
                            固定QRのA4掲示PDFを発行
                        </button>
                        <p class="publishing-desc" style={{ marginTop: "0.5rem", fontSize: "0.85rem" }}>
                            ⚠ 印刷した固定QRは失効しないため、撮影・共有されたURLからでもチェックインが可能になります。
                            求められる場合は自動更新画面の運用をご検討ください。
                        </p>
                        <a
                            class="btn-primary"
                            style={{ textDecoration: "none", display: "inline-block", padding: "0.6rem 1.2rem", marginTop: "0.5rem" }}
                            href={`/checkin-qr/${model.eventId}`}
                            target="_blank"
                            rel="noopener"
                        >
                            受付確認QR画面を開く(自動更新)
                        </a>
                    </section>
                </div>
            </div>
        </Layout>
    );
}
