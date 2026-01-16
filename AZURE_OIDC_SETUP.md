# GitHub ActionsでのAzure OIDC自動デプロイ設定ガイド

## 概要
このガイドでは、GitHub ActionsからAzure AppServiceへOIDC（OpenID Connect）を使用して自動デプロイする手順を説明します。

## Azure側の設定手順

### 1. Azure AD アプリケーションの作成
以下のコマンドを実行してアプリケーションを作成します：

```bash
az ad app create --display-name "Kabeuchi-GitHub-Deploy" --query "appId" -o tsv
```

出力されたアプリIDをメモしておきます（例: `4e584e36-c6c2-4083-9cf7-2006ef1cfbac`）

### 2. サービスプリンシパルの作成
```bash
az ad sp create --id <appId> --query "id" -o tsv
```

出力されたサービスプリンシパルオブジェクトIDをメモしておきます。

### 3. フェデレーション資格情報の作成
以下の内容でJSONファイルを作成します（`credential.json`）：

```json
{
  "name": "GitHub-Main-Deploy",
  "issuer": "https://token.actions.githubusercontent.com",
  "subject": "repo:ylearning86/Kabeuchi-AI:ref:refs/heads/main",
  "audiences": ["api://AzureADTokenExchange"]
}
```

次のコマンドでフェデレーション資格情報を作成します：

```bash
az ad app federated-credential create --id <appId> --parameters credential.json
```

### 4. AppServiceへの権限付与
サブスクリプション内のAppServiceに対してContributor権限を付与します：

```bash
az role assignment create \
  --role "Contributor" \
  --assignee-object-id <servicePrincipalObjectId> \
  --scope "/subscriptions/dfbae745-0767-476a-a131-6fefa69ae9a8/resourceGroups/kabeuchi-rg"
```

## GitHub側の設定

### 1. GitHub Secretsの作成
以下の3つのシークレットをGitHubリポジトリに設定します：

| シークレット名 | 値 | 説明 |
|---|---|---|
| `AZURE_CLIENT_ID` | Azure ADアプリケーションのAppID | Azure AD認証用 |
| `AZURE_TENANT_ID` | `16b3c013-d300-468d-ac64-7eda0820b6d3` | テナントID |
| `AZURE_SUBSCRIPTION_ID` | `dfbae745-0767-476a-a131-6fefa69ae9a8` | サブスクリプションID |

#### 設定手順：
1. GitHubリポジトリの **Settings** > **Secrets and variables** > **Actions** に移動
2. **New repository secret** をクリック
3. 各シークレットを追加

### 2. ワークフローファイル
ワークフローファイルは既に `.github/workflows/azure-appservice-deploy.yml` に設定されています。

このワークフローは以下の処理を実行します：
- `main`ブランチへのプッシュ時に自動実行
- .NET 8.0でビルド
- AppServiceへOIDC認証でデプロイ

## 動作確認

1. リポジトリの任意のファイルを変更して `main` ブランチにプッシュ
2. GitHub Actions の **Actions** タブでワークフローの実行を確認
3. デプロイが成功するとAppServiceが更新されます

## トラブルシューティング

### AZURE_CLIENT_IDが無効
- AppIDが正しく設定されているか確認
- AppIDが誤って削除されていないか確認

### テナントIDまたはサブスクリプションIDが無効
- Secretsに正しい値が設定されているか確認

### AppServiceへのデプロイに失敗
- サービスプリンシパルにAppServiceへの権限があるか確認
- AppService名が正しいか確認（`kabeuchi`）

## 参考資料
- [Azure/login - GitHub Actions](https://github.com/Azure/login)
- [Azure/webapps-deploy - GitHub Actions](https://github.com/Azure/webapps-deploy)
- [Azure AD OIDC設定](https://learn.microsoft.com/ja-jp/azure/active-directory/workload-identities/workload-identity-federation)
