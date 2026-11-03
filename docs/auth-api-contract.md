# 認証・全体アカウント管理API契約

[← 43 認証設計の入口](43-passkey-authentication-design.md)

## API契約

| メソッド・パス | 用途・応答 |
| --- | --- |
| `GET /health` | 未認証の稼働確認。DBの正常性は示さない |
| `GET /auth/csrf` | 同一OriginへCSRFトークンを返す |
| `POST /auth/login/options` / `verify` | 名前入力なしの標準認証、開始時に地域IDを受け取り、成功時にセッションとCookieを発行 |
| `POST /auth/enrollment/options` / `verify` | optionsへ`code`と初回`displayName`、verifyへ標準資格情報を送る。登録先はコードで固定し、自動ログインしない |
| `GET /auth/me` | `authUserId`、アカウントの`displayName`、`expiresAtUtc`、`verifiedAtUtc`。人物コードは返さない |
| `POST /auth/logout` | 現在セッションを失効 |
| `POST /auth/reauthentication/options` / `verify` | 有効セッションの本人を再確認。期限は延ばさない |
| `GET /auth/passkeys` | 本人の鍵メタデータだけを返す |
| `POST /auth/passkeys/options` / `verify` | 直近本人確認後に本人へ鍵を追加 |
| `DELETE /auth/passkeys/{id}` | 最後の鍵を拒否し、削除成功時は全セッションを失効 |
| `GET /admin/accounts` | 全体Admin専用。アカウントID順の`authUserId`・`displayName`・`isEnabled`。人物行は含めない |
| `POST /admin/accounts` | 全体Admin・直近本人確認・CSRFが必要。`requestId`だけを受け取り、新規一般アカウントとコードを発行 |
| `POST /admin/accounts/{id}/enrollment-code` | 同じ条件で無効・鍵なしの一般／Adminへ再発行 |

メール・パスワード登録を開く`MapIdentityApi`は使わず、標準パスキー機能へ薄い受付と画面を接続する。通常起動はCookie認証・OAuth・管理者APIを常に公開し、必須設定が不備なら起動しない。設定と管理コマンドの扱いは[42](42-api-foundation.md#認証の必須設定)を参照する。管理者の全失効HTTP経路・権限変更は未実装であり、この表に含めない。人物の追加は家計簿側の後続機能とする。

## 運用と家計簿の境界

固定HTTPS・既知プロキシ・永続化・バックアップ・全失効後の復旧は[42](42-api-foundation.md)。ログイン成功を家計簿の利用許可とは扱わない。家計簿の参加・権限・人物との任意対応は[58](58-ledger-foundation-design.md)で管理する。

## 管理APIの再送と対象条件

`POST /admin/accounts`は空でないUUIDの`requestId`だけを受け取り、人物・名前・ロール等の追加項目は400。初回201で`authUserId`・`code`・`expiresAtUtc`を返す。同じ管理者・要求IDの再送は200で同じIDを返し、コードと期限はnull。別管理者が同じ要求IDを使う場合は409。別要求IDは別の新規アカウントとなる。

再発行は対象アカウントがなければ404、有効または鍵が残る場合は409。停止中のAdminにも別の有効Adminから発行でき、表示名・ロールを維持する。初回発行・再発行とも最新Admin・有効セッション・直近5分未満の本人確認・Origin・CSRFを保存前後で検証し、途中失敗は全変更を戻す。再発行の同時成功は最後に確定したコードだけを有効とする。

`GET /admin/accounts`は登録待ち・停止中・人物未対応を含むアカウントID順。公開項目はID・表示名・利用可否だけで、参照に直近本人確認・CSRFを要求しない。名前だけの人物は含めず、役割・鍵・セッション・コード等を公開しない。

