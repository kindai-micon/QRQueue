import { useState, useEffect } from "preact/hooks";
import type { ComponentChildren } from "preact";

// SvelteKit routes/+layout.svelte から移行
// (未ログインなら /login へリダイレクトする管理画面共通レイアウト)
const MENU_ITEMS = [
    { name: "ユーザー管理", href: "/users" },
    { name: "ロール管理", href: "/roles" },
    { name: "イベント管理", href: "/event" },
];

export default function Layout({ children, chrome = "full" }: { children?: ComponentChildren; chrome?: "full" | "header" }) {
    const [userName, setUserName] = useState<string | null>(null);
    const [checked, setChecked] = useState(false);
    const [drawerOpen, setDrawerOpen] = useState(false);

    useEffect(() => {
        // ヘッダーのみのページ(ログイン・初期登録・チケット確認)では
        // 認証チェックを行わない(Svelte 版レイアウトと同じ挙動)
        if (chrome === "header") return;

        (async () => {
            try {
                const res = await fetch("/api/user/MyInfo");
                if (res.ok) {
                    const data = await res.json();
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
            <style>{`
                * { box-sizing: border-box; }
                body { margin: 0; font-family: sans-serif; }
                .app-container { display: flex; flex-direction: column; min-height: 100vh; }
                .layout-header {
                    display: flex; align-items: center; justify-content: space-between;
                    background-color: #3f51b5; color: white; padding: 0.75rem 1rem;
                    position: sticky; top: 0; z-index: 100;
                }
                .header-left { display: flex; align-items: center; }
                .menu-button {
                    display: none; font-size: 1.5rem; background: none; border: none;
                    color: white; cursor: pointer; margin-right: 1rem;
                }
                .title { font-size: 1.2rem; font-weight: bold; }
                .layout-body { display: flex; flex: 1; height: 100%; }
                .sidebar {
                    width: 240px; background-color: #f4f4f4; padding: 1rem;
                    border-right: 1px solid #ddd;
                }
                .sidebar nav a, .drawer nav a {
                    display: block; padding: 0.5rem; color: #333;
                    text-decoration: none; border-radius: 4px;
                }
                .sidebar nav a:hover, .drawer nav a:hover { background-color: #ddd; }
                .main { flex: 1; padding: 1rem; }
                .drawer {
                    position: fixed; top: 0; left: 0; width: 240px; height: 100%;
                    background-color: #f4f4f4; padding: 1rem;
                    box-shadow: 2px 0 5px rgba(0, 0, 0, 0.2); z-index: 200;
                }
                .drawer-close {
                    background: none; border: none; font-size: 1.2rem;
                    margin-bottom: 1rem; cursor: pointer;
                }
                @media (max-width: 768px) {
                    .sidebar { display: none; }
                    .menu-button { display: block; }
                    .layout-body { flex-direction: column; }
                }
            `}</style>
            <div class="app-container">
                <header class="layout-header">
                    <div class="header-left">
                        {chrome === "full" && (
                            <button class="menu-button" onClick={() => setDrawerOpen(!drawerOpen)}>☰</button>
                        )}
                        <div class="title">QRQueue 管理システム</div>
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
