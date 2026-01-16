# Kabeuchi-AI

ビジネスプラン壁打ちWebアプリケーション

## 概要

Kabeuchi-AIは、ビジネスプランのアイデアを整理し、検証するための壁打ち（ブレインストーミング）支援Webアプリケーションです。
AIを活用して、ビジネスアイデアに対するフィードバックや質問を提供し、計画の精度を高めることを目指します。

## 技術スタック

- **バックエンド**: .NET 8 WebAPI
- **フレームワーク**: ASP.NET Core

## プロジェクト構成

```
Kabeuchi-AI/
├── KabeuchiAI/          # .NET 8 WebAPI プロジェクト
├── .github/
│   └── workflows/       # GitHub Actions CI/CD
└── README.md
```

## 開発環境のセットアップ

### 必要要件

- .NET 8 SDK

### ビルド・実行

```bash
# プロジェクトディレクトリに移動
cd KabeuchiAI

# パッケージの復元
dotnet restore

# ビルド
dotnet build

# 実行
dotnet run
```

アプリケーションは `https://localhost:7272` または `http://localhost:5104` で起動します。

### テスト実行

```bash
cd KabeuchiAI
dotnet test
```

## CI/CD

GitHub Actionsを使用して、mainブランチへのpush/PR時に自動的にビルドとテストが実行されます。

### Azure App Serviceへの自動デプロイ

mainブランチへのpush時に、Azure App Serviceへ自動的にデプロイされます。

#### セットアップ手順

1. Azure PortalでApp Serviceリソースを作成
2. App Serviceの「発行プロファイル」をダウンロード
3. GitHubリポジトリの Settings > Secrets and variables > Actions で以下のシークレットを追加:
   - `AZURE_WEBAPP_PUBLISH_PROFILE`: ダウンロードした発行プロファイルの内容
4. 必要に応じて `.github/workflows/azure-appservice-deploy.yml` の `AZURE_WEBAPP_NAME` 環境変数をApp Service名に合わせて変更

## ライセンス

TBD

## 貢献

TBD
