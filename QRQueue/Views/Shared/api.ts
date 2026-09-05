// QRQueue API の共通型・helper。
// Models/API/ 配下の C# DTO(レコード・クラス)と 1:1 で対応する手書きミラー。
// enum はサーバー側で JsonStringEnumConverter により文字列化されるため union 型で受ける。

// === enum(QRQueue.Models の列挙型に対応) ===
export type GroupStatus = "Matching" | "Waiting" | "Calling" | "Interrupted" | "Completed" | "Cancelled";
export type TicketStatus = "Registered" | "Cancelled";
export type EventStatus = "Preparing" | "Open" | "Closed";

// === 呼び出しキュー(Models/API/QueueView.cs, ParticipationGroupView.cs) ===
export type GroupView = {
    number: number;
    people: number;
    status: GroupStatus;
};

// GET /api/call/queue/{eventDisplayId} のレスポンス(QueueView)
export type QueueView = {
    waitingGroup: GroupView[];
    callingGroup: GroupView[];
    interruptedGroup: GroupView[];
    peoplePool: number;
};

export const GROUP_STATUS_LABEL: Record<GroupStatus, string> = {
    Matching: "マッチング中",
    Waiting: "呼び出し待ち",
    Calling: "呼び出し中",
    Interrupted: "割り込み待ち",
    Completed: "受付済み",
    Cancelled: "無効",
};

export const groupStatusLabel = (status: GroupStatus): string =>
    GROUP_STATUS_LABEL[status] ?? String(status);

// === 参加登録(Models/API/EntryViews.cs) ===
// GET /api/entry/{eventDisplayId} のレスポンス(EventInfoView)
export type EventInfoView = {
    eventName: string;
    status: EventStatus;
    isOpen: boolean;
    maxGroupSize: number;
};

// POST /api/entry/join と /api/entry/group/join のレスポンス(JoinResult)
export type JoinResult = {
    ticketDisplayId: string;
    groupNumber: number | null;
    joinToken?: string | null; // 方式③グループ作成時のみ
};

// POST /api/entry/join の 409(JoinConflict)。復元用の電子券IDを同梱
export type JoinConflict = {
    message: string;
    ticketDisplayId: string;
};

// POST /api/entry/restore のレスポンス(RestoreResult)
export type RestoreResult = {
    ticketDisplayId: string;
};

// POST /api/entry/checkin のレスポンス(CheckinResult)
export type CheckinResult = {
    groupNumber: number;
    status: GroupStatus;
};

// GET /api/entry/group/{joinToken} のレスポンス(GroupInfoView)
export type GroupInfoView = {
    groupNumber: number;
    memberCount: number;
    isFull: boolean;
    isJoinable: boolean;
};

// === 電子券(Models/API/TicketViews.cs) ===
// GET /api/ticket/{guid} のレスポンス(TicketView)。status はグループ状態かチケット状態
export type TicketView = {
    number: number;
    status: GroupStatus | TicketStatus;
    eventId: string | null;
    eventName?: string | null;
    groupNumber?: number | null;
    currentCallingNumber?: number | null;
    aheadCount?: number | null;
    joinToken?: string | null;
    isRepresentative?: boolean;
};

export const TICKET_STATUS_LABEL: Record<GroupStatus | TicketStatus, string> = {
    Matching: "グループ編成中",
    Waiting: "呼び出し待ち",
    Calling: "呼び出し中",
    Interrupted: "割り込み待ち",
    Completed: "受付完了 ✓",
    Cancelled: "無効",
    Registered: "参加登録済み",
};

// === イベント(Models/API/EventViews.cs) ===
// GET /api/event/List の要素(EventListItem)
export type EventListItem = {
    name: string;
    id: string;
};

// === ユーザー・ロール(Models/API/SendUser.cs) ===
export type SendAuthority = {
    name: string;
};

export type SendRole = {
    name: string;
    authorities: SendAuthority[];
};

export type SendUser = {
    userName: string;
    roles: SendRole[];
};

// GET /api/user/GetPasscode のレスポンス(PasscodeView)
export type PasscodeView = {
    passcode: string;
};

// === Web Push(Models/API/PushViews.cs) ===
// GET /api/push-subscription/vapid-public-key のレスポンス(VapidPublicKeyView)
export type VapidPublicKeyView = {
    publicKey: string;
};

// === 統一レスポンス(Models/API/ApiMessage.cs) ===
export type ApiMessage = {
    message: string;
};

// 409/404 などサーバー側メッセージ({ message } が標準。ProblemDetails 等の保険付き)を読み取る
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
