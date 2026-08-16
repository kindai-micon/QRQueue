import { useState, useEffect } from "preact/hooks";
import Layout from "@/Shared/Layout";

// SvelteKit routes/+page.svelte から移行
export default function Index() {
    const [userName, setUserName] = useState<string | null>(null);

    useEffect(() => {
        (async () => {
            try {
                const res = await fetch("/api/user/MyInfo");
                if (res.ok) {
                    const data = await res.json();
                    setUserName(data?.userName ?? null);
                }
            } catch (error) {
                console.error("ユーザー情報の取得に失敗:", error);
            }
        })();
    }, []);

    return (
        <Layout>
            {userName !== null && <h1>Welcome {userName}</h1>}
        </Layout>
    );
}
