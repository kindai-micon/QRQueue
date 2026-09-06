import { useState, useEffect } from "preact/hooks";
import Layout from "@/Shared/Layout";
import { readErrorMessage } from "@/Shared/api";

type Model = {
    eventId: string;
};

// SvelteKit routes/event/[eventid]/+page.svelte から移行
export default function Detail({ model }: { model: Model }) {
    const [eventName, setEventName] = useState("");
    const [showDeleteModal, setShowDeleteModal] = useState(false);
    const [deleteError, setDeleteError] = useState<string | null>(null);
    const [deleting, setDeleting] = useState(false);

    useEffect(() => {
        (async () => {
            const res = await fetch(`/api/event/Name?id=${model.eventId}`);
            setEventName(await res.text());
        })();
    }, [model.eventId]);

    // イベント削除。グループ・チケット等の関連データはサーバー側でカスケード削除される
    async function executeDelete() {
        setDeleting(true);
        try {
            const res = await fetch("/api/event/Delete", {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify(eventName),
            });
            if (res.ok) {
                window.location.href = "/event";
                return;
            }
            // 統一エラー形式 ApiMessage
            setDeleteError(await readErrorMessage(res));
            setShowDeleteModal(false);
        } catch (err) {
            console.log(err);
            setDeleteError("通信エラーが発生しました。");
            setShowDeleteModal(false);
        } finally {
            setDeleting(false);
        }
    }

    return (
        <Layout title={eventName ? `イベント: ${eventName} | QRQueue` : "イベント | QRQueue"}>
            <link rel="stylesheet" href="/css/event-detail.css" />
            {showDeleteModal && (
                <div class="modal-overlay">
                    <div class="modal">
                        <h3>イベント削除の確認</h3>
                        <p>「{eventName}」を削除してもよろしいですか？<br />
                        グループ・チケットなどの関連データもすべて削除され、元に戻せません。</p>
                        {deleteError && <p class="modal-error">{deleteError}</p>}
                        <div class="modal-buttons">
                            <button class="btn-danger" onClick={executeDelete} disabled={deleting}>
                                {deleting ? "削除中..." : "削除する"}
                            </button>
                            <button class="btn-secondary btn-sm" onClick={() => { setShowDeleteModal(false); setDeleteError(null); }} disabled={deleting}>キャンセル</button>
                        </div>
                    </div>
                </div>
            )}
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

                <div class="danger-zone">
                    <div class="danger-title">危険な操作</div>
                    <p class="danger-desc">イベントを削除すると、グループ・チケットなどの関連データもすべて削除され、元に戻せません。</p>
                    <button class="btn-danger" onClick={() => { setShowDeleteModal(true); setDeleteError(null); }}>
                        イベントを削除
                    </button>
                </div>
            </div>
        </Layout>
    );
}

