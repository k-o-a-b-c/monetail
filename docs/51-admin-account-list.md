# 51 管理者向けアカウント一覧

**家計簿や人物から独立したアカウント一覧APIを実装。** [43 認証設計](43-passkey-authentication-design.md)に従い、全体管理者だけへ公開する。人物一覧・家計簿の参加者一覧とは分ける。

## API契約

`GET /admin/accounts`は有効なログインと現在の`Admin`ロールを要求し、認証ID順に返す。

```json
[
  { "authUserId": "example-account-a", "displayName": "表示名A", "isEnabled": true },
  { "authUserId": "example-account-b", "displayName": null, "isEnabled": false }
]
```

- `AspNetUsers`だけを起点にし、人物対応なし・家計簿未参加・登録待ち・停止中のアカウントも含む。名前だけの人物は含めない。
- 表示名はアカウント自身の保存値。初回登録前や補完しない旧行は`null`。`isEnabled=false`から登録待ち／停止を推測せず、鍵・セッションの有効性を示す値とも扱わない。
- 公開項目は認証ID・表示名・利用可否だけ。人物コード・Windows用`account_name`・ロール・コード・ハッシュ・資格情報・セッション情報は返さない。名前から自動対応しない。

## 保護と範囲

参照に直近本人確認・CSRFは要求せず、認証更新のレート枠も消費しない。要求ごとに有効セッション・アカウント・最新ロールを確認する。未認証・失効・利用停止は401、一般利用者・Admin取消後は403、DB障害は秘密を含めない503。人物未対応では拒否しない。302転送・Cookie／期限更新・DB更新は行わない。

固定HTTPS・ホスト／ポート・`no-store`等は`/auth`と共通。不一致は400。パスキー設定なしの既定構成は`/health`のみで、このAPIを公開しない。

配置は引き続き`Features/Monetail/Accounts`（[52](52-reusable-authentication-boundaries.md)）。一覧からの新規発行APIは[54](54-admin-account-creation.md)、再発行は[55](55-admin-enrollment-code-reissue.md)。Web管理画面・家計簿参加・人物管理は後続（[56](56-new-user-onboarding-policy.md)、[57](57-ledger-data-ownership.md)）。Windows版・業務値・保存済み`receipt.total`は変更しない。検証結果は[tests/README.md](../tests/README.md)を参照する。
