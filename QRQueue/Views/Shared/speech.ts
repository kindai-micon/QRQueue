// Web Speech API による呼び出し読み上げユーティリティ。
// 呼び出しコンソール(/event/{id}/call)から利用する。音声は端末内蔵の
// speechSynthesis(日本語ボイス)を使用するため、追加リソースは不要。

export function isSpeechSupported(): boolean {
    return typeof window !== "undefined" && "speechSynthesis" in window;
}

function pickJapaneseVoice(): SpeechSynthesisVoice | null {
    const voices = window.speechSynthesis.getVoices();
    return voices.find((v) => v.lang?.toLowerCase().startsWith("ja")) ?? null;
}

/// 指定テキストを日本語ボイスで repeat 回読み上げる。
/// 再生開始前に既存の読み上げをキャンセルする。
function speakJapanese(text: string, repeat: number): void {
    if (!isSpeechSupported()) return;

    const synth = window.speechSynthesis;
    synth.cancel();

    for (let i = 0; i < repeat; i++) {
        const utterance = new SpeechSynthesisUtterance(text);
        utterance.lang = "ja-JP";
        const voice = pickJapaneseVoice();
        if (voice) utterance.voice = voice;
        utterance.rate = 0.95;
        synth.speak(utterance);
    }
}

/**
 * 呼び出しアナウンスを読み上げる。
 * @param number 呼び出し番号(例: 1003)
 * @param people グループ人数
 * @param repeat 繰り返し回数(既定2回)
 */
export function speakCallAnnouncement(number: number, people: number, repeat = 2): void {
    speakJapanese(
        `呼び出しいたします。${number}番のお客様、${people}名様、こちらまでお越しください。`,
        repeat);
}

/**
 * 呼び出しアナンスに続き、優先プール(割り込み待ち)のグループも
 * 同じ読み上げに含める。プールが空の場合は通常の呼び出しと同じ。
 * @param groups 現在優先プールにいるグループ(番号と人数)
 */
export function speakCallWithPoolAnnouncement(
    number: number,
    people: number,
    groups: { number: number; people: number }[],
    repeat = 2): void {
    if (groups.length === 0) {
        speakCallAnnouncement(number, people, repeat);
        return;
    }

    const targets = groups.map((g) => `${g.number}番のお客様`).join("、");
    speakJapanese(
        `呼び出しいたします。${number}番のお客様、${people}名様、こちらまでお越しください。` +
        `あわせて優先でご案内します。${targets}も、こちらまでお越しください。`,
        repeat);
}

/**
 * 再呼び出しアナウンスを読み上げる(番号が変わらないまま再度呼ばれたとき)。
 * @param repeat 繰り返し回数(既定2回)
 */
export function speakCallAgainAnnouncement(number: number, people: number, repeat = 2): void {
    speakJapanese(
        `改めて呼び出しいたします。${number}番のお客様、${people}名様、こちらまでお越しください。`,
        repeat);
}

/**
 * 優先プール(割り込み待ち)に新しく入ったグループを読み上げる。
 * 複数グループが同時に入った場合は1文にまとめて読み上げる。
 * @param groups 新しくプールに入ったグループ(番号と人数)
 * @param repeat 繰り返し回数(既定2回)
 */
export function speakInterruptedAnnouncement(
    groups: { number: number; people: number }[], repeat = 2): void {
    if (groups.length === 0) return;

    const targets = groups.map((g) => `${g.number}番のお客様`).join("、");
    speakJapanese(
        `優先でご案内します。${targets}、お手数ですが再度受付までお越しください。`,
        repeat);
}
