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

/**
 * 呼び出しアナウンスを読み上げる。
 * @param number 呼び出し番号(例: 1003)
 * @param people グループ人数
 * @param repeat 繰り返し回数(既定2回)
 */
export function speakCallAnnouncement(number: number, people: number, repeat = 2): void {
    if (!isSpeechSupported()) return;

    const synth = window.speechSynthesis;
    synth.cancel();

    const text = `呼び出しいたします。${number}番のお客様、${people}名様、こちらまでお越しください。`;

    for (let i = 0; i < repeat; i++) {
        const utterance = new SpeechSynthesisUtterance(text);
        utterance.lang = "ja-JP";
        const voice = pickJapaneseVoice();
        if (voice) utterance.voice = voice;
        utterance.rate = 0.95;
        synth.speak(utterance);
    }
}
