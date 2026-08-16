import { useState, useEffect } from "preact/hooks";
import Layout from "@/Shared/Layout";

type EventItem = {
    name: string;
    id: string;
};

// SvelteKit routes/event/+page.svelte から移行
export default function Index() {
    const [events, setEvents] = useState<EventItem[] | null>(null);
    const [error, setError] = useState<string | null>(null);
    const [newName, setNewName] = useState("");
    const [createError, setCreateError] = useState<string | null>(null);
    const [creating, setCreating] = useState(false);

    async function loadList() {
        try {
            const res = await fetch("/api/event/List");
            if (!res.ok) throw new Error(`HTTP ${res.status}`);
            setEvents(await res.json());
            setError(null);
        } catch (err) {
            setError(`イベント一覧の取得に失敗しました: ${(err as Error).message}`);
        }
    }

    async function createEvent(e: Event) {
        e.preventDefault();
        setCreateError(null);
        if (!newName.trim()) {
            setCreateError("名前を入力してください");
            return;
        }

        setCreating(true);
        try {
            const res = await fetch("/api/event/Create", {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify(newName.trim()),
            });
            if (!res.ok) throw new Error(`HTTP ${res.status}`);

            setNewName("");
            await loadList();
        } catch (err) {
            setCreateError(`作成に失敗しました: ${(err as Error).message}`);
        } finally {
            setCreating(false);
        }
    }

    useEffect(() => {
        loadList();
    }, []);

    return (
        <Layout>
            <style>{`
                .container { padding: 2rem; max-width: 800px; margin: 0 auto; }
                .new-form { display: flex; gap: 0.5rem; margin-bottom: 1.5rem; flex-wrap: wrap; }
                .new-form input {
                    flex: 1; padding: 0.5rem; border: 1px solid #ccc;
                    border-radius: 0.5rem; font-size: 1rem;
                }
                .new-form button {
                    padding: 0.5rem 1rem; border: none; background: #007acc; color: white;
                    border-radius: 0.5rem; cursor: pointer; transition: background 0.2s;
                }
                .new-form button:hover { background: #005fa3; }
                .event-list { display: grid; gap: 1rem; }
                .event-item {
                    padding: 1rem; border: 1px solid #ccc; border-radius: 0.75rem;
                    background: #f9f9f9; transition: background 0.2s, box-shadow 0.2s;
                    text-decoration: none; color: inherit; display: block;
                }
                .event-item:hover { background: #e9f5ff; box-shadow: 0 2px 8px rgba(0,0,0,0.1); }
                .status { margin-top: 1rem; color: gray; }
                .error { color: red; font-weight: bold; margin-top: 0.5rem; }
            `}</style>
            <div class="container">
                <h1>イベント一覧</h1>

                <form class="new-form" onSubmit={createEvent}>
                    <input
                        type="text"
                        placeholder="新しいイベント名を入力"
                        value={newName}
                        onInput={(e) => setNewName(e.currentTarget.value)}
                        disabled={creating}
                    />
                    <button type="submit" disabled={creating}>
                        {creating ? "作成中..." : "作成"}
                    </button>
                </form>

                {createError && <p class="error">{createError}</p>}

                {events === null && !error && <p class="status">読み込み中...</p>}
                {error && <p class="error">{error}</p>}
                {events !== null && events.length === 0 && <p class="status">イベントが登録されていません。</p>}
                {events !== null && events.length > 0 && (
                    <div class="event-list">
                        {events.map((item) => (
                            <a class="event-item" key={item.id} href={`/event/${encodeURIComponent(item.id)}`}>
                                {item.name}
                            </a>
                        ))}
                    </div>
                )}
            </div>
        </Layout>
    );
}
