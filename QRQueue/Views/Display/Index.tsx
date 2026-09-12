import { useState, useEffect, useRef } from "preact/hooks";
import type { HubConnection } from "@microsoft/signalr";
import Layout from "@/Shared/Layout";
import type { EventInfoView, QueueView } from "@/Shared/api";
import { isSpeechSupported, speakCallAnnouncement, speakCallAgainAnnouncement, speakCallWithPoolAnnouncement, speakInterruptedAnnouncement } from "@/Shared/speech";
import "/css/display.css";

type Model = {
    eventId: string; // eventDisplayId
};

// 投影用画面(設計書 /display/[eventid]、旧 view 置換)。
// 現在呼び出し中を大型表示 + 直近履歴。呼び出し時に強調アニメーションのみ。
export default function Display({ model }: { model: Model }) {
    const [queue, setQueue] = useState<QueueView | null>(null);
    const [eventName, setEventName] = useState<string>("");
    const [history, setHistory] = useState<number[]>([]);
    const [flash, setFlash] = useState(false);
    const [denied, setDenied] = useState(false);

    // 呼び出し読み上げ(音声合成)。投影画面ごとにトグルで ON/OFF を指定し、
    // ON にしたこの画面だけが読み上げる(他の画面では音が出ない)。
    // 設定は localStorage に永続化し、再読み込み後も引き継ぐ。
    // 注意: このページは SSR(ServerAndClient)でサーバー側でも初期化されるため、
    // ブラウザ API へのアクセスは必ず存在チェックをしてから行う。
    const speechSupported = isSpeechSupported();
    const [ttsOn, setTtsOn] = useState(() =>
        typeof localStorage !== "undefined" && localStorage.getItem("displayTts") === "1");
    const ttsRef = useRef(ttsOn);
    // 直近の呼び出し中グループの人数(再呼び出し読み上げ用。SignalR の Called には人数が含まれない)
    const callingPeopleRef = useRef(1);
    // 直近の優先プール(割り込み待ち)の番号集合。増えたグループだけを読み上げる
    const interruptedRef = useRef<Set<number>>(new Set());

    function toggleTts() {
        setTtsOn((on) => {
            const next = !on;
            ttsRef.current = next;
            if (typeof localStorage !== "undefined") {
                localStorage.setItem("displayTts", next ? "1" : "0");
            }
            if (typeof window !== "undefined" && !next) window.speechSynthesis?.cancel();
            return next;
        });
    }

    useEffect(() => {
        (async () => {
            try {
                const res = await fetch(`/api/entry/${model.eventId}`);
                if (res.ok) {
                    const data: EventInfoView = await res.json();
                    setEventName(data.eventName ?? "");
                }
            } catch {
                // イベント名は表示上の補助のみ
            }
        })();
    }, [model.eventId]);

    useEffect(() => {
        let connection: HubConnection | null = null;
        let disposed = false;
        let poll: number | undefined;
        let lastCalling: number | null = null;
        let firstLoad = true;

        async function load() {
            try {
                const res = await fetch(`/api/call/queue/${model.eventId}`);
                if (res.status === 401 || res.status === 403) {
                    setDenied(true);
                    return;
                }
                if (!res.ok) return;
                setDenied(false);
                const data: QueueView = await res.json();
                setQueue(data);

                const current = data.callingGroup[0]?.number ?? null;
                if (current !== lastCalling) {
                    if (current != null) {
                        // 新しい呼び出しに切り替わった → 直近履歴へ積み、強調アニメ再生
                        setHistory((h) => [current, ...h.filter((n) => n !== current)].slice(0, 6));
                        setFlash(true);
                        setTimeout(() => setFlash(false), 1600);
                        // 読み上げが有効な表示画面のみ音声アナウンス。
                        // 優先プール(割り込み待ち)がいれば呼び出しにあわせて同じ読み上げに含める
                        if (ttsRef.current) {
                            speakCallWithPoolAnnouncement(
                                current,
                                data.callingGroup[0]?.people ?? 1,
                                data.interruptedGroup.map((g) => ({ number: g.number, people: g.people })));
                        }
                    }
                    lastCalling = current;
                }
                callingPeopleRef.current = data.callingGroup[0]?.people ?? 1;

                // 優先プール(割り込み待ち)の番号が増えたときだけ読み上げる
                // (初回ロード時は前回集合が空でも読み上げない)
                const interrupted = new Set(data.interruptedGroup.map((g) => g.number));
                if (!firstLoad && ttsRef.current) {
                    const added = data.interruptedGroup.filter((g) => !interruptedRef.current.has(g.number));
                    if (added.length > 0) {
                        speakInterruptedAnnouncement(added.map((g) => ({ number: g.number, people: g.people })));
                    }
                }
                interruptedRef.current = interrupted;
                firstLoad = false;
            } catch (err) {
                console.error("キュー情報の取得に失敗:", err);
            }
        }

        (async () => {
            try {
                const { HubConnectionBuilder, HttpTransportType } = await import("@microsoft/signalr");
                connection = new HubConnectionBuilder()
                    .withUrl("/api/queueHub", { skipNegotiation: true, transport: HttpTransportType.WebSockets })
                    .withAutomaticReconnect()
                    .build();
                connection.on("Called", (args?: { groupNumber?: number }) => {
                    // 再呼び出し(番号が変わらないまま Called が再送)の検知。
                    // サーバーは next/again の両方で Called を送るため、
                    // - 番号が前回と同じ → 再呼び出し: ここで読み上げ(lastCalling は据え置き)
                    // - 番号が変わった   → 新規呼び出し: load() 内の変更検知で読み上げる(二重読み上げ防止)
                    // SignalR が使えない環境ではポーリングの番号変更検知がフォールバックになる。
                    const n = args?.groupNumber;
                    if (n != null && n === lastCalling && ttsRef.current) {
                        speakCallAgainAnnouncement(n, callingPeopleRef.current);
                    }
                    load();
                });
                connection.on("QueueChanged", load);
                connection.onreconnected(async () => {
                    await connection?.invoke("SetEvent", model.eventId);
                    await load();
                });
                await connection.start();
                if (disposed) {
                    await connection.stop();
                    return;
                }
                await connection.invoke("SetEvent", model.eventId);
            } catch (err) {
                console.error("SignalR connection setup error:", err);
            }
            await load();
            if (!disposed) {
                // CallController 側は SignalR を送らない運用でも追従できるようポーリング併用
                poll = window.setInterval(load, 10000);
            }
        })();

        return () => {
            disposed = true;
            if (poll) window.clearInterval(poll);
            connection?.stop().catch(() => { /* ignore */ });
        };
    }, [model.eventId]);

    const calling = queue?.callingGroup[0] ?? null;
    // 次に呼ばれるグループ(サーバー側で番号順に並んでいる)
    const next = queue?.waitingGroup[0] ?? null;

    if (denied) {
        return (
            <Layout chrome="header" title="呼び出し表示 | QRQueue">
                <div class="display-denied">
                    <h1>表示できません</h1>
                    <p>
                        この画面は <code>CallView</code> 権限を持つアカウントでログインすると見られます。
                        <a href="/login">ログインへ</a>
                    </p>
                </div>
            </Layout>
        );
    }

    return (
        <Layout chrome="header" title="呼び出し表示 | QRQueue">
            <div class="display-screen">
                <div class="display-event">
                    {eventName}
                    {speechSupported && (
                        <span class="display-tts-controls">
                            <button
                                class={`display-tts-toggle ${ttsOn ? "display-tts-on" : ""}`}
                                onClick={toggleTts}
                                title="この画面で呼び出し番号を音声読み上げします(ONにした画面だけ鳴ります)"
                            >
                                {ttsOn ? "🔊 読み上げ ON" : "🔇 読み上げ OFF"}
                            </button>
                            {ttsOn && (
                                <button
                                    class="display-tts-toggle"
                                    onClick={() => speakCallAnnouncement(calling?.number ?? 1001, calling?.people ?? 1)}
                                    title="読み上げの音量を確認できます(ブラウザの音声再生許可もここで与えられます)"
                                >
                                    🔈 テスト
                                </button>
                            )}
                        </span>
                    )}
                </div>

                <div class={`display-now ${flash ? "display-flash" : ""}`}>
                    <div class="display-now-label">いま呼び出し中</div>
                    {calling ? (
                        <div class="display-now-number">{calling.number}</div>
                    ) : (
                        <div class="display-now-empty">——</div>
                    )}
                    {calling && (
                        <div class="display-now-people">{calling.people} 人の方、受付までお越しください</div>
                    )}
                </div>

                {next && (
                    <div class="display-next">
                        <span class="display-next-label">次のグループ</span>
                        <span class="display-next-number">{next.number} 番</span>
                        <span class="display-next-people">{next.people} 人</span>
                    </div>
                )}

                <div class="display-side">
                    <div class="display-block">
                        <div class="display-block-title">待ち</div>
                        <div class="display-block-value">{(queue?.waitingGroup.length ?? 0)}</div>
                    </div>
                    <div class="display-block">
                        <div class="display-block-title">割り込み待ち</div>
                        <div class="display-block-value">{(queue?.interruptedGroup.length ?? 0)}</div>
                    </div>
                    <div class="display-block">
                        <div class="display-block-title">プール人数</div>
                        <div class="display-block-value">{(queue?.peoplePool ?? 0)}</div>
                    </div>
                </div>

                {history.length > 0 && (
                    <div class="display-history">
                        <span class="display-history-label">直近の呼び出し</span>
                        {history.map((n, i) => (
                            <span key={`${n}-${i}`} class="display-history-item">{n}</span>
                        ))}
                    </div>
                )}
            </div>
        </Layout>
    );
}
