# GitHub Actions OIDC デプロイ設定 - 完了チェックリスト

## 概要
GitHub ActionsからAzure AppServiceへOIDC（OpenID Connect）で自動デプロイする設定です。

## 📋 実施済み項目

✅ **ワークフローファイル更新**
- `.github/workflows/azure-appservice-deploy.yml` をOIDC対応に更新
- `azure/login@v2` を使用（OIDC対応）
- `id-token: write` パーミッションを追加

✅ **フェデレーション資格情報の作成**
- GitHub Actions用Azure ADアプリケーション作成
- `main`ブランチからのデプロイに対応するフェデレーション資格情報を設定

## 🔧 次に実施する必要がある項目

### 1️⃣ Azure AD アプリケーション作成（GitHub Secretsに登録するため）

#### PowerShellまたはBashで実行：
```bash
# テナントIDでログイン（16b3c013-d300-468d-ac64-7eda0820b6d3）
az login --tenant 16b3c013-d300-468d-ac64-7eda0820b6d3

# アプリケーション作成
az ad app create --display-name "Kabeuchi-GitHub-Deploy" --query "appId" -o tsv
```

出力例：`4e584e36-c6c2-4083-9cf7-2006ef1cfbac` ← **このIDをメモ**

### 2️⃣ サービスプリンシパル作成
```bash
az ad sp create --id <上記のappId> --query "id" -o tsv
```

出力例：`bc658468-b6ae-46a4-ae67-16ea5097019e` ← **このIDをメモ**

### 3️⃣ フェデレーション資格情報作成

`credential.json`を作成：
```json
{
  "name": "GitHub-Main-Deploy",
  "issuer": "https://token.actions.githubusercontent.com",
  "subject": "repo:ylearning86/Kabeuchi-AI:ref:refs/heads/main",
  "audiences": ["api://AzureADTokenExchange"]
}
```

実行：
```bash
az ad app federated-credential create --id <appId> --parameters credential.json
```

### 4️⃣ AppService へのIAM権限付与
```bash
az role assignment create \
  --role "Contributor" \
  --assignee-object-id <servicePrincipalObjectId> \
  --scope "/subscriptions/dfbae745-0767-476a-a131-6fefa69ae9a8/resourceGroups/kabeuchi-rg"
```

### 5️⃣ GitHub Repository Secrets設定

GitHubリポジトリの **Settings** > **Secrets and variables** > **Actions** で以下を追加：

| Secret名 | 値 |
|---------|-----|
| `AZURE_CLIENT_ID` | 手順1で取得したアプリID |
| `AZURE_TENANT_ID` | `16b3c013-d300-468d-ac64-7eda0820b6d3` |
| `AZURE_SUBSCRIPTION_ID` | `dfbae745-0767-476a-a131-6fefa69ae9a8` |

## ✅ 動作確認

1. リポジトリの任意のファイルを変更して`main`ブランチにプッシュ
2. GitHub Actions タブでワークフロー実行を確認
3. AppServiceが更新されたことを確認

## 📋 環境情報

| 項目 | 値 |
|------|-----|
| テナントID | 16b3c013-d300-468d-ac64-7eda0820b6d3 |
| サブスクリプションID | dfbae745-0767-476a-a131-6fefa69ae9a8 |
| リソースグループ | kabeuchi-rg |
| AppService名 | kabeuchi |
| AppService URL | https://kabeuchi.azurewebsites.net |

## 🔗 参考資料

- [Azure/login GitHub Action](https://github.com/Azure/login)
- [OpenID Connect を使用した Azure ログイン](https://docs.microsoft.com/ja-jp/azure/active-directory/workload-identities/workload-identity-federation)
- [Azure/webapps-deploy GitHub Action](https://github.com/Azure/webapps-deploy)
