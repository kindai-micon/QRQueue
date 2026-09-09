# サーバーセットアップ(nginx + Cloudflare Tunnel)

本番サーバーのリバースプロキシ構成。アプリ本体のデプロイは `deploy.yml`(GitHub Actions)が行い、
このディレクトリのファイルは **初回セットアップ時に手動で配置する**(アプリの更新で変わらないため)。

```
利用者 ─HTTPS→ Cloudflare ─Tunnel(外向き)→ cloudflared → nginx(:80) → qrqueue.service(127.0.0.1:5000)
```

## 1. nginx(リバースプロキシ)

```bash
sudo cp nginx/qrqueue.conf /etc/nginx/sites-available/qrqueue.conf
sudo ln -s /etc/nginx/sites-available/qrqueue.conf /etc/nginx/sites-enabled/
sudo rm -f /etc/nginx/sites-enabled/default   # 既存のデフォルトサイトがあれば
sudo nginx -t && sudo systemctl reload nginx
```

- SignalR の WebSocket アップグレード(`Upgrade` / `Connection` ヘッダ)に対応済み
- `Host` / `X-Forwarded-For` / `X-Forwarded-Proto` をアプリへ伝搬する
  (アプリ側の `UseForwardedHeaders` が 127.0.0.1 を信頼するよう設定済み — Program.cs 参照)

## 2. cloudflared(Cloudflare Tunnel)

```bash
sudo mkdir -p /etc/cloudflared
# Cloudflare ダッシュボードまたは CLI でトンネルを作成
cloudflared tunnel create qrqueue
sudo cp cloudflared/config.yml /etc/cloudflared/config.yml
# config.yml の <TUNNEL-ID> を実行結果の値に置換する
sudo cp ~/.cloudflared/<TUNNEL-ID>.json /etc/cloudflared/

# 公開ホスト名の紐付け(Cloudflare 側。リポジトリ外の作業)
cloudflared tunnel route dns qrqueue <hostname>

sudo cp cloudflared.service /etc/systemd/system/cloudflared.service
sudo systemctl daemon-reload
sudo systemctl enable --now cloudflared.service
```

- トンネルはサーバーから**外向き**に接続するため、インバウンドのポート開放は不要
- nginx(:80)と cloudflared は同一ホストを想定

## 3. アプリ側で対応済みのこと

- `UseForwardedHeaders` が同一ホストの nginx(`127.0.0.1` / `::1`)からの
  `X-Forwarded-*` を信頼する(Program.cs)。これにより QR 掲示PDF に埋め込む
  BaseURL が `https://<公開ドメイン>` になり、HTTPS リダイレクトのループも起きない
- WebSocket(SignalR)は Kestrel が素通りで扱うためアプリ側の追加対応は不要
