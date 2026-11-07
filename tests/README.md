# テストの構成・実行と結果

件数・実行方法・採用結果の正本。旧VB.NET版の検証と、新版WinUI 3・Clientの検証を区別する。過去の詳細は[CI検証記録](ci-verification-records.md)と凍結済みの[履歴](history.md)、旧Windows版の手動確認は[専用チェックリスト](legacy-manual-checks.md)を参照する。実装のない画面を検証済みとは扱わない。

| 対象 | 件数 |
| --- | ---: |
| 旧版の計算 | 35 |
| SQLite移行（税端数・更新版） | 10 |
| 旧版Windowsの実SQLite・画面・起動設定 | 98 |
| ASP.NET Core API | 858 |
| 登録・ログイン・管理・家計簿招待画面のJavaScript（Node・合成DOM/WebAuthn） | 53 |
| Windows Client（接続・認証・保存・家計簿・人物・参加者・招待・支払方法・補完） | 323 |
| Windowsの実DPAPI・保存 | 3 |
| WinUI 3の製品起動・画面スモーク（1シナリオ） | 1 |
| CIの実行対象選択・成功結果の再利用・配布条件 | 69 |
| 合計 | 1450 |

## 検証する範囲

- Clientは合成HTTPと一時保存先を使う。接続確認、OAuth/ループバック、セッション復元、更新と失効、別プロセスの認証変更、設定/トークン保存、家計簿の起動先・作成・切替、人物のCRUD/順保存/競合/再送を確認する。日付・時刻は補完期間・世紀選択・空欄・不正入力・全角・区切り・地域設定を検証する。
- StorageはWindowsのCurrentUser DPAPIを実際に使い、暗号化保存と新しいStoreでの復元、破損時の認証破棄と接続設定保持、平文の拒否を確認する。UIへの依存はない。
- WinUI 3は製品EXEを起動し、同じMainWindowのXAML・各partial・ClientConnection・PersonRowを使う別のスモークAppで合成データを検証する。ホーム、左右の人物管理、リサイズとテーマに加え、接続設定・ブラウザー起動の失敗と再開・認証キャンセル・実ループバックからの合成認可応答・保存ログインの確認と失効・家計簿作成の再送と選択・接続先変更・ログアウト成功／失敗を1シナリオとして確認する。製品Appから起動時の接続・復元を開始し、遅い接続での同じWindow内の待機表示・完了後の停止・タイムアウトも確認する。実データや保存済みの接続先/トークンは使用しない。 人物の削除確認では参照拒否・競合・通信失敗/応答喪失・降格/除外・認証失効を確認する。人物編集では保存・再送・競合・権限喪失・ContentDialogの保存／取消と背後の移動抑止、同一アカウントの再ログイン後の入力/版/要求IDの復元、別アカウント・別APIへの非引継ぎも検証する。参加者の役割/人物対応・除外/競合/権限喪失と、家計簿招待の発行/再送/コピー/取消も同じ画面の実コントロールで検証する。入力部品は同じハンドラーと実コントロールのフォーカスで確認し、物理キー注入や実IME変換の検証とは区別する。
- APIは実HTTP・合成アカウント・隔離SQLiteを使い、認証・現在の参加/役割・保存失敗の巻き戻し・同時操作・FK・再送・競合・招待の状態と消費・共有回数枠を検証する。CookieとWindows Bearerを区別する。
- 匿名自己登録は実IdentityのWebAuthn検証、保存各段階とコミットの巻き戻し、同時登録・重複鍵、移動窓・容量・信頼済みIP、再起動／認証リセット、既存DBからのデータ保持移行を確認する。登録後の通常ログイン、Bearerでも自己登録のOrigin・CSRFが必要なこと、招待登録を成功数へ含めないことも対象とする。
- 全体AdminのWeb管理はHTML/JS/一覧/更新の権限、登録状態と招待用途の分離、メモ保存/移行/再送/ロールバック、画面内再認証と入力保持、五十音の安定順・URL破棄を対象とする。
- 招待はURL初回発行・7日/30分/CLI復旧10分・ハッシュ保存・取消/再発行（CLI復旧・未知用途は取消対象外）・未登録/利用中/鍵残存へのCLI復旧拒否・二重消費・旧方式拒否・管理者権限変更とロールバック・CLI復旧のID/名前/ロール保持を確認する。画面は製品JSをNodeのvmで実行し、フラグメント除去、保存先の制約、キャンセル/通信失敗の再試行、明示ログイン、OAuth戻り先制限を検証する。実ブラウザーの検証とは区別する。
- Dockerは既存の起動確認に加え、Dockerサーバー用Composeのhostネットワーク・loopback限定待受・外部プロキシ非同梱、隔離bridgeでの秘密ファイル読込・非root起動・PFX再生成拒否・DB/管理者/PFXの保持・転送HTTPSとHost境界を確認する。実際の外部HTTPS終端へは接続しない。
- 旧VB.NET版の計算・DB・画面・移行・性能は既存のテスト構成を維持する。実データ・利用者環境への適用を自動検証済みとは扱わない。

## 登録モード設定の追加検証

`RegistrationSettingsTests` の13ケースは、既定招待制・有効値・不正値・通常API起動時の設定固定・不正値の起動拒否・DB非初期化を対象とする。追加時のPR #101単独の[CI 41644938668](https://github.com/k-o-a-b-c/monetail/actions/runs/41644938668)は成功。これらは下記の自己登録API回帰にも含め、未設定で自己登録を403にすることも確認した。

## 採用結果

科目並び替えAPIに実HTTP/隔離SQLiteの61ケースを追加。無効科目・収支混在・欠番/同順位からの再採番、0件/1件/同順保存時の新版、家計簿Admin限定と全体権限の分離、入力/家計簿境界、全行属性/他の更新版の保持、旧版/同時保存/追加削除との競合、DB条件付きUPDATE、途中障害・降格/除外/認証失効の全体ロールバック、競合情報の保護と共有書込枠を確認する。ローカルReleaseで新規61件と既存の科目CRUD/支払方法並び替え147件が成功。ソース`95993d19b911c225732fe7375718cecfcf588ab8`の[CI 42369954389](https://github.com/k-o-a-b-c/monetail/actions/runs/42369954389)でAPI全858件・Node・Python、Dockerビルドと通常/server/配布アーカイブの起動検証が成功。基点main `a24574d20f6d09d23b88adf6fed23dd45a7729e8`（PR #128マージ）の[CI 42366871026](https://github.com/k-o-a-b-c/monetail/actions/runs/42366871026)成功を変更前の根拠として再利用し、変更前の全件確認と影響のないWindows系・旧版・性能テストは再実行していない。構成合計1450件を全件再実行した意味ではない。DB/Migration・CRUD仕様・Client/WinUI・レシート・既存税計算の変更なし。実運用DB・実Windowsからの操作は未検証。

科目CRUD APIに実HTTP/隔離SQLiteの108ケースを追加（構成API797件）。参加者の閲覧/家計簿Adminのみ更新・全体Adminとの分離、家計簿境界、正規化/UTF-16/無効科目の重複、収支/税区分/boolean/JSON/更新版入力、同時作成/再送/行更新、作成後の改名・削除と再送、RESTRICTと想定外DBエラー分類、独立した順序版、保存前/確定前の降格・除外・認証失効とロールバック、競合情報の失効後非公開、既存20/120回枠の共有を検証する。将来の取引参照はテスト専用FK表のみで再現し、改名時のJOIN表示と保存済み税区分/率の非更新を確認する。対象をローカルで確認し、ソース`684367bba852badcc809cafff8cc962231c4eaa4`の[CI 42361091410](https://github.com/k-o-a-b-c/monetail/actions/runs/42361091410)でAPI全797件・Node・Python、Dockerビルドと通常/server/配布アーカイブ起動検証が成功。影響のないClient/WinUI・旧版・性能テストは再実行していない。DBモデル/Migration・Client/WinUI・レシート・既存税計算は変更していない。基点main `92cab63`の[CI 42354526041](https://github.com/k-o-a-b-c/monetail/actions/runs/42354526041)成功を変更前の根拠とする。

科目設定の移設（DEC-139）は科目/税DB対象48ケースがローカルで成功。旧仕様の費用・発生区分/周期の組合せ20ケースを、新仕様の収支区分INSERT/UPDATE8ケースと、8列/モデル/Designer一致・DB上の収支変更可能性・適用済み旧構造の非自動変換3ケースへ置換したため、API構成は698→689件。名称・家計簿分離・参照・版・税区分/税率履歴・移行往復/全経路の既存検証は維持している。同じMigration IDが記録済みの隔離DBでは、Migrateしても旧11列・税履歴/科目/版が変わらず、initialize-dbの構造照合が拒否することを確認した。実DBは変更・初期化していない。ソース`3e7fbd6e0cecafdc1fc157685034a54524de33d0`の[CI 42351364987](https://github.com/k-o-a-b-c/monetail/actions/runs/42351364987)でAPI全689件・Node・Python、Dockerビルドと起動検証が成功。影響のないClient/WinUI・旧版・性能テストは再実行していない。変更前の根拠はmain `bfb9ce13563e4a74781b40bd44b1f310304d6d61`の[CI 42313249586](https://github.com/k-o-a-b-c/monetail/actions/runs/42313249586)成功を採用する。

科目・共通税区分/税率履歴のDB基盤（DEC-137・138）に57ケースを追加。API全697件の実行と、追加した初期API DB→最新版の全経路保持1件、Node53件、Python判定69件がローカルで成功（現構成API698件）。Nodeは作業環境で子プロセスの集計がファイル単位になったため`--test-isolation=none`で全53ケースを確認した。新規/既存API DB、旧列・認証/パスキー・人物・支払方法の保持、MigrationのUp→Down→Upと再適用、FK/UNIQUE/CHECK・無効科目の重複・将来の複合FK参照、初期値の組合せ、UTF-16境界、行版と家計簿版の独立、Decimal精度、期間両端・終了なし・全重複形・更新・同時挿入・トランザクション破棄を検証する。過去Migrationのテストは対象Migrationを明示し、人物表等を比較から除外せずに保持検証する形へ変更した。加えて初期API DBから最新版までの全経路でも、認証・パスキー・全体権限・家計簿参加/人物対応と全旧列の保持を確認する。最終ソース`3e9487f29a70879c5882c0337e2044f48d580014`の[CI 42294710994](https://github.com/k-o-a-b-c/monetail/actions/runs/42294710994)でAPI全698件・Node・Pythonが成功。Docker/起動検証は製品入力が同じ初回ソース`1a364d3`の[CI 42293545583](https://github.com/k-o-a-b-c/monetail/actions/runs/42293545583)の成功記録を再利用した。基点main `c0f6fee8581707b9806d302cb7811188d29b5acd`の[CI 42233302048](https://github.com/k-o-a-b-c/monetail/actions/runs/42233302048)ではAPI/Dockerが成功し、WinUI起動検証は既存の失敗。本変更でWinUI・旧版・税計算は変更しない。科目API・WinUI・取引テーブル・実データ移行、税履歴の実データ投入と管理運用は未実装。実運用DBへの適用は未検証。

配備用の名称をNAS固有からDockerサーバー用へ変更し、共通要件とTailscale構成例を分離した。旧リリースのアーカイブ名はそのまま保持し、新リリースで`monetail-docker-api-vX.Y.Z.tar.gz`を使用する。実装と配布構成の検証結果はPR #125のCIで確認する。

過去のPR #124ではNAS配布を既存のhostネットワークTailscaleコンテナ前提へ変更した。配布物とComposeからTailscale service・Serve設定・認証キー・state管理を削除し、APIのloopback待受と設定・データ保持を検証する。従前の検証結果は当時の構成の履歴として残す。

GHCR専用スクリプトのCI選択を最適化し、回帰8件を追加。Python全68件と差分検査がローカルで成功した。対象2ファイルの単独・混在変更、追加/変更/実行属性/削除での製品fingerprint維持、例外外への改名と共通/新規スクリプトの安全側判定、mainの成功記録再利用・記録欠落・過去の未検証API変更を確認する。変更前の根拠は基点main `ca21a81d6345eae2003efad69f53f580b1d065fa`の[Tests 42179499790](https://github.com/k-o-a-b-c/monetail/actions/runs/42179499790)成功を再利用する。

初回GHCR配布失敗の修正では、Registry検証13件を追加し、判定・配布条件の全60件とactionlintがローカルで成功。単一manifest/OCI index/provenance、固定参照・digest・platform不一致、既存タグ、404のRegistryエラー種別、401/403/429/500・redirect・通信断・タイムアウトと認証先制限を確認する。失敗実行[42174933547](https://github.com/k-o-a-b-c/monetail/actions/runs/42174933547)はamd64のpush成功後、ローカル`RepoDigests[0]`の形式検証で停止していた（その取得値自体はログになく断定しない）。公開済みの作業用イメージを読み取り、修正スクリプトでamd64のprovenance付きindexとarm64の単一manifestからpushログと一致するdigestを取得し、既存タグ・CPU誤指定の拒否と未公開版の404も確認した。両イメージのmanifest統合は`imagetools create --dry-run`で検証した。新しいタグでの実push・最終manifest公開・Releases作成はマージ後に未検証として残し、`api-v0.1.0`は変更していない。API本体・Dockerfile・NAS構成は未変更で、基点mainの[Tests 42173513238](https://github.com/k-o-a-b-c/monetail/actions/runs/42173513238)成功を変更前の根拠として再利用する。

GHCR/NAS配布はmainの同一SHAのpush Tests成功だけを採用し、PR/fork/別ブランチ/別SHA/失敗・取消の結果と不正バージョンを拒否する。新規5件を含む判定47件、actionlint、Bash構文、開発/NAS/配布用Composeがローカルで成功。最新APIのDockerビルドと通常/NAS/配布アーカイブ起動を確認し、初回再実行拒否、ソースなし配置、コンテナ再作成・停止バックアップ・隔離復元後のDB/DPキー/PFX/Tailscale state用ファイルのSHA256一致と管理者の保持が成功。環境の公開プロキシCAは一時DockerfileのBuildKit secretでビルド時だけ渡し、製品Dockerfileは変更していない。ソース`a2654e8a5896f4aa8f7336011bfce68bc24efc84`の[CI 42165673600](https://github.com/k-o-a-b-c/monetail/actions/runs/42165673600)で判定47件・API641件・Node53件・Client323件・Storage3件・旧版の計算/移行/保存/画面/ビルド/性能・WinUI製品/検証用ビルドと既存画面が成功。Docker/NAS/配布アーカイブ検証も成功し、今回はCI判定の変更に伴い全対象を実行した。GHCRへの実push・GitHub Releasesの実公開/匿名pull・ARM64ネイティブ実行はタグ配布後の確認が必要。実NAS・tailnet/Serve・実Windowsログイン・実バックアップ復元は未検証。

DEC-136の支払方法画面はソース`8c35292d8fa10cfb3f9d05b08f05272fadd15e6b`の[CI 42032155217](https://github.com/k-o-a-b-c/monetail/actions/runs/42032155217)でWinUI製品/検証用Releaseビルドと既存・新規画面1シナリオが成功。人物別グループ/本人優先/本日両端/全期間切替/空見出し除外、Admin/Editor/未対応Editor/Viewer、ダイアログ入力/作成再送/削除/行競合、全件並び替え/未知の追加行を含む409/取消/メニュー移動、認証復帰/降格/別アカウント非引継ぎ、Enter移動/900pxリサイズを合成HTTPで確認した。並び替え409への対象人物全行の追加は承認済みのAPI変更で、Clientに不整合応答の拒否3件を追加。API全641件・Client全323件・Storage3件・Node53件・Dockerビルド/既存/NAS起動は[CI 42029128688](https://github.com/k-o-a-b-c/monetail/actions/runs/42029128688)の成功ジョブを再利用（この実行全体は下記WinUI異常で失敗）。ローカルClient323件・API並び替え39件・差分検査も成功。構成合計1206件を全件再実行した意味ではない。DB/Migration・外部ライブラリ変更なし。CIのDPIは100%。実API接続・実Windows手動操作・実IME/物理キー・125/150/200%やモニター間DPI移動は未検証。

**既存ネイティブ異常の再発：** [CI 42029128688](https://github.com/k-o-a-b-c/monetail/actions/runs/42029128688)と[CI 42030851084](https://github.com/k-o-a-b-c/monetail/actions/runs/42030851084)で、PR #118と同じ人物再ログイン確認のAutomation Invoke中に0xC0000005が再発。後者の`ui-invoke.txt`では直前の保存ボタンがLoaded=False/Root=Trueだった。人物ダイアログのLoaded/操作可能状態を待機し、表示済みボタンを優先して取得するよう検証基盤を補強した。テスト項目・判定・UI Automation操作は維持し、診断ログも保存する。最終CIでは再発しなかったが、ネイティブ層の根本原因・再発可能性まで解決したとは扱わない。

支払方法Clientは新規67件で5操作のHTTP契約、作成下書きの正規化/requestId維持、全操作の認証更新、部分更新の省略/null、全期間と空所有者の版・同順位/欠番の保持、3形式の409最新情報、権限表示/最新権限/回数制限、入力・不正応答/競合対象の拒否、別アカウント検知/認証失効/取消を確認する。ローカルReleaseでClient全320件・差分検査が成功。API/DB/Migration/WinUI画面・外部ライブラリは変更していない。ソース`876bd41c3819fa66d9aa08b4ba539c74de23b67f`の[CI 42001480827](https://github.com/k-o-a-b-c/monetail/actions/runs/42001480827)でClient320件・Storage3件・WinUI製品/検証用ビルドと既存画面1シナリオが成功。初回の未変更の人物再ログイン画面確認でボタンInvoke時にネイティブアクセス違反0xC0000005が発生し、失敗ジョブだけを同一ソースで再実行して成功した（原因未特定、コード/テストの緩和なし）。未変更のAPI等は成功結果を再利用し、構成合計1203件を全件再実行した意味ではない。実Windowsの支払方法画面・実API接続は未検証（今回は合成HTTP）。

DEC-132支払方法並び替えAPIに39ケースを追加。権限/本人対応/認証境界、他家計簿/所有者/配列/UUID検証、有効期間外を含む全件再採番、所有者間の独立性、0件/1件/同順再保存の新版、対象人物のみの409最新順、同時保存、DB条件付き版更新、2行目の保存失敗・降格/対応変更/除外/認証失効の全体ロールバックを検証する。ソース`aa2209c8e533bf6bbd48bb7210d7442dade7e81f`の[CI 41978635513](https://github.com/k-o-a-b-c/monetail/actions/runs/41978635513)でAPI全641件・Node53件・Dockerビルド・既存/NAS起動確認が成功。ローカルReleaseの追加39件・Node53件・差分検査も成功。未変更のWindows系・旧版は既存成功結果を再利用し、構成合計1136件を全件再実行した意味ではない。DB/Migration・外部ライブラリ・WinUIの変更なし。実運用DBへの適用は未検証。

PR #116レビュー修正は、所有者の人物削除後の作成再送409・異内容再送409・新規要求の所有者必須400、現在のViewer/人物未対応Editor/除外済みの再送拒否とDB不変を確認する4ケース、APIの所有者UUID順・人物共通順・本人位置が異なる一覧を本人対応あり/なしで確認する2ケースを追加。ローカルReleaseでAPI全602件・Node53件・差分検査が成功。GET配列順は変更せず、表示側で本人優先・人物共通順にする契約を正本へ明記した。DB/Migration・画面の変更はない。関連CIはpush後に確認する。

DEC-135支払方法CRUDはソース`11762d6ce7039e183dd919234d0bd7a0256f1700`の[CI 41964968258](https://github.com/k-o-a-b-c/monetail/actions/runs/41964968258)でAPI596件・Node53件、Dockerビルドと既存/NASスモークが成功。新規API44ケースで全期間/空所有者の一覧、Admin/Editor/Viewerと現在の人物対応、名前/UTF-16/日付/部分更新、他家計簿、同時再送、旧版での並行編集/削除、DB条件付き更新の拒否、保存直後の権限/対応/認証変化と全体ロールバックを確認。参照拒否はテスト専用複合RESTRICT FKで409と順序版維持を検証し、本番の業務表は作成していない。ローカル新規44件・最終重点3件（重複あり）とReleaseビルド/差分検査も成功。PR #115はマージ済みで最新main79ba6dcを基点とし、DB/Migration/外部ライブラリ変更なし。未変更対象は既存成功を再利用し、構成合計1091件を全件再実行した意味ではない。将来の取引表追加時には参照FKと期間検証の接続が必要。WinUI・並び替えPUT・旧版データ移行・ポイントは対象外。実運用DBへの配備は未検証。

PR #115の支払方法Migrationテストは、既存人物の家計簿ID/人物ID・名前・有効期間・表示順・人物編集revisionを旧列のまま比較し、人物以外の既存表（認証情報・パスキーを含む）も比較する。支払方法の有無2ケースで実IMigratorによるUp→Down→Up、旧DDL/データの保持、再Upの全DDL一致・FK/UNIQUE/CHECK/所有者固定・UUID v4の個別再生成・再初期化での版維持・モデル一致を確認した。ローカルReleaseの関連移行25件、最終の往復2件と差分検査が成功。追加の往復2ケースにより、PRのAPI構成は既存3ケースを含め552件、全体1047件（全件実行の結果ではない）。本番コードは変更していない。

**Downのデータ消失リスク：** `AddLedgerPaymentMethods.Down`は支払方法表を削除するため、データがあっても全支払方法が失われる。再Upしても復元されず、人物の支払方法順序版も新UUIDになる。既存人物の旧列・認証データは保持されるが、この往復はバックアップ復元の代わりにならない。実データがあるDBでDownする前には、支払方法データを含むバックアップと復元手順が必要。これは現在のDownの挙動を検証・記録するもので、本番DBで実行したものではない。

PR #111の招待エラー文言を正本どおりに修正し、既存4ケースで表示全文の一致を検証した。ローカルNode53件・差分検査が成功。件数の増加やAPI/DB変更はない。

PR #111レビュー修正はソース`bf7d5e44de1902808a0f070803e61522e74d22a0`の[CI 41806291878](https://github.com/k-o-a-b-c/monetail/actions/runs/41806291878)でAPI547件・Node53件、Dockerビルドと既存/NASスモークが成功。追加8ケースでログイン状態別の暫定戻る文言/遷移、一時情報削除、終端エラーの操作非表示、通常通信失敗の導線維持、別アカウント→明示ログアウト/再ログインの間の記録保持・元の本人だけの明示GET再開を確認した。既存OAuthの回帰も成功。ローカルNode53件・差分検査も成功。業務API/DB/外部ライブラリは変更せず、未変更対象は以前の成功を再利用。構成合計1042件を一度に再実行した意味ではない。実ブラウザー・実パスキー端末・実NASは未検証。

DEC-117のWeb家計簿招待受領はソース`f6647b8a7690b404e413bf5ca778453c966b1d15`の[CI 41799288188](https://github.com/k-o-a-b-c/monetail/actions/runs/41799288188)でAPI547件・Node45件、Dockerビルドと既存/NASスモークが成功。追加HTTP2件・JS17件で匿名ページのHTTPS/CSP/no-store、明示参加・参加済み/利用不可、ログイン復帰とOAuth維持、応答喪失・再読み込み・404未確認・同じ本人の明示GET・別アカウント拒否・保存不能・トークン削除を確認した。ローカルもHTTP2件・Node45件が成功。業務API/DBの契約は変更せず未変更対象は既存成功を再利用。構成合計1034件を同時再実行した意味ではない。実ブラウザーのsessionStorage/履歴・Cookie・パスキー端末・実NASは未検証。Webレシート一覧とWindows招待受信は対象外。

DEC-116のWinUI 3参加者・家計簿招待管理はソース`81b33682d0a615648c65691de4c28e8280095d81`の[CI 41785660852（attempt 2）](https://github.com/k-o-a-b-c/monetail/actions/runs/41785660852/attempts/2)で成功。製品/スモークのReleaseビルドは警告・エラー0、製品起動と実コントロールの1シナリオ174確認が成功（合成HTTP・データ、RasterizationScale=1、撮影なし）。API順の維持・内部ID非表示、本人の役割/除外禁止と人物対応保存、候補の無効化、競合の入力/版保持と明示切替、除外確認、403後の入場制限、招待の新しい発行順・実クリップボードコピー・取消・応答喪失時の同一要求ID、成功応答一覧の利用、同じ本人の認証復帰と別アカウントへの非引継ぎを確認した。Client253件・DPAPI3件は[CI 41784414918](https://github.com/k-o-a-b-c/monetail/actions/runs/41784414918)の各成功ジョブを再利用し、以後Clientソースは不変。ローカルは新規Client27件、Releaseビルド、文書リンク/差分検査が成功。初期のPopup配置とスモークの仮想化行待ちを修正した。最終ソースのattempt 1では既存の人物再認証のAutomationPeer.Invoke中に単発のネイティブクラッシュ（0xC0000005）があり、WinUIジョブだけの同一ソース再実行は成功、原因は未特定。API/DBは変更せず、未変更対象も既存成功を再利用するため、構成合計1015件を同時に再実行した意味ではない。Windows 11実機の高DPI/モニター間移動、物理キー/IME、実サーバーの認証・招待運用は未検証。ブラウザーの家計簿参加画面は未実装。

DEC-115の全体Admin向けWebアカウント管理はソース`c785125d661b87f2ba7ccae300cb7b928bdeb6c1`の[CI 41769793422](https://github.com/k-o-a-b-c/monetail/actions/runs/41769793422)でAPI545件・Node28件・判定42件、Dockerビルドと既存/NASスモークが成功。追加35 APIケースと13 JSケースでHTML/JS/APIの権限、登録方式2種類・状態4種類、履歴/CLI復旧との分離、メモの上限/空欄/非公開/重複、移行による全既存表の保持・モデル一致、保存中の権限失効と原子性、5分境界と画面内再認証、要求ID再送・五十音/欠損/ID順・ダイアログ/URL破棄を確認した。既存の初期Admin・招待/緊急復旧・通常ログイン・Windows OAuthも回帰成功。初回CIは新設メモ表を含む既存スナップショット期待値1件が失敗し、更新後に成功した。ローカルは重点62件・モデル一致1件・保存/登録34件（対象の重複あり）とNode28件、文書リンク・差分検査が成功。未変更のWindows等は既存CI成功を再利用し、当時の構成合計988件を全件再実行した意味ではない。実ブラウザー・実パスキーデバイス・実NASは未検証。全体ロール変更・停止/削除・Web復旧は未実装。

PR #105の追加修正はソース`30f223183a2f7eb8aebbb4247284087633dc5942`の[CI 41747788416](https://github.com/k-o-a-b-c/monetail/actions/runs/41747788416)でAPI510件・Node15件・判定42件、Dockerビルドと既存/NASスモークが成功。追加8ケースで通常/初期管理者招待だけの取消、CLI復旧リンクと未知用途の不変、取消後の復旧WebAuthn成功、未登録の一般/初期管理者と利用中/鍵残存へのCLI復旧発行拒否・DB不変を確認した。ローカルは対象44件＋追加2件が成功。既存の緊急復旧・再登録・ログイン・Windows OAuthもAPI回帰に含む。未変更対象は以前のCI成功を再利用し、当時の構成合計940件を全件再実行した意味ではない。DBスキーマの追加変更はなく、実端末/実NASは未検証。

DEC-050・113の招待リンク/登録画面と利用者確認済みのCLI緊急復旧（DEC-114）は、ソース`429010cb0646a6f1b6a110c3b23860ee599ed5d3`の[CI 41744295834](https://github.com/k-o-a-b-c/monetail/actions/runs/41744295834)でAPI502件・Node15件・判定42件、Dockerビルドと既存/NASスモークが成功。復旧ではNULLを含む表示名と認証ID・ロールを保ち、自動ログインしない。初回ソース`dda6882`の[CI 41742917219](https://github.com/k-o-a-b-c/monetail/actions/runs/41742917219)は共通CIへNode検証を追加したため当時の全構成930件と旧版ビルド/性能・Compose・WinUIも成功し、後続では変更のない対象の成功を再利用した。同時点の932件は追加2ケース込みの構成合計で、最終回に全件を再実行した意味ではない。ローカルでも既存API484件・対象37件・最終の保持/取消3件とNode15件、actionlint・JS構文・文書リンク・差分検査が成功。実ブラウザー・実パスキーデバイス・実NAS配備は未検証。Web家計簿画面への遷移、CAPTCHA、退会、本人向けの自己復旧は未実装。

DEC-112自己登録はソース`e64a7164acccccc1a791eddd4ea77315530f76b1`の[CI 41731192843](https://github.com/k-o-a-b-c/monetail/actions/runs/41731192843)でAPI484件・判定42件が成功した。新規38ケースに加え、既存の招待登録・管理CLI・ログイン・Windows OAuth・家計簿APIを回帰確認した。製品APIは先行ソース`46ad9b6`と同一で、Dockerビルド・既存/NASスモークは[CI 41730305252](https://github.com/k-o-a-b-c/monetail/actions/runs/41730305252)の成功を再利用した。ローカルは新規37件、全API483件、その後追加したBearer境界と既存テストの補強を含む重点15件が成功。APIのReleaseビルド、文書リンクと差分検査も成功した。旧版・Client・Storage・WinUI等は変更がなく再実行していない。899件は構成合計であり今回の実行件数とは区別する。実ブラウザー・実パスキーデバイス・実配備は未検証。登録画面・CAPTCHA・退会・全パスキー喪失時の自己復旧は未実装のまま。

Docker NAS配備はソース`3f5bd7d249d70580e5d2de7464b3dcbe7c364a81`の[CI 41643374059](https://github.com/k-o-a-b-c/monetail/actions/runs/41643374059)で、構成848件、Compose設定、APIのDockerビルドと既存/NASスモーク、旧版ビルド・性能計測が成功した。今回は共通CIと判定処理の変更に伴い全対象を実行した。ローカルでもAPI433件・判定42件・Compose・actionlint・Bash構文と両Dockerスモークが成功。ローカルのDockerビルドには作業環境の公開プロキシCAをBuildKit secretでビルド段階だけへ渡したが、配布用Dockerfileは変更せず、CIではそのまま成功した。Tailscale公式イメージv1.104.4の取得とCLIも確認した。実NAS、実tailnet/Serveの証明書取得更新、実パスキー/Windows接続、実バックアップ復元は未検証。[NAS手順](../docs/deployment-docker-nas.md)の実機確認を行う。

人物の共通順設定と認証後の復元はソース`de2e0aebec793c5d8f5d60e90c69295bedf217e2`を[CI 41605779743](https://github.com/k-o-a-b-c/monetail/actions/runs/41605779743)で確認。判定41件とWinUIスモーク1シナリオが成功し、製品/スモークのReleaseビルドは警告・エラー0。共通順と通常の本人先頭表示、移動と取消の通信なし、全IDの一括保存、競合時の最新一覧への直接置換、通信失敗/応答喪失、権限変更を検証した。さらに同じ本人での再ログイン後の未保存順・元の全体版の維持と非自動保存、認証待ち中の人物増減、降格/除外後の保存禁止、未参加時のGET抑止、別アカウント/接続先への非引き継ぎも成功。API・Client・Storage・旧版等は変更がなく再実行していない。構成合計847件は変わらず、スモーク内の確認項目を個別のテスト件数に加算しない。実キー・IME・実端末DPI・実ブラウザーのパスキーは未検証。

### 過去の確認（索引）

以下は**当時の個別検証**であり、現在のテスト件数や全機能の成功を意味しない。コミットの完全なSHA、再実行の範囲、失敗・未検証事項を含む原文は[過去のCI検証記録](ci-verification-records.md)に保持する。

| 対象・詳細 | 当時のソース | CI | 特記事項 |
| --- | --- | --- | --- |
| [人物の共通順（初期操作）](ci-verification-records.md#ci-41598552778) | `04c962a1b3` | [41598552778](https://github.com/k-o-a-b-c/monetail/actions/runs/41598552778) | 当時は並び順保存中の認証失効後復帰が未実装（後続で実装） |
| [人物削除のWindows画面](ci-verification-records.md#ci-41593840818) | `1e320297c3` | [41593840818](https://github.com/k-o-a-b-c/monetail/actions/runs/41593840818) | 人物削除・競合・権限変化のスモーク確認 |
| [NuGetキャッシュと復元](ci-verification-records.md#ci-41586922999) | `c44a148d49` | [41586922999](https://github.com/k-o-a-b-c/monetail/actions/runs/41586922999) | 復元ヒット・再テスト抑制の実測、時間短縮率は保証しない |
| [WinUI構成整理前・画面撮影](ci-verification-records.md#ci-41418236183) | `c44a148d49` | [41418236183](https://github.com/k-o-a-b-c/monetail/actions/runs/41418236183)・[41418289696](https://github.com/k-o-a-b-c/monetail/actions/runs/41418289696) | 撮影は96DPI・合成データ |
| [WinUI構成整理・全体検証](ci-verification-records.md#ci-41424095869) | `8592ed02b4` | [41424095869](https://github.com/k-o-a-b-c/monetail/actions/runs/41424095869)・[41424095689](https://github.com/k-o-a-b-c/monetail/actions/runs/41424095689) | 当時の構成815件 |
| [ホーム上部1行化](ci-verification-records.md#ci-41433846188) | `3b61079676` | [41433846188](https://github.com/k-o-a-b-c/monetail/actions/runs/41433846188)・[41433992670](https://github.com/k-o-a-b-c/monetail/actions/runs/41433992670) | 1行ヘッダーと単発撮影 |
| [ホームの表示・ボタン調整](ci-verification-records.md#ci-41513466165) | `21d3925fb4` | [41513466165](https://github.com/k-o-a-b-c/monetail/actions/runs/41513466165)・[41513601351](https://github.com/k-o-a-b-c/monetail/actions/runs/41513601351) | ライト／ダークと登録表記 |
| [接続・認証・家計簿操作画面](ci-verification-records.md#ci-41519000314) | `b11ecc5b81` | [41519000314](https://github.com/k-o-a-b-c/monetail/actions/runs/41519000314) | 当時は製品起動からの自動復元が未接続 |
| [起動・ログイン復元・待機表示](ci-verification-records.md#ci-41521479704) | `1386b5e460` | [41521479704](https://github.com/k-o-a-b-c/monetail/actions/runs/41521479704)・[41522184140](https://github.com/k-o-a-b-c/monetail/actions/runs/41522184140) | 製品起動、待機表示、タイムアウト、撮影 |
| [人物追加・編集・入力・認証復帰](ci-verification-records.md#ci-41532331268) | `41193cdd94` | [41532331268](https://github.com/k-o-a-b-c/monetail/actions/runs/41532331268)・[41532322990](https://github.com/k-o-a-b-c/monetail/actions/runs/41532322990) | 当時の構成826件、更新後一覧と入力復元 |
| [人物画面の単発撮影](ci-verification-records.md#ci-41532325889) | `41193cdd94` | [41532325889](https://github.com/k-o-a-b-c/monetail/actions/runs/41532325889) | 96DPI画像で表示領域を確認 |
| [人物追加・編集のContentDialog化](ci-verification-records.md#ci-41541869671) | `eeea52d32a` | [41541869671](https://github.com/k-o-a-b-c/monetail/actions/runs/41541869671)・[41541869672](https://github.com/k-o-a-b-c/monetail/actions/runs/41541869672) | ContentDialogの操作・保存・復帰 |
| [ContentDialogの単発撮影](ci-verification-records.md#ci-41541866024) | `eeea52d32a` | [41541866024](https://github.com/k-o-a-b-c/monetail/actions/runs/41541866024) | RenderTargetBitmap撮影（背後は画像外） |
| [CIの選択・成功記録再利用](ci-verification-records.md#ci-41581065430) | `8b82a4ae61` | [41581065430](https://github.com/k-o-a-b-c/monetail/actions/runs/41581065430) | 構成847件をCIで確認 |
| [CIの手動撮影](ci-verification-records.md#ci-41581145112) | `8b82a4ae61` | [41581145112](https://github.com/k-o-a-b-c/monetail/actions/runs/41581145112) | 撮影専用で成功記録を保存しない |
| [成功記録11種類の照合](ci-verification-records.md#ci-41582333257) | `a8b256e852` | [41582333257](https://github.com/k-o-a-b-c/monetail/actions/runs/41582333257) | 判定41件のみ再実行、他は成功記録を再利用 |

## 実行方法（リポジトリ直下）

ブラウザーのスクリプト検証はNode.jsで`node --test tests/Monetail.Api.Tests/registration-ui.test.js`を実行する。DOM・WebAuthn・通信だけを合成し、製品のスクリプト本体を実行する。CIではAPIテストと同じ検証入力に含め、Node検証が失敗した場合はAPIの成功記録を保存しない。

```sh
# .NET 10 / LinuxまたはWindows
dotnet test tests/Monetail.Api.Tests/Monetail.Api.Tests.csproj --configuration Release
dotnet test tests/Monetail.Windows.Client.Tests/Monetail.Windows.Client.Tests.csproj --configuration Release
# .NET 10 / Windows
dotnet test tests/Monetail.Windows.Storage.Tests/Monetail.Windows.Storage.Tests.csproj --configuration Release
dotnet build src/Monetail.WinUI/Monetail.WinUI.csproj --configuration Release --runtime win-x64 -p:Platform=x64
# 旧版の.NET 8検証
dotnet run --project tests/CalculationTests/CalculationTests.vbproj
python3 -m unittest discover -s tests -p 'test_*migration.py' -v
# CI選択
python3 -m unittest discover -s .github/scripts -p 'test_*.py' -v
```

WinUI 3の製品起動と画面スモークは[専用ワークフロー](../.github/workflows/winui.yml)と[run.ps1](Monetail.WinUI.Smoke/run.ps1)で確認する。通常CIでは画像を取らない。撮影はActionsの「WinUI 3 foundation」を手動実行し、対象ブランチと`capture=true`を指定する。撮影専用ブランチへのpushは不要。

旧Windowsでは`dotnet run --project tests/PersistenceTests/PersistenceTests.vbproj`で画面・実SQLiteを確認する。性能計測は同コマンドに`--measure-receipt-reads CSVのパス`を渡し、通常テストと区別する（[測定条件](../docs/34-receipt-read-performance.md)）。APIのDocker確認は[起動・運用](../docs/42-api-foundation.md)を参照する。

NAS構成の確認はLinux上のDocker・Bash・OpenSSL・Python 3・curlを使い、APIイメージをビルドしてから`bash tests/Monetail.Api.Tests/nas-smoke.sh monetail-api:ci`を実行する。隔離したDB・秘密ファイルとComposeプロジェクトは終了時に削除し、実Tailscaleサービスは起動しない。

## 必要なCIだけを実行する

自動起動はPRの作成・更新と`main`へのpushだけとする。作業ブランチのpushとPRで同じ検証を二重起動しない。WinUI 3の通常検証も[Tests](../.github/workflows/tests.yml)から[再利用ワークフロー](../.github/workflows/winui.yml)を呼び出し、独立したpush/PR起動は行わない。PR更新時は同じPRの古い実行を中止する。

[select_tests.py](../.github/scripts/select_tests.py)が変更と参照プロジェクトから検証対象を選び、[ci_plan.py](../.github/scripts/ci_plan.py)が同じ入力の成功記録を確認する。PRは分岐点以降の全差分を対象にし、最後のコミットが文書だけでも、PR内の未検証・失敗した変更を取り落とさない。改名・削除も扱い、未分類の変更は失敗させる。

| 変更 | 確認対象（同じ入力の成功記録があれば再利用） |
| --- | --- |
| 文書だけ | 軽量なCI判定。PR内に未検証・失敗した変更が残る場合は、その検証を実行 |
| APIソース／テスト | API、製品ソースではDockerも確認 |
| Clientソース | Client・Windowsの実DPAPI・WinUI 3 |
| Clientテストだけ | Client |
| Storageテストだけ | Windowsの実DPAPI |
| WinUI 3／スモーク | WinUI 3のビルド・製品起動・画面スモーク |
| Dockerfile等／Compose／NAS配備 | Dockerfile等はDocker、Compose・NAS配備はDocker＋Compose |
| 旧Windows・計算・SQL等 | 依存するテスト、ビルド、必要なら性能計測 |
| GHCR専用の`registry_image.py`・`test_registry_image.py` | 常時実行するPython検証のみ。製品テストの指紋にも含めない |
| 共通CI・判定処理・SDK／ビルド設定 | 全対象。WinUI専用ワークフローの変更はWinUIのみ |

GHCRの例外は`.github/scripts/registry_image.py`・`.github/scripts/test_registry_image.py`の完全一致だけに限定する。他の新しいスクリプト、選択・成功記録の共通処理やそのテストは従来どおり全対象へ影響する。Python検証は毎回実行するため、この2ファイルを製品の指紋から除いても検証を省略しない。`main`ではGHCRだけのpushでも全入力の成功記録を照合し、未検証の製品変更や記録欠落があれば必要な検証を実行する。

再利用には、各検証に影響するファイルのパス・Gitオブジェクト・実行属性から求めた指紋を使う。製品だけでなくテスト、参照先、CI定義、共通ビルド設定を含め、チェックアウトしたPRのマージ後ツリーで照合する。入力が変われば別の記録となる。既存の未分類ファイルは指紋から黙って落とさず、保守的に全検証へ含める。

[成功記録用アクション](../.github/actions/ci-proof/action.yml)は、各検証が成功した後にだけ、指紋と元のCIへのリンクをActions cacheへ保存する。中断・失敗を成功として保存しない。復元は完全一致だけとし、記録なし・不一致・破損・キャッシュ障害は再実行する。Windowsで保存した記録もLinuxの判定ジョブで照合する。キャッシュは最適化であり、GitHub側で消去されたら検証し直す。再利用の有無と元の成功CIは実行概要へ表示し、再実行していないものを今回の検証件数へ加算しない。

`main`では現在の全対象の成功記録を確認し、以前のpushが未検証でも次のpushで取り落とさない。PR専用キャッシュを`main`の成功記録として流用せず、GitHubのキャッシュ公開範囲に従う。main取り込み後の確認はPRとの二重起動とは区別する。

NuGetパッケージは[共通アクション](../.github/actions/nuget-cache/action.yml)でキャッシュする。OS・アーキテクチャ・ジョブ・実SDK版・プロジェクト/依存設定でキーを分け、並列ジョブが部分的なパッケージ群を同じキーへ保存しないようにする。旧版の`packages.config`用フォルダーも含める。依存設定が変わった場合は既存パッケージを種として復元できるが、NuGetの依存解決とテスト/ビルドは省略しない。成功結果の再利用とは別の仕組みで、キャッシュ障害時も通常の復元へ戻る。

撮影は必要時の手動実行だけで行い、撮影結果を通常検証の成功記録として登録しない。全検証をやり直したい場合はActionsの「Tests」を`force=true`で手動実行する。成功済みの入力でも再実行できる。

## 新版Windowsの手動確認（未実施）

製品起動から接続・ログイン復元・家計簿の行き先へ進む処理を画面へ接続した。人物追加・編集・削除確認・日付入力と共通Enter操作は接続済み。共通順の設定と同一アカウントでの認証後復元も接続済み。時刻入力のUIは未接続。実装後は[43](../docs/43-passkey-authentication-design.md)・[58](../docs/58-ledger-foundation-design.md)・[59](../docs/59-input-controls-design.md)の契約に沿って確認する。ARM64、100／125／150／200%の物理端末、モニター間移動・ハイコントラスト・日本語IME・実キー入力、実ブラウザーのパスキー、本番配備・実DB移行は未検証。

## 旧Windowsで画面を手動確認する手順（利用者環境では未実施）

手順は[旧Windows版の手動確認チェックリスト](legacy-manual-checks.md)を参照する。**利用者環境での実施は未確認**。必ずDBのバックアップ・テスト用コピーを使い、旧アプリと新アプリで同じDBを同時利用しない。
