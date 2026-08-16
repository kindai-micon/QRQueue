import { useState, useEffect } from "preact/hooks";
import Layout from "@/Shared/Layout";

type LogEntry = {
    issuer: string;
    date: string;
    count: number;
    startNumber: number;
    endNumber: number;
};

type Model = {
    eventId: string;
};

// SvelteKit routes/event/[eventid]/publishing/+page.svelte から移行
export default function Publishing({ model }: { model: Model }) {
    const [eventName, setEventName] = useState("");
    const [issueCount, setIssueCount] = useState(10);
    const [totalIssued, setTotalIssued] = useState(0);
    const [isGenerating, setIsGenerating] = useState(false);
    const [logs, setLogs] = useState<LogEntry[]>([]);

    useEffect(() => {
        (async () => {
            const res = await fetch(`/api/event/Name?id=${model.eventId}`);
            setEventName(await res.text());

            await refreshLogs();
        })();
    }, [model.eventId]);

    async function loadLogs(): Promise<LogEntry[]> {
        const logRes = await fetch(`/api/pdf/logs?eventDisplayId=${model.eventId}`);
        if (!logRes.ok) {
            console.error("ログの取得に失敗しました");
            return [];
        }
        const data = await logRes.json();
        return data.map((entry: any) => ({
            issuer: entry.issuerName,
            date: new Date(entry.issuedAt).toLocaleString("ja-JP", { timeZone: "Asia/Tokyo" }),
            count: entry.count,
            startNumber: entry.startNumber,
            endNumber: entry.endNumber,
        }));
    }

    async function refreshLogs() {
        const loaded = await loadLogs();
        setLogs(loaded);
        setTotalIssued(loaded.reduce((sum, log) => sum + log.count, 0));
    }

    async function generateTickets(e: Event) {
        e.preventDefault();
        setIsGenerating(true);
        try {
            const response = await fetch("/api/pdf/generate", {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({
                    count: issueCount,
                    eventDisplayId: model.eventId,
                }),
            });

            if (!response.ok) {
                alert("PDFの生成に失敗しました");
                return;
            }

            const blob = await response.blob();
            const url = window.URL.createObjectURL(blob);
            const a = document.createElement("a");
            a.href = url;
            a.download = "チケット.pdf";
            document.body.appendChild(a);
            a.click();
            document.body.removeChild(a);

            await refreshLogs();
        } catch (err) {
            console.error("エラー:", err);
            alert("予期せぬエラーが発生しました");
        } finally {
            setIsGenerating(false);
        }
    }

    return (
        <Layout>
            <link rel="stylesheet" href="/css/event-publishing.css" />
            <div class="publishing-container">
                <div class="page-title">イベント: {eventName}</div>

                <form class="section issue-form" onSubmit={generateTickets}>
                    <div class="label">発行枚数を入力：</div>
                    <input
                        type="number"
                        min="1"
                        value={issueCount}
                        onInput={(e) => setIssueCount(Number(e.currentTarget.value))}
                    />
                    <button type="submit" class="btn-primary" disabled={isGenerating}>
                        {isGenerating ? "発行中..." : "チケットを発行"}
                    </button>
                </form>

                <div class="section">
                    <div class="label">合計発行枚数: {totalIssued}</div>
                    <div class="label">発行ログ:</div>
                    <table class="data-table">
                        <thead>
                            <tr>
                                <th>発行者</th>
                                <th>発行日時</th>
                                <th>枚数</th>
                                <th>番号範囲</th>
                            </tr>
                        </thead>
                        <tbody>
                            {logs.map((log, i) => (
                                <tr key={i}>
                                    <td>{log.issuer}</td>
                                    <td>{log.date}</td>
                                    <td>{log.count}</td>
                                    <td>No.{log.startNumber} ～ No.{log.endNumber}</td>
                                </tr>
                            ))}
                        </tbody>
                    </table>
                </div>
            </div>
        </Layout>
    );
}

