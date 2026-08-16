<script lang="ts">
    import { page } from '$app/stores';
    import { derived } from 'svelte/store';
    import { onMount } from 'svelte';
    // URLパラメータからイベント名を取得
    const eventId = $page.params.eventid;

    let eventName = "";
    onMount(async () => {
        let res = await fetch(`/api/event/Name?id=${eventId}`);
        console.log(res);

        eventName = await res.text();

        console.log(eventName);

    });
</script>

<style>
    .container {
        padding: 2rem;
        max-width: 800px;
        margin: 0 auto;
    }

    .title {
        font-size: 2rem;
        font-weight: bold;
        margin-bottom: 1.5rem;
        padding-left: 1rem;
        background: linear-gradient(to right, #f0f8ff, transparent);
    }

    .nav {
        display: grid;
        gap: 1rem;
        margin-top: 2rem;
    }

    .link-card {
        display: block;
        padding: 1rem 1.5rem;
        border-radius: 0.75rem;
        background: #f9f9f9;
        border: 1px solid #ddd;
        transition: all 0.2s ease;
        text-decoration: none;
        color: #333;
        font-size: 1.1rem;
        font-weight: 500;
    }

        .link-card:hover {
            background: #e9f5ff;
            border-color: #007acc;
            box-shadow: 0 2px 8px rgba(0,0,0,0.08);
        }

    .desc {
        font-size: 0.9rem;
        color: #666;
        margin-top: 0.25rem;
    }
</style>

<div class="container">
    <div class="title">
        イベント: {eventName}
    </div>

    <div class="nav">
        <a class="link-card" href="{eventId}/publishing">
            チケットの発行
            <div class="desc">チケットを発行できます</div>
        </a>

        <a class="link-card" href="{eventId}/tickets">
            チケット一覧
            <div class="desc">発行済みのチケットの一覧を確認できます</div>
        </a>
    </div>
</div>
