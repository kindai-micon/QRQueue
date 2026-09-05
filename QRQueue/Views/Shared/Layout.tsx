import { useState, useEffect } from "preact/hooks";
import type { ComponentChildren } from "preact";
import type { SendUser } from "@/Shared/api";

// SvelteKit routes/+layout.svelte から移行
// (未ログインなら /login へリダイレクトする管理画面共通レイアウト)
const MENU_ITEMS = [
    { name: "ユーザー管理", href: "/users" },
    { name: "ロール管理", href: "/roles" },
    { name: "イベント管理", href: "/event" },
];

export default function Layout({ children, chrome = "full", title }: { children?: ComponentChildren; chrome?: "full" | "header"; title?: string }) {
    const [userName, setUserName] = useState<string | null>(null);
    const [checked, setChecked] = useState(false);
    const [drawerOpen, setDrawerOpen] = useState(false);

    // タブにURLではなくページ名を表示する(SSR の <title> に加えてクライアントでも確定させる)
    useEffect(() => {
        if (title) document.title = title;
    }, [title]);

    useEffect(() => {
        // ヘッダーのみのページ(ログイン・初期登録・チケット確認)では
        // 認証チェックを行わない(Svelte 版レイアウトと同じ挙動)
        if (chrome === "header") return;

        (async () => {
            try {
                const res = await fetch("/api/user/MyInfo");
                if (res.ok) {
                    const data: SendUser = await res.json();
                    setUserName(data?.userName ?? null);
                }
            } catch (error) {
                console.error("ユーザー情報の取得に失敗:", error);
            } finally {
                setChecked(true);
            }
        })();
    }, [chrome]);

    useEffect(() => {
        if (checked && userName === null) {
            window.location.href = "/login";
        }
    }, [checked, userName]);

    return (
        <div>
            {title && <title>{title}</title>}
            <link rel="stylesheet" href="/css/layout.css" />
            <link rel="stylesheet" href="/css/site.css" />
            <div class="app-container">
                <header class="layout-header">
                    <div class="header-left">
                        {chrome === "full" && (
                            <button class="menu-button" onClick={() => setDrawerOpen(!drawerOpen)}>☰</button>
                        )}
                        <div class="layout-title">QRQueue 管理システム</div>
                    </div>
                </header>
                {drawerOpen && (
                    <div class="drawer">
                        <button class="drawer-close" onClick={() => setDrawerOpen(false)}>✖ 閉じる</button>
                        <nav>
                            {MENU_ITEMS.map((item) => (
                                <a key={item.href} href={item.href} onClick={() => setDrawerOpen(false)}>
                                    {item.name}
                                </a>
                            ))}
                        </nav>
                    </div>
                )}
                <div class="layout-body">
                    {chrome === "full" && (
                        <aside class="sidebar">
                            <nav>
                                {MENU_ITEMS.map((item) => (
                                    <a key={item.href} href={item.href}>{item.name}</a>
                                ))}
                            </nav>
                        </aside>
                    )}
                    <main class="main">{children}</main>
                </div>
            </div>
        </div>
    );
}

