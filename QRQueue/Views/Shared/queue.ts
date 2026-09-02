// 呼び出しキュー関連の共通型・helper(設計§9.1 の各画面から参照)

export type GroupView = {
    number: number;
    people: number;
    status: number; // GroupStatus enum(数値で返る)
};

// GET /api/call/queue/{eventDisplayId} のレスポンス(QueueView)
export type QueueView = {
    waitingGroup: GroupView[];
    callingGroup: GroupView[];
    interruptedGroup: GroupView[];
    peoplePool: number;
};

// QRQueue.Models.GroupStatus の順序(Matching=0 … Cancelled=5)
export const GROUP_STATUS_LABEL: Record<number, string> = {
    0: "マッチング中",
    1: "呼び出し待ち",
    2: "呼び出し中",
    3: "割り込み待ち",
    4: "受付済み",
    5: "無効",
};

export const groupStatusLabel = (status: number): string =>
    GROUP_STATUS_LABEL[status] ?? String(status);

// 409/404 などのサーバーメッセージ(Conflict("..."))を読み取る
export async function readErrorMessage(res: Response): Promise<string> {
    try {
        const text = await res.text();
        if (!text) return res.statusText;
        try {
            const parsed = JSON.parse(text);
            if (typeof parsed === "string") return parsed;
            if (parsed?.message) return String(parsed.message);
            if (parsed?.title) return String(parsed.title);
            return text;
        } catch {
            return text;
        }
    } catch {
        return "エラーが発生しました";
    }
}
