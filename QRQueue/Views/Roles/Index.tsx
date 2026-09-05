import { useState, useEffect } from "preact/hooks";
import Layout from "@/Shared/Layout";
import type { SendRole } from "@/Shared/api";

// SvelteKit routes/roles/+page.svelte から移行
export default function Index() {
    const [roles, setRoles] = useState<SendRole[]>([]);
    const [authorityList, setAuthorityList] = useState<string[]>([]);
    const [newRoleName, setNewRoleName] = useState("");

    useEffect(() => {
        fetchRoles();
        fetchAuthorityList();
    }, []);

    async function fetchRoles() {
        const res = await fetch("/api/Role/RoleList");
        setRoles(await res.json() as SendRole[]);
    }

    async function fetchAuthorityList() {
        const res = await fetch("/api/Role/AuthorityList");
        setAuthorityList(await res.json() as string[]);
    }

    async function addRole(e: Event) {
        e.preventDefault();
        if (!newRoleName.trim()) return;

        const res = await fetch("/api/Role/CreateRole", {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify(newRoleName.trim()),
        });

        if (res.ok) {
            setNewRoleName("");
            await fetchRoles();
        } else {
            alert("ロールの追加に失敗しました");
        }
    }

    async function deleteRole(roleName: string) {
        const res = await fetch("/api/Role/DeleteRole", {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify(roleName.trim()),
        });

        if (res.ok) {
            await fetchRoles();
        } else {
            alert("ロールの削除に失敗しました");
        }
    }

    async function toggleAuthority(roleName: string, authority: string, has: boolean) {
        const url = has ? "/api/Role/RemoveAuthority" : "/api/Role/AddAuthority";

        await fetch(url, {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({ roleName, authority }),
        });

        await fetchRoles();
    }

    function hasAuthority(role: SendRole, authority: string): boolean {
        return role.authorities.some((a) => a.name === authority);
    }

    return (
        <Layout title="ロール管理 | QRQueue">
            <link rel="stylesheet" href="/css/roles.css" />
            <div class="roles-container">
                <h2>ロール管理</h2>
                <form class="input-area" onSubmit={addRole}>
                    <input
                        type="text"
                        placeholder="新しいロール名"
                        value={newRoleName}
                        onInput={(e) => setNewRoleName(e.currentTarget.value)}
                    />
                    <button class="btn-primary" type="submit" disabled={!newRoleName.trim()}>追加</button>
                </form>

                {roles.map((role) => (
                    <div class="role-card" key={role.name}>
                        <div class="role-header">
                            {role.name}
                            {role.name !== "Admin" && (
                                <button class="btn-danger btn-sm" onClick={() => deleteRole(role.name)}>削除</button>
                            )}
                        </div>

                        {authorityList.map((authority) => (
                            <div class="authority-toggle" key={authority}>
                                <span>{authority}</span>
                                <label class="switch">
                                    <input
                                        type="checkbox"
                                        checked={hasAuthority(role, authority)}
                                        disabled={role.name === "Admin"}
                                        onChange={() => toggleAuthority(role.name, authority, hasAuthority(role, authority))}
                                    />
                                    <span class="slider"></span>
                                </label>
                            </div>
                        ))}
                    </div>
                ))}
            </div>
        </Layout>
    );
}

