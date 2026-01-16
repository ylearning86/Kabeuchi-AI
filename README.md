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

1. **Azure PortalでApp Serviceリソースを作成**

2. **Azure ADでアプリ登録を作成**
   - Azure Portal > Azure Active Directory > アプリの登録 > 新規登録
   - 任意の名前でアプリケーションを登録（例: `kabeuchi-ai-github-actions`）
   - クライアントIDとテナントIDをメモ

3. **フェデレーション資格情報の設定**
   - 作成したアプリの登録 > 証明書とシークレット > フェデレーション資格情報
   - 「資格情報の追加」をクリック
   - フェデレーション資格情報のシナリオ: `GitHub Actions deploying Azure resources`
   - 組織: GitHubのユーザー名またはOrg名（例: `ylearning86`）
   - リポジトリ: リポジトリ名（例: `Kabeuchi-AI`）
   - エンティティタイプ: `Branch`
   - GitHub ブランチ名: `main`
   - 名前: 任意（例: `github-actions-main-branch`）

4. **AzureのRBACロールを設定**
   - Azure Portal > App Service > アクセス制御(IAM)
   - 「ロールの割り当ての追加」をクリック
   - ロール: `Website Contributor` または `Contributor`
   - メンバー: 手順2で作成したアプリを選択

5. **GitHubシークレットの設定**
   - GitHubリポジトリの Settings > Secrets and variables > Actions
   - 以下のシークレットを追加:
     - `AZURE_CLIENT_ID`: 手順2で取得したクライアントID
     - `AZURE_TENANT_ID`: 手順2で取得したテナントID
     - `AZURE_SUBSCRIPTION_ID`: AzureポータルのサブスクリプションID

6. **ワークフローファイルの確認**
   - 必要に応じて `.github/workflows/azure-appservice-deploy.yml` の `AZURE_WEBAPP_NAME` 環境変数をApp Service名に合わせて変更

## ライセンス

TBD

## 貢献

TBD
# Test Deployment - 2026年 1月 16日 金曜日 16:08:00 TST
