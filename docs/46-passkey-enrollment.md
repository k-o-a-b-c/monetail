# 46 登録コードによるパスキー登録

**登録画面・コード検証・Identityによる登録検証・アカウント有効化まで実装。ログイン・セッションは[47](47-passkey-login-sessions.md)まで実装。管理画面は後続。** [43 認証設計](43-passkey-authentication-design.md)と[45 コード発行](45-initial-admin-enrollment.md)に従う。本番設定・実データ・Windows版は変更していない。

## 有効化と配備設定

既存SQLiteの隔離コピーへ[44 追加移行](44-auth-database-foundation.md)を適用し、[45](45-initial-admin-enrollment.md)の管理コマンドから初期管理者とコードを発行する。通常起動でDBを自動移行しない。今回の登録機能に新しいテーブル・移行はなく、既存のIdentityパスキーストアと登録コード表を使う。

接続文字列と次の設定を配備環境に渡す。実際のホスト名・パス・証明書・コードはコミットしない。URLと証明書の調達方法は運用で扱う。

| 設定キー | 環境変数 | 内容 |
| --- | --- | --- |
| `ConnectionStrings:Monetail` | `ConnectionStrings__Monetail` | 移行済みの既存SQLiteコピー |
| `Passkeys:Origin` | `Passkeys__Origin` | 固定HTTPSオリジン。例示用は`https://monetail.example.test`。パス・クエリ・フラグメント・ユーザー情報を含めない |
| `Passkeys:RpId` | `Passkeys__RpId` | Originと同じホスト名。スキーム・ポート・パスを含めない |
| `Passkeys:DataProtectionKeysPath` | `Passkeys__DataProtectionKeysPath` | Data Protectionキーを永続化するフォルダの絶対パス |
| `Passkeys:TrustedProxies:0`など | `Passkeys__TrustedProxies__0`など | HTTPS終端から転送する場合だけ、そのプロキシのIPアドレスを列挙する |

`Passkeys`セクションがなければ登録関連の経路を公開せず、従来どおり`/health`だけを提供する。セクションを指定した場合はOrigin・RP ID・キー保存先・接続文字列を必須とし、不完全なら起動を拒否する。管理コマンドはこのWeb設定を必要としない。

API自身のHTTPS待受け、または明示した既知プロキシでのHTTPS終端を使う。転送ヘッダーは指定IPからの1段だけを信頼し、転送先ホストもOriginに限定する。プロキシ指定なしでは転送処理を無効にする。登録関連の要求は、転送処理後のHTTPS・ホスト・ポートが設定Originに一致しなければ拒否する。

現在のComposeはDB・キー・証明書をマウントしない稼働確認用のまま。登録を試す配備では、SQLiteの親フォルダとキーのフォルダを永続化し、非rootの実行ユーザーだけが必要な読み書きをできるようにする。Data Protectionのアプリ識別子は`Monetail.Api`。キーはこの実装で保存時に暗号化せず、OSのアクセス制限で保護する。通常の再作成では同じキーとDBを維持する。復元後の全失効手順は[49](49-authentication-revocation-recovery.md)まで実装し、具体的な本番配備は後続とする。

## 画面とHTTP契約

固定Originの`/auth/enroll`を開き、登録コードを貼り付けて「パスキーを登録する」を押す。名前や人物コード・認証IDの入力は受け付けない。端末の本人確認を終えると、画面のコードを消して登録完了を表示する。ログインは自動実行せず、完了後のリンクから[47 ログイン画面](47-passkey-login-sessions.md)へ進む。

| 経路 | 入出力・処理 |
| --- | --- |
| `GET /auth/enroll`、`GET /auth/enroll.js` | 埋込済みの最小HTMLとJavaScriptだけを配信する |
| `GET /auth/csrf` | CSRF Cookieを設定し、JSONの`token`を返す |
| `POST /auth/enrollment/options` | 本文`{"code":"…"}`を受け付け、Identity標準の作成オプションJSONと、チャレンジ用HttpOnly Cookieを返す |
| `POST /auth/enrollment/verify` | 本文`{"credential":{…}}`のWebAuthn応答を標準Identityで検証し、成功時200と完了メッセージを返す。アカウント等のIDを送って登録先を変更できない |

両POSTは`Content-Type: application/json`、同一Originの`Origin`ヘッダー、`GET /auth/csrf`で得た`X-CSRF-TOKEN`が必要。CSRF不正・無効コード・登録検証失敗は人物情報を含めない400、別Originは403、回数超過は429、DB障害等は503とする。本文は64 KiBまでで、それを超える要求は413。コード受付・検証を合わせて送信元IPあたり1分20回に制限し、待ち行列は作らない。

登録コードはPOST本文だけで送り、URLやブラウザーストレージへ保存しない。認証画面・スクリプト・CSRF・認証APIは`no-store`とし、外部スクリプト・フレーム埋込みをCSPで拒否する。Cookieは`__Host-`、Secure・HttpOnly・SameSite=Lax・Path=/、Domainなし。資格情報付きCORSやメール・パスワード登録経路は公開しない。

## 検証と確定の境界

1. コードのSHA-256ハッシュを探し、登録用途・未使用・未失効・10分の期限内であることを確認する。
2. 対象アカウントが無効かつパスキー0件で、人物への対応と人物本体が存在することを確認する。
3. Identityの作成オプションを発行する。ResidentKeyとUserVerificationはともに`required`、attestationは`none`とする。人物名はパスキーの表示用であり、対応の判定には使わない。
4. 対象認証ID・人物コード・コードハッシュ・Identityの検証状態を、サーバーメモリの5分チャレンジに保持する。推測できないCookieで同じブラウザーに結び付ける。最大1024件とし、同じブラウザーの再開始は古いチャレンジを置き換える。
5. 検証要求でチャレンジを原子的に取り出す。成功・失敗とも再利用不可。再起動でも未完了チャレンジを失い、利用者はコード入力からやり直す。チャレンジは単一APIプロセス内で管理する。
6. SQLiteの書込トランザクションを開始して、コード・利用可否・残存パスキー・同じ人物への対応を再確認する。Origin・RP ID・チャレンジ・本人確認・登録済み資格情報等のWebAuthn検証は標準`IPasskeyHandler<AuthUser>`が行う。
7. 検証成功時だけパスキー保存・`IsEnabled=true`・コード消費日時を同じトランザクションで確定する。登録先をクライアントから指定させず、再発行・全失効等と競合して古くなったコードや人物対応の変更を拒否する。途中失敗は全体を戻し、別ブラウザーの同時登録でも二重成功させない。

生コード・Cookie・WebAuthn応答を通常ログへ記録しない。成功時は認証ID・対象人物・日時・結果だけを監査記録へ残す。登録失敗ではコードを消費せず、チャレンジだけを使い切るため、期限内のコードで登録操作をやり直せる。

## 検証範囲と後続

実SQLiteとIdentityの自動検証に加え、localhost専用HTTPS・Chromiumの仮想認証器で、画面からdiscoverable credentialを登録し、コード消去・アカウント有効化・コード消費・既存total保持まで確認した。物理端末のWindows Helloやスマートフォン、本番HTTPS配備は未確認。件数・実行結果は[tests/README.md](../tests/README.md)へ集約する。

通常ログインと固定期限セッション・本人情報・ログアウトは[47](47-passkey-login-sessions.md)まで実装した。本人再確認は[48](48-passkey-reauthentication.md)まで実装した。全失効・復旧コマンドは[49](49-authentication-revocation-recovery.md)まで実装した。本人のパスキー追加／削除は[50](50-self-service-passkeys.md)まで実装した。管理画面、追加管理者の管理、業務APIは後続とする。ログインできても家計の業務APIはまだ提供しない。
