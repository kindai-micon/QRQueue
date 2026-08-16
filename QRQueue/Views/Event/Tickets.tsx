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
            <link rel="stylesheet" href="/css/event-tickets.css" />
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
                            <table class="data-table">
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

