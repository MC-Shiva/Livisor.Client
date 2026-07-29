# GitHub Projects 連携ワークフロー

Issue / PR の動きに合わせて GitHub Projects (v2) の `Status` を自動更新します。
`Livisor` と `Livisor.Client` の両リポジトリに同じ内容を配置し、**同一のプロジェクト盤**を共有します。

## ワークフロー一覧

| ファイル | トリガー | 動作 |
| --- | --- | --- |
| `add-to-project.yml` | Issue が作成された | プロジェクトにアイテムとして追加 |
| `project-status-on-assigned.yml` | Issue に担当者が付いた | `Status` → **Todo** |
| `project-status-on-branch.yml` | ブランチが作成された | `Status` → **In Progress** |
| `project-status-on-pr-issues-only.yml` | PR が作成 / Draft 解除された | `Status` → **In Review** |
| `project-status-on-merge-issues-done.yml` | PR がマージされた | `Status` → **Done** |

Issue 番号は**ブランチ名に含まれる最初の数字**から判定します。そのため
`feat/12-add-login` のように `<type>/<Issue番号>-<説明>` の形式で切ってください。
数字が見つからない場合や該当 Issue が存在しない場合は、ジョブは成功のまま何もしません。

## セットアップ（未完了 — 手動作業が必要）

ワークフローは配置済みですが、以下 3 点が揃うまで動きません。

### 1. プロジェクト盤の `Status` 選択肢を用意する

`https://github.com/orgs/MC-Shiva/projects/1` を開き、`Status` フィールドに以下 4 つの
選択肢があることを確認してください。GitHub のデフォルトは `Todo` / `In Progress` / `Done` の
3 つなので、**`In Review` は手動で追加する必要があります。**

- `Todo`
- `In Progress`
- `In Review`
- `Done`

> 表記は大文字小文字・スペースまで完全一致で判定します。盤側の名前を変えた場合は
> 各ワークフロー先頭の `TARGET_STATUS` も合わせて書き換えてください。

### 2. GitHub App を作成して org にインストールする

Organization の Projects (v2) は `secrets.GITHUB_TOKEN` では書き換えられないため、
GitHub App のトークンを使います。

1. `https://github.com/organizations/MC-Shiva/settings/apps/new` から App を作成
   - **Permissions → Organization permissions → Projects: `Read and write`**
   - **Permissions → Repository permissions → Issues: `Read and write`**
   - **Permissions → Repository permissions → Pull requests: `Read-only`**
   - Webhook は不要（`Active` のチェックを外す）
2. 作成後の画面で **App ID** を控える
3. **Private keys → Generate a private key** で `.pem` をダウンロード
4. **Install App** から `MC-Shiva` org にインストールし、`Livisor` と `Livisor.Client`
   の両リポジトリを対象に含める

### 3. シークレットを登録する

org レベルで一度登録すれば両リポジトリから参照できます。

```bash
# App ID
gh secret set APP_ID --org MC-Shiva --visibility all --body "<App ID>"

# 秘密鍵 (.pem ファイルをそのまま)
gh secret set APP_PRIVATE_KEY --org MC-Shiva --visibility all < ~/Downloads/<app-name>.private-key.pem
```

org 設定を触れない場合は、各リポジトリに個別登録でも動きます。

```bash
for r in MC-Shiva/Livisor MC-Shiva/Livisor.Client; do
  gh secret set APP_ID --repo "$r" --body "<App ID>"
  gh secret set APP_PRIVATE_KEY --repo "$r" < ~/Downloads/<app-name>.private-key.pem
done
```

## 設定値を変える場合

各ファイル先頭の `env` ブロックに `▼▼▼ 環境に合わせて変更してください ▼▼▼` の
マーカー付きでまとまっています。

```yaml
env:
  PROJECT_OWNER: MC-Shiva   # Organization 名
  PROJECT_NUMBER: "1"       # プロジェクト URL 末尾の数字
  TARGET_STATUS: In Progress
```

プロジェクト番号を変える場合は、**両リポジトリの全 5 ファイル**を書き換えてください。

## 動作確認

1. Issue を 1 件作成 → プロジェクト盤に追加されること
2. その Issue に自分をアサイン → `Todo` になること
3. `feat/<Issue番号>-test` でブランチを作成 → `In Progress` になること
4. PR を作成 → `In Review` になること
5. PR をマージ → `Done` になること

失敗した場合は Actions のログを確認してください。設定ミスは
`project ... not found` / `status '...' not found` として明示的に失敗します。
