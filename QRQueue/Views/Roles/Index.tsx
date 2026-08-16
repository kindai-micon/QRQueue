import { useState, useEffect } from "preact/hooks";
import Layout from "@/Shared/Layout";

type SendAuthority = {
    name: string;
};

type SendRole = {
    name: string;
    authorities: SendAuthority[];
};

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
        setRoles(await res.json());
    }

    async function fetchAuthorityList() {
        const res = await fetch("/api/Role/AuthorityList");
        setAuthorityList(await res.json());
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
        <Layout>
            <style>{`
                .roles-container { display: flex; flex-direction: column; align-items: center; padding: 2rem; }
                .input-area { margin-bottom: 1rem; display: flex; gap: 0.3rem; }
                .input-area input[type="text"] { padding: 0.4rem; width: 200px; }
                .role-card {
                    border: 1px solid #ccc; padding: 1rem; margin-bottom: 1rem;
                    width: 300px; text-align: left;
                }
                .role-header { font-weight: bold; margin-bottom: 1rem; text-align: center; }
                .authority-toggle {
                    display: flex; justify-content: space-between; align-items: center; margin: 0.3rem 0;
                }
                .switch { position: relative; display: inline-block; width: 40px; height: 20px; }
                .switch input { opacity: 0; width: 0; height: 0; }
                .slider {
                    position: absolute; cursor: pointer; background-color: #ccc;
                    border-radius: 34px; top: 0; left: 0; right: 0; bottom: 0; transition: .2s;
                }
                .slider:before {
                    position: absolute; content: ""; height: 14px; width: 14px;
                    left: 3px; bottom: 3px; background-color: white; border-radius: 50%; transition: .2s;
                }
                .switch input:checked + .slider { background-color: #4caf50; }
                .switch input:checked + .slider:before { transform: translateX(20px); }
                .delete-button {
                    color: white; background-color: red; border: none;
                    padding: 0.4rem 0.6rem; float: right; cursor: pointer;
                }
                .add-role-button {
                    padding: 10px 20px; background-color: #007bff; color: white;
                    border: none; border-radius: 3px; cursor: pointer; transition: background-color 0.3s ease;
                }
                .add-role-button:disabled { background-color: #ccc; cursor: not-allowed; }
                .switch input:disabled + .slider { background-color: #aaa; cursor: not-allowed; opacity: 0.6; }
            `}</style>
            <div class="roles-container">
                <h2>ロール管理</h2>
                <form class="input-area" onSubmit={addRole}>
                    <input
                        type="text"
                        placeholder="新しいロール名"
                        value={newRoleName}
                        onInput={(e) => setNewRoleName(e.currentTarget.value)}
                    />
                    <button class="add-role-button" type="submit" disabled={!newRoleName.trim()}>追加</button>
                </form>

                {roles.map((role) => (
                    <div class="role-card" key={role.name}>
                        <div class="role-header">
                            {role.name}
                            {role.name !== "Admin" && (
                                <button class="delete-button" onClick={() => deleteRole(role.name)}>削除</button>
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
