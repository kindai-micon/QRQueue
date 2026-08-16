import { useState, useEffect } from "preact/hooks";
import Layout from "@/Shared/Layout";

type Ticket = {
    number: number;
    status: string;
    issuedAt: string;
    updatedAt: string;
    issuerName: string;
};

type Model = {
    eventId: string;
};

// SvelteKit routes/event/[eventid]/tickets/+page.svelte から移行
// ステータス集計は新 TicketStatus(Registered / Cancelled) に対応
export default function Tickets({ model }: { model: Model }) {
    const [tickets, setTickets] = useState<Ticket[] | null>(null);
    const [error, setError] = useState<string | null>(null);
    const [searchTerm, setSearchTerm] = useState("");

    useEffect(() => {
        (async () => {
            try {
                const res = await fetch(`/api/ticket/list?eventDisplayId=${model.eventId}`);
                if (!res.ok) throw new Error(`HTTP ${res.status}`);
                setTickets(await res.json());
                setError(null);
            } catch (e) {
                setError(`チケット一覧取得に失敗しました: ${(e as Error).message}`);
            }
        })();
    }, [model.eventId]);

    const filteredTickets = (tickets ?? []).filter(
        (t) =>
            searchTerm === "" ||
            t.number.toString().includes(searchTerm.toLowerCase()) ||
            t.status.toLowerCase().includes(searchTerm.toLowerCase()) ||
            t.issuerName.toLowerCase().includes(searchTerm.toLowerCase()),
    );

    const registeredCount = filteredTickets.filter((t) => t.status === "Registered").length;
    const cancelledCount = filteredTickets.filter((t) => t.status === "Cancelled").length;

    return (
        <Layout>
            <style>{`
                .tickets-container { padding: 2rem; max-width: 900px; margin: auto; font-family: sans-serif; }
                .search-box { margin-bottom: 1rem; }
                .search-box input {
                    width: 100%; padding: 0.5rem; font-size: 1rem;
                    border-radius: 4px; border: 1px solid #ccc; box-sizing: border-box;
                }
                .status-summary { display: flex; gap: 1rem; margin-bottom: 2rem; flex-wrap: wrap; }
                .card {
                    flex: 1 1 150px; background-color: #f9f9f9; padding: 1rem;
                    border-radius: 8px; border: 1px solid #ddd; text-align: center;
                    box-shadow: 2px 2px 6px rgba(0, 0, 0, 0.05);
                }
                .card h2 { margin: 0; font-size: 1.2rem; color: #333; }
                .card p { font-size: 1.5rem; font-weight: bold; color: #0077cc; margin: 0.5rem 0 0; }
                .tickets-table { width: 100%; border-collapse: collapse; margin-top: 1rem; }
                .tickets-table th, .tickets-table td { border: 1px solid #ccc; padding: 0.5rem; text-align: center; }
                .tickets-table th { background: #f0f0f0; }
                .error { color: red; margin-top: 1rem; }
                .loading { font-style: italic; }
            `}</style>
            <div class="tickets-container">
                <h1>発行済みチケット一覧</h1>

                {tickets === null && !error && <p class="loading">読み込み中...</p>}
                {error && <p class="error">{error}</p>}
                {tickets !== null && (
                    <>
                        <div class="search-box">
                            <input
                                type="text"
                                placeholder="番号、状態、発行者名で検索..."
                                value={searchTerm}
                                onInput={(e) => setSearchTerm(e.currentTarget.value)}
                            />
                        </div>

                        <div class="status-summary">
                            <div class="card">
                                <h2>登録済み (Registered)</h2>
                                <p>{registeredCount}</p>
                            </div>
                            <div class="card">
                                <h2>キャンセル (Cancelled)</h2>
                                <p>{cancelledCount}</p>
                            </div>
                        </div>

                        {filteredTickets.length === 0 ? (
                            <p>該当するチケットは見つかりませんでした。</p>
                        ) : (
                            <table class="tickets-table">
                                <thead>
                                    <tr>
                                        <th>番号</th>
                                        <th>状態</th>
                                        <th>発行日時</th>
                                        <th>更新日時</th>
                                        <th>発行者</th>
                                    </tr>
                                </thead>
                                <tbody>
                                    {filteredTickets.map((t) => (
                                        <tr key={t.number}>
                                            <td>No.{t.number}</td>
                                            <td>{t.status}</td>
                                            <td>{new Date(t.issuedAt).toLocaleString("ja-JP")}</td>
                                            <td>{new Date(t.updatedAt).toLocaleString("ja-JP")}</td>
                                            <td>{t.issuerName}</td>
                                        </tr>
                                    ))}
                                </tbody>
                            </table>
                        )}
                    </>
                )}
            </div>
        </Layout>
    );
}
