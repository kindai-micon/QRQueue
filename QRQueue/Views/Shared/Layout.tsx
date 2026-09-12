import { useState, useEffect } from "preact/hooks";
import type { ComponentChildren } from "preact";
import type { SendUser } from "@/Shared/api";
import "/css/layout.css";
import "/css/site.css";

// SvelteKit routes/+layout.svelte から移行
// (未ログインなら /login へリダイレクトする管理画面共通レイアウト)
const MENU_ITEMS = [
    { name: "ユーザー管理", href: "/users" },
    { name: "ロール管理", href: "/roles" },
    { name: "イベント管理", href: "/event" },
    { name: "パスワード変更", href: "/account/password" },
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

    async function handleLogout() {
        try {
            await fetch("/api/user/Logout", { method: "POST" });
        } catch (error) {
            console.error("ログアウトに失敗:", error);
        } finally {
            window.location.href = "/login";
        }
    }

    return (
        <div>
            {title && <title>{title}</title>}
            <div class="app-container">
                <header class="layout-header">
                    <div class="header-left">
                        {chrome === "full" && (
                            <button class="menu-button" onClick={() => setDrawerOpen(!drawerOpen)}>☰</button>
                        )}
                        <div class="layout-title">QRQueue 管理システム</div>
                    </div>
                    <div class="header-right">Powered by 近畿大学プログラミング研究部</div>
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
                            <button class="logout-link" onClick={handleLogout}>ログアウト</button>
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
                                <button class="logout-link" onClick={handleLogout}>ログアウト</button>
                            </nav>
                        </aside>
                    )}
                    <main class="main">{children}</main>
                </div>
                <footer class="layout-footer">Powered by 近畿大学プログラミング研究部</footer>
            </div>
        </div>
    );
}

