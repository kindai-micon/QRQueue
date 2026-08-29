import { useState, useEffect } from "preact/hooks";
import Layout from "@/Shared/Layout";
import { readErrorMessage } from "@/Shared/queue";

type Model = {
    joinToken: string;
};

type GroupInfo = {
    groupNumber: number;
    memberCount: number;
    isFull: boolean;
    isJoinable: boolean;
};

// グループ参加確認画面(設計§9.1 /join/[token])。方式③の招待QRの飛び先。
export default function Join({ model }: { model: Model }) {
    const [info, setInfo] = useState<GroupInfo | null>(null);
    const [notFound, setNotFound] = useState(false);
    const [error, setError] = useState<string | null>(null);
    const [busy, setBusy] = useState(false);
    const [loaded, setLoaded] = useState(false);

    useEffect(() => {
        (async () => {
            try {
                const res = await fetch(`/api/entry/group/${encodeURIComponent(model.joinToken)}`);
                if (res.ok) {
                    setInfo(await res.json());
                } else {
                    setNotFound(true);
                    setError(await readErrorMessage(res));
                }
            } catch (err) {
                console.error("グループ情報の取得に失敗:", err);
                setNotFound(true);
            } finally {
                setLoaded(true);
            }
        })();
    }, [model.joinToken]);

    async function joinGroup() {
        setBusy(true);
        setError(null);
        try {
            const res = await fetch("/api/entry/group/join", {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({ joinToken: model.joinToken }),
            });
            if (res.ok) {
                const data = await res.json();
                window.location.href = `/ticket/${data.ticketDisplayId}`;
                return;
            }
            setError(await readErrorMessage(res));
        } catch (err) {
            console.error("グループ参加に失敗:", err);
            setError("通信エラーが発生しました");
        } finally {
            setBusy(false);
        }
    }

    return (
        <Layout chrome="header">
            <link rel="stylesheet" href="/css/join.css" />
            <div class="join-container">
                {!loaded && <p class="join-loading">グループ情報を読み込み中...</p>}

                {loaded && notFound && (
                    <div class="join-card join-card-error">
                        <h1>❌</h1>
                        <h2>この招待QRは無効です</h2>
                        <p>{error ?? "グループが見つかりません。代表者に新しいQRをもらってください。"}</p>
                    </div>
                )}

                {loaded && info && (
                    <div class="join-card">
                        <div class="join-kind">グループ参加</div>
                        <div class="join-number-label">グループ番号</div>
                        <div class="join-number">{info.groupNumber}</div>
                        <div class="join-members">
                            現在のメンバー <strong>{info.memberCount}</strong> / 3人
                        </div>

                        {error && <div class="join-error">{error}</div>}

                        {info.isJoinable ? (
                            <button
                                class="join-btn"
                                onClick={joinGroup}
                                disabled={busy}
                            >
                                {busy ? "参加中..." : "このグループに参加"}
                            </button>
                        ) : (
                            <div class="join-closed">
                                {info.isFull
                                    ? "このグループは満員です"
                                    : "このグループは呼び出し済みのため参加できません"}
                            </div>
                        )}

                        <p class="join-note">
                            すでに別のグループ・単独で参加中の場合、現在の参加を取り消して(this端末の電子券はそのまま)、
                            このグループに参加し直します。
                        </p>
                    </div>
                )}
            </div>
        </Layout>
    );
}
