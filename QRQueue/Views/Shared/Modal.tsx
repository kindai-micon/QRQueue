// alert() の代替として使う共通メッセージモーダル。
// site.css の .modal-overlay / .modal 系スタイルを流用する。
// message が null のときは何も描画しない。
import type { FunctionComponent } from "preact";

type Props = {
    message: string | null;
    onClose: () => void;
    title?: string;
};

const MessageModal: FunctionComponent<Props> = ({ message, onClose, title = "お知らせ" }) => {
    if (message === null) return null;
    return (
        <div class="modal-overlay">
            <div class="modal">
                <div class="modal-title">{title}</div>
                <div class="modal-content" style={{ whiteSpace: "pre-wrap" }}>{message}</div>
                <div class="modal-buttons">
                    <button class="btn-primary btn-sm" onClick={onClose}>OK</button>
                </div>
            </div>
        </div>
    );
};

export default MessageModal;
