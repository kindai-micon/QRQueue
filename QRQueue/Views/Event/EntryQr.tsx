import { useState, useEffect } from "preact/hooks";
import Layout from "@/Shared/Layout";
import { readErrorMessage } from "@/Shared/queue";
import "/css/checkin.css";

type Model = {
    eventId: string; // eventDisplayId
};

// 参加登録QRの掲示表示(自動更新)。
// チェックインQRと同じ仕様: 30秒で回転する到着確認コード付きQRを、10秒ごとに
// 再取得して表示し続ける。会場入口・受付のタブレット等での設置を想定。
// バックグラウンドタブではタイマーが長い間隔に抑制されるため、必ず前面表示のまま運用すること。
// 印刷物(固定QR)が必要な場合は管理画面から掲示PDFを発行する。
export default function EntryQr({ model }: { model: Model }) {
    const [src, setSrc] = useState<string>("");
    const [error, setError] = useState<string | null>(null);
    const [updatedAt, setUpdatedAt] = useState<Date | null>(null);

    useEffect(() => {
        let disposed = false;
        let timer: number | undefined;

        async function refresh() {
            try {
                const res = await fetch(`/api/call/entry-qrcode/${model.eventId}?t=${Date.now()}`);
                if (res.status === 401 || res.status === 403) {
                    if (!disposed) setError("参加登録QRを表示するには CallView 権限でログインしてください。");
                    return;
                }
                if (!res.ok) {
                    if (!disposed) setError(await readErrorMessage(res));
                    return;
                }
                const blob = await res.blob();
                const url = URL.createObjectURL(blob);
                if (!disposed) {
                    setSrc((prev) => {
                        if (prev) URL.revokeObjectURL(prev);
                        return url;
                    });
                    setUpdatedAt(new Date());
                    setError(null);
                } else {
                    URL.revokeObjectURL(url);
                }
            } catch (err) {
                console.error("参加登録QRの更新に失敗:", err);
            }
        }

        refresh();
        // 30秒ウィンドウに対し10秒ごとに更新(チェックインQRと同じ間隔)
        timer = window.setInterval(refresh, 10000);

        return () => {
            disposed = true;
            if (timer) window.clearInterval(timer);
        };
    }, [model.eventId]);

    return (
        <Layout chrome="header">
            <div class="checkin-container">
                <div class="checkin-card">
                    <div class="checkin-kind">参加登録QR</div>
                    <p class="checkin-desc">
                        このQRを読み取って<strong>参加登録ページ</strong>を開いてください。
                        すべての参加者はまずここから登録します。
                    </p>

                    {error && (
                        <div class="checkin-error">
                            <h2>表示できません</h2>
                            <p>{error}</p>
                        </div>
                    )}

                    {!error && src && (
                        <>
                            <img src={src} alt="参加登録QR" width={360} height={360} style={{ maxWidth: "100%" }} />
                            <p class="checkin-note">
                                このQRは<strong>30秒ごとに更新</strong>されます(最新: {updatedAt?.toLocaleTimeString("ja-JP")})。
                                スクリーンショットや撮影された古いQRは使用できません。
                                この画面を会場で表示し続ける場合は、<strong>タブを前面表示</strong>のままにしてください。
                                印刷物(固定QR)が必要な場合は管理画面から<strong>掲示PDFを発行</strong>してください。
                            </p>
                        </>
                    )}
                </div>
            </div>
        </Layout>
    );
}
