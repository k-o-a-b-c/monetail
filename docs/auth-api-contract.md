# 認証・全体アカウント管理API契約

[← 43 認証設計の入口](43-passkey-authentication-design.md)

## API契約

| メソッド・パス | 用途・応答 |
| --- | --- |
| `GET /health` | 未認証の稼働確認。DBの正常性は示さない |
| `GET /auth/csrf` | 同一OriginへCSRFトークンを返す |
| `POST /auth/login/options` / `verify` | 名前入力なしの標準認証、開始時に地域IDを受け取り、成功時にセッションとCookieを発行 |
| `POST /auth/enrollment/options` / `verify` | optionsへ`token`と初回`displayName`、verifyへ標準資格情報を送る。登録先は招待トークンで固定し、自動ログインしない |
| `GET /auth/me` | `authUserId`、アカウントの`displayName`、`expiresAtUtc`、`verifiedAtUtc`。人物コードは返さない |
| `POST /auth/logout` | 現在セッションを失効 |
| `POST /auth/reauthentication/options` / `verify` | 有効セッションの本人を再確認。期限は延ばさない |
| `GET /auth/passkeys` | 本人の鍵メタデータだけを返す |
| `POST /auth/passkeys/options` / `verify` | 直近本人確認後に本人へ鍵を追加 |
| `DELETE /auth/passkeys/{id}` | 最後の鍵を拒否し、削除成功時は全セッションを失効 |
| `GET /admin/accounts` | 全体Admin専用。アカウントID順の`authUserId`・`displayName`・`isEnabled`。人物行は含めない |
| `POST /admin/accounts` | 全体Admin・直近本人確認・CSRFが必要。`requestId`だけを受け取り、新規一般アカウントと招待リンクを発行 |
| `POST /admin/accounts/{id}/enrollment-invitation` | 同じ条件で無効・鍵なし・未登録の一般／初期Adminへ招待再発行。登録済み停止者はCLI復旧へ分離 |

メール・パスワード登録を開く`MapIdentityApi`は使わず、標準パスキー機能へ薄い受付と画面を接続する。通常起動はCookie認証・OAuth・管理者APIを常に公開し、必須設定が不備なら起動しない。設定と管理コマンドの扱いは[42](42-api-foundation.md#認証の必須設定)を参照する。管理者の全失効HTTP経路・権限変更は未実装であり、この表に含めない。人物の追加は家計簿側の後続機能とする。

<a id="自己登録apidec-110112仕様確定未実装"></a>
<a id="自己登録apidec-110未実装の設計案"></a>

<a id="招待リンクapicliの目標契約dec-113未実装"></a>

## 招待リンクAPI・CLI（DEC-113・実装済み）

`POST /auth/enrollment/options`は**`{"token":"...","displayName":"..."}`を受け付け、旧`code`入力や追加項目は400で拒否する**。`/auth/enrollment/verify`のWebAuthn資格情報はそのまま利用する。登録用のトークンは43文字のbase64url（乱数32バイト）で、リンク`<固定HTTPS Origin>/auth/enroll#token=<token>`を受け取ったブラウザーからだけPOST本文へ渡す。DBにはハッシュのみ保存し、生トークンを保存・ログ出力しない。画面からのコード入力も廃止する。

| 経路・操作 | 実装後の応答・制約 |
| --- | --- |
| `POST /admin/accounts` | 初回201だけ`authUserId`・`enrollmentUrl`・`expiresAtUtc`。再送200は同じIDだがURL/期限はnull。従来の`code`応答は廃止 |
| `POST /admin/accounts/{id}/enrollment-invitation` | Admin＋直近本人確認＋CSRFで未利用・鍵なしの既存アカウントへリンクを再発行。旧`/enrollment-code`は廃止し、旧リンクは失効 |
| `DELETE /admin/accounts/{id}/enrollment-invitation` | Admin＋直近本人確認＋CSRFで未使用リンクを取消。未使用の有効な招待がなくても204を返し、利用者アカウントは削除しない |
| `POST /auth/enrollment/options` | `{"token":"...","displayName":"..."}` を受け付ける。招待が有効・未消費・未取消であることを検証。旧`code` DTOは廃止 |
| `POST /auth/enrollment/verify` | 既存の資格情報検証。成功時だけ招待の使用済み化と鍵・有効化を原子的に確定 |
| `GET /auth/enroll` | 招待トークンをフラグメントからメモリへ取り込み、すぐにURLから削除。入力欄は表示名のみ |
| `GET /auth/register` | 公開モードのみ提供する招待なし登録画面。`InvitationOnly`では表示・利用不可 |
| `GET /auth/login` | 招待制では新規登録リンクなし、公開モードのみ新規登録リンクを表示 |

通常の新規一般ユーザー招待の有効期限は**7日**、`initialize-admin`による初期管理者招待は**30分**とする。登録済み停止アカウントのWeb再発行は409で拒否する。運営者による`issue-recovery-link`は暫定10分を維持し、本人向けの自己復旧は実装しない。`initialize-admin`・`issue-enrollment-invitation`・`issue-recovery-link`は、**固定`Passkeys:Origin`を用いて招待URLだけを標準出力**し、Origin未設定時は生コードによる代替を禁止する。APIもCLIも互換用`code`は返さない。

旧コード受付・通常招待への10分固定を残す移行期は設けない。CLI緊急復旧の10分はDEC-114の明示した別用途とする。ユーザーによるリンク開封は消費に含めず、登録成功時の1回だけ消費する。取消・再発行・期限切れ、管理者の権限失効、並行登録、フラグメント除去、誤ったOrigin、通常のログイン／OAuth回帰をテストする。取消・再発行・登録を保存する前後でも現在の状態を確認し、失敗時はロールバックする。 招待リンクの正本は[DEC-113](auth-passkeys.md#招待リンクの詳細契約dec-113利用者確認済み未実装)。

## 自己登録API（DEC-110・112・実装済み）

以下の匿名APIを実装した。`Registration:Mode=Public`への変更とAPI再起動で利用可能になる。既定の`InvitationOnly`は維持し、両APIを403で拒否する。登録画面はDEC-050・113で追加した。CAPTCHA・退会・全パスキー喪失時の自己復旧は含まない。

| 実装済みの経路 | 処理 |
| --- | --- |
| `POST /auth/self-registration/options` | 未認証で表示名を受け取り、一般公開設定・入力条件・CSRF等を検証し、WebAuthn作成オプションと未完了チャレンジを返す。アカウントはまだ確定しない |
| `POST /auth/self-registration/verify` | 未認証で資格情報を検証し、現在も一般公開中であることを確かめ、一般アカウントと鍵を同一トランザクションで作成する。自動ログインはしない |

既存の`/auth/enrollment/options`・`verify`は、**事前発行された認証IDと招待トークンに固定する別用途**として残す。初期管理者・CLI復旧を自己登録の匿名経路に移さない。既存の`/admin/accounts`や家計簿招待APIも維持する。自己登録では全体Admin権限・家計簿参加権限を付与しない。

両APIとも既存の固定HTTPS・Origin・CSRF・WebAuthnのセキュリティ条件を維持する。自己登録はBearerを送った場合も同一OriginとCSRFを要求する。認証IP回数枠だけを、安全な公開登録の十分条件とはみなさない。

### 要求・応答とHTTP境界（DEC-112・実装済み）

| API | 要求JSON | 成功応答 |
| --- | --- | --- |
| `POST /auth/self-registration/options` | `{"displayName":"表示名"}` | `200`とWebAuthn作成オプション（サーバーが仮の認証IDをチャレンジに固定） |
| `POST /auth/self-registration/verify` | `{"credential":{...}}` | `200`と登録完了メッセージ。Cookie・アクセストークンは発行しない |

既存の`/auth/enrollment/options`・`verify`とは用途を分離する。基本パス・入力・HTTP応答はDEC-112で確定。既存のHTTPS、Origin、CSRF、本文64 KiB、標準WebAuthn検証、認証IP回数制限を弱めない。レスポンス内の案内文面と詳細DTOの拡張は実装レビューで定める。

| 条件 | HTTP応答 | DB状態 |
| --- | --- | --- |
| 招待制で自己登録を要求 | `403` | 追加・更新なし。完了側でもモード確認 |
| 表示名不正・チャレンジ期限切れ／再利用・WebAuthn検証失敗 | `400` | 追加・更新なし |
| 書込失敗 | `503` | 一括ロールバック |
| IP別の開始・共通認証回数、成功100件／24時間、自己登録チャレンジの容量超過 | `429` | 追加・更新なし |
| Cookie要求のOriginが不正 | `403`（現行ミドルウェア） | 追加・更新なし |
| CSRF検証に失敗 | `400`（現行EndpointFilter） | 追加・更新なし |

サーバー起動時の設定固定と再起動でのチャレンジ破棄を前提とし、設定を即時変更するAPIやホットリロードは設けない。**20回／分／IPの共通枠、5回／10分／IPの開始枠、成功100件／直近24時間のDB永続枠、5分・最大128件のチャレンジ枠**をDEC-112の確定仕様とする。成功記録とアカウント／鍵は同一SQLiteトランザクションで確定し、リセットで成功数を回避できない。正本は[認証・DEC-112](auth-passkeys.md#自己登録api濫用防止の確定仕様dec-112未実装)。実装後でもパスキー全紛失時の本人復旧・退会と共有データの扱いを整えるまで、不特定多数への本番公開を開始しない。

### 実装済みと後続工程

1. **完了（DEC-111）**：設定読込・既定値・起動時検証。既存の登録・ログイン・管理CLI・Windows OAuthは維持。
2. **完了（DEC-112）**：公開モード専用options・verify、WebAuthn原子登録、IP・成功・チャレンジの上限とDBマイグレーション。権限・並行保存・巻き戻し・上限境界を自動テストで確認。
3. **完了（DEC-050・113）**：招待登録画面とPublicだけの新規登録入口、登録成功からの明示ログインを接続。
4. 一般公開を実際に有効化する前に、匿名登録の濫用防止・全パスキー紛失時の本人復旧・退会と共有データの扱いを別設計・検証する。

### 自己登録APIの自動テスト

- 招待制で匿名のoptions／verifyを拒否し、認証DBに新規行を残さない。モードを公開から招待へ切り替えた途中要求も確定させない。
- 一般公開で表示名の正常値・不正値を検証し、成功時だけ有効な一般アカウントとパスキーを作成。全体Admin・家計簿参加・セッションは作成しない。
- WebAuthnの署名・RP ID・Origin・UV・CSRF・チャレンジ再利用と期限切れ、途中失敗、同時完了、レスポンス喪失、DB保存失敗を検証し、不正登録・半端な登録を防ぐ。
- 管理者発行リンクの登録／再発行、初期管理者CLI、ログイン・OAuth、家計簿作成・招待・参加を回帰確認する。
- 共通IP 20／分、開始IP 5／10分（移動窓）、24時間成功100件（移動窓・DB永続・同時要求も上限を超えない）、チャレンジ128件・5分とIP追跡4096キーの上限をテストする。招待制の既存チャレンジ1024件へ影響しないことも検証する。

実Identity・隔離SQLite・HTTPで検証し、保存各段階とコミットの失敗、DBロック、URL末尾や大文字小文字での制限迂回、信頼していない転送元IP、再起動と認証リセット、既存APIからのデータ保持移行も対象とする。件数・採用結果と実端末での未検証事項は[tests/README](../tests/README.md)を参照する。

## 運用と家計簿の境界

固定HTTPS・既知プロキシ・永続化・バックアップ・全失効後の復旧は[42](42-api-foundation.md)。ログイン成功を家計簿の利用許可とは扱わない。家計簿の参加・権限・人物との任意対応は[58](58-ledger-foundation-design.md)で管理する。

## 管理APIの再送と対象条件

`POST /admin/accounts`は空でないUUIDの`requestId`だけを受け取り、人物・名前・ロール等の追加項目は400。初回201で`authUserId`・`enrollmentUrl`・`expiresAtUtc`を返す。同じ管理者・要求IDの再送は200で同じIDを返し、URLと期限はnull。別管理者が同じ要求IDを使う場合は409。別要求IDは別の新規アカウントとなる。

再発行は対象アカウントがなければ404、有効または鍵が残る場合は409。登録済み停止アカウントは409。未登録かどうかは表示名・消費済み登録・セッション履歴で確認する。停止後の復旧は運営者の明示的なCLIへ分離する。初回発行・再発行とも最新Admin・有効セッション・直近5分未満の本人確認・Origin・CSRFを保存前後で検証し、途中失敗は全変更を戻す。再発行の同時成功は最後に確定したリンクだけを有効とする。取消は有効な未使用招待がなければ204。利用者アカウントは削除しない。

`GET /admin/accounts`は登録待ち・停止中・人物未対応を含むアカウントID順。公開項目はID・表示名・利用可否だけで、参照に直近本人確認・CSRFを要求しない。名前だけの人物は含めず、役割・鍵・セッション・コード等を公開しない。

