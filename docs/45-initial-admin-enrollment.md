# 45 初期管理者と登録コードの発行

**人物を指定しない初期管理者作成と、認証ID指定のコード再発行を実装。** [43 認証設計](43-passkey-authentication-design.md)、[44 認証DB](44-auth-database-foundation.md)を引き継ぐ。新規空DBの準備と管理画面は未実装（[56](56-new-user-onboarding-policy.md)）。本番DBへの適用は行っていない。

## 隔離したコピーでの操作

既存業務DBのコピーを`ConnectionStrings__Monetail`で指定して追加移行する。接続文字列・生コードはリポジトリへ記録しない。

```sh
dotnet run --project src/Monetail.Api -- migrate-auth-db
dotnet run --project src/Monetail.Api -- initialize-admin
# 出力されたアカウントIDを指定して、必要な場合だけ再発行する。
dotnet run --project src/Monetail.Api -- issue-enrollment-code --account-id ACCOUNT_ID
```

| 操作 | 処理 |
| --- | --- |
| `initialize-admin` | 管理者がまだいなければ、新しい認証ID・Adminロール・無効状態・コードを同じトランザクションで作る。人物が0件でも実行可能 |
| 初期作成の再実行 | 管理者が既にいれば何も変更せず終了し、生コードも再表示しない。既存の一般アカウントを昇格させない |
| `issue-enrollment-code --account-id ID` | 無効かつ鍵なしの対象へ再発行。認証ID・表示名・ロールを維持し、古い未使用コードの失効と新コードを同時に保存 |

成功した発行でだけ、アカウントID・生コード・UTC期限を標準出力へ表示する。応答を失った場合も自動再発行せず、同じIDを指定して明示的に再発行する。旧`--person-code`は拒否する。不存在ID・移行未適用・利用中・鍵が残る対象は終了コード1、成功は0。Webサーバーを起動しない。

人物の選択・新規作成・対応の保存は行わない。表示名は[46](46-passkey-enrollment.md)の初回登録で本人が入力し、復旧では既存名を保持する（DEC-045）。既存人物名からのコピー・自動統合もしない。将来の家計簿ごとの任意対応は別の後続機能とする。

## 保存・保全と後続

コードは32バイトの暗号学的乱数、10分・1回限り。`auth_enrollment_codes`にはSHA-256ハッシュ・認証ID・用途・UTC作成／期限・消費／取消日時だけを保存する。未使用・未取消コードは1アカウント1件の部分一意索引でも制約する。

SQLiteの書込トランザクションで同時操作を直列化する。初期作成失敗ではアカウント・ロール・コードを戻し、再発行失敗では旧コード取消も戻す。監査ログは認証ID・操作・日時・結果だけとし、生コードを通常ログに含めない。コード発行だけでは有効化・ログインしない。

全失効・緊急復旧は[49](49-authentication-revocation-recovery.md)、一般アカウントの初回発行APIは[54](54-admin-account-creation.md)、一般／管理者への再発行APIは[55](55-admin-enrollment-code-reissue.md)を参照する。停止HTTP経路・追加管理者の権限管理・新規空DB準備は後続。人物・取引・過去の更新者・保存済み`receipt.total`は変更しない。検証件数と結果は[tests/README.md](../tests/README.md)に集約する。
