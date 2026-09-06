import { useState, useEffect } from "preact/hooks";
import Layout from "@/Shared/Layout";
import { readErrorMessage, type EventListItem } from "@/Shared/api";

// SvelteKit routes/event/+page.svelte から移行
export default function Index() {
    const [events, setEvents] = useState<EventListItem[] | null>(null);
    const [error, setError] = useState<string | null>(null);
    const [newName, setNewName] = useState("");
    const [createError, setCreateError] = useState<string | null>(null);
    const [creating, setCreating] = useState(false);
    const [targetToDelete, setTargetToDelete] = useState<EventListItem | null>(null);
    const [deleteError, setDeleteError] = useState<string | null>(null);
    const [deleting, setDeleting] = useState(false);

    async function loadList() {
        try {
            const res = await fetch("/api/event/List");
            if (!res.ok) throw new Error(`HTTP ${res.status}`);
            setEvents(await res.json() as EventListItem[]);
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

    function confirmDelete(item: EventListItem) {
        setTargetToDelete(item);
        setDeleteError(null);
    }

    function closeDeleteModal() {
        setTargetToDelete(null);
        setDeleteError(null);
    }

    // イベント削除。グループ・チケット等の関連データはサーバー側でカスケード削除される
    async function executeDelete() {
        if (!targetToDelete) return;
        setDeleting(true);
        try {
            const res = await fetch("/api/event/Delete", {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify(targetToDelete.name),
            });
            if (res.ok) {
                setEvents((prev) => prev?.filter((e) => e.id !== targetToDelete.id) ?? null);
                closeDeleteModal();
            } else {
                // 統一エラー形式 ApiMessage
                setDeleteError(await readErrorMessage(res));
            }
        } catch (err) {
            console.log(err);
            setDeleteError("通信エラーが発生しました。");
        } finally {
            setDeleting(false);
        }
    }

    useEffect(() => {
        loadList();
    }, []);

    return (
        <Layout title="イベント管理 | QRQueue">
            <link rel="stylesheet" href="/css/event.css" />
            {targetToDelete && (
                <div class="modal-overlay">
                    <div class="modal">
                        <h3>イベント削除の確認</h3>
                        <p>「{targetToDelete.name}」を削除してもよろしいですか？<br />
                        グループ・チケットなどの関連データもすべて削除され、元に戻せません。</p>
                        {deleteError && <p class="modal-error">{deleteError}</p>}
                        <div class="modal-buttons">
                            <button class="btn-danger" onClick={executeDelete} disabled={deleting}>
                                {deleting ? "削除中..." : "削除する"}
                            </button>
                            <button class="btn-secondary btn-sm" onClick={closeDeleteModal} disabled={deleting}>キャンセル</button>
                        </div>
                    </div>
                </div>
            )}
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
                    <button type="submit" class="btn-primary" disabled={creating}>
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
                            <div class="event-row" key={item.id}>
                                <a class="event-item" href={`/event/${encodeURIComponent(item.id)}`}>
                                    {item.name}
                                </a>
                                <button class="btn-danger btn-sm" onClick={() => confirmDelete(item)}>削除</button>
                            </div>
                        ))}
                    </div>
                )}
            </div>
        </Layout>
    );
}

