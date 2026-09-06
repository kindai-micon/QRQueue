# インフラ構成

QRQueue のインフラ構成図(Mermaid)。デプロイ経路は PR #17 により Tailscale 経由の SSH。
各図の PNG は `images/` 配下に同梱(`mmdc -i <>.mmd -o <>.png -s 2 -b white` で再生成可能)。

## 全体構成

参加者・スタッフからのアクセスと、リリースサーバー上の構成。

```mermaid
flowchart LR
    subgraph users["利用者"]
        P["参加者(スマホ)<br/>電子券 / PWA / Web Push"]
        S["スタッフ・管理者(ブラウザ)<br/>呼び出しコンソール / 投影 / 管理画面"]
    end

    subgraph server["リリースサーバー(Linux)"]
        APP["qrqueue.service (systemd)<br/>.NET 10 ASP.NET Core + JsxCore<br/>:5000"]
        DB[("PostgreSQL<br/>lottery-db")]
        APP --- DB
    end

    PUSH["ブラウザ プッシュサービス<br/>(FCM / APNs)"]

    P -- "HTTP / WebSocket(SignalR)<br/>:5000" --> APP
    S -- "HTTP / WebSocket(SignalR)<br/>:5000" --> APP
    APP -- "Web Push (VAPID)" --> PUSH
    PUSH -- "呼び出し通知" --> P
```

![全体構成](images/infrastructure-overall.png)

- アプリは `ASPNETCORE_URLS=http://*:5000` で待ち受け(`.deploy/qrqueue.service`)
- DB 接続文字列は GitHub Secrets `APPSETTINGS_JSON` 経由で `appsettings.json` として配置
- PDF(QuestPDF)・QR生成はアプリ内で完結(外部サービスなし)

## デプロイフロー

`release` ブランチへの push を起点に、GitHub Actions が Tailscale 経由でSSHデプロイする。

```mermaid
sequenceDiagram
    autonumber
    participant D as 開発者
    participant G as GitHub(release)
    participant A as GitHub Actions<br/>(ubuntu-latest)
    participant T as Tailscale tailnet
    participant S as リリースサーバー

    D->>G: push(release ブランチ)
    G->>A: ワークフロー起動
    A->>A: dotnet restore / build / publish
    A->>T: tailnet に参加<br/>(OAuth クライアント, tag:ci, ephemeral)
    A->>T: ping でデプロイ先の到達確認
    A->>S: ssh-keyscan(known_hosts 登録)
    A->>S: scp .deploy/qrqueue.service
    A->>S: rsync publish-output/
    A->>S: appsettings.json 配置 + systemctl restart
```

![デプロイフロー](images/deploy-flow.png)

- runner は `tag:ci` 付きの ephemeral ノードで、ワークフロー終了後に自動削除される
- 接続先 `DEPLOY_HOST` は MagicDNS 名または Tailscale IP(`100.x.x.x`)
- サーバーのSSHを公開インターネットに晒す必要がない

## 開発環境

ローカルでは .NET Aspire でアプリと PostgreSQL コンテナを起動する。

```mermaid
flowchart LR
    DEV["開発者"] --> AH["QRQueue.Aspire.AppHost<br/>.NET Aspire 9.5"]
    AH --> APP["QRQueue プロジェクト"]
    AH --> PG[("PostgreSQL コンテナ<br/>(Docker Desktop)")]
    APP --- PG
```

![開発環境](images/dev-env.png)
