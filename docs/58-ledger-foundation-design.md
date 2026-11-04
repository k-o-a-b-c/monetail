# 58 家計簿のDB・APIと後続設計

このファイルは家計簿仕様の**入口と旧リンクの互換案内**です。仕様本文は担当領域ごとの正本に分割しています。Codexはタスクに関係する文書だけを読み、全ファイルを一括読込しないでください。

## 分野別の正本

| 内容 | 参照先 |
| --- | --- |
| 共通前提・DB・API・移行の分野別案内 | [ledger-foundation-core.md](./ledger-foundation-core.md) |
| 人物の仕様・一覧・追加／編集・削除・並び順 | [ledger-persons.md](./ledger-persons.md) |
| 参加者・権限・アカウントと人物の対応 | [ledger-participants.md](./ledger-participants.md) |
| 人物管理API・DB・作成要求の再送・整合性 | [ledger-person-api.md](./ledger-person-api.md) |
| 新規利用・登録／ログイン画面・家計簿ホーム | [ledger-onboarding.md](./ledger-onboarding.md) |
| 家計簿の招待・参加確認・招待エラー | [ledger-invitations.md](./ledger-invitations.md) |
| Windows版の接続・ログイン・起動時の遷移 | [ledger-windows.md](./ledger-windows.md) |

現行の進捗・決定状況はリンク先の記述に従います。仕様を変更するときは担当する正本1か所を更新し、履歴や他分野へ同じ契約を複製しません。

## 過去の見出しリンク

既存の文書・決定履歴からの `58-ledger-foundation-design.md#...` を維持するための案内です。新しい参照は上表の正本へ直接リンクしてください。

<a id="58-家計簿のdbapiと後続設計"></a> [58 家計簿のDB・APIと後続設計](./ledger-foundation-core.md#58-家計簿のdbapiと後続設計)

<a id="確定済みの前提"></a> [確定済みの前提](./ledger-core-principles.md#確定済みの前提)

<a id="確定した仕様dec-046048"></a> [確定した仕様（DEC-046・048）](./ledger-core-principles.md#確定した仕様dec-046048)

<a id="人物の管理dec-055apiとwindows画面実装"></a> [人物の管理（DEC-055・APIとWindows画面実装）](./ledger-persons.md#人物の管理dec-055apiとwindows画面実装)

<a id="人物管理画面同じウィンドウ内で切替"></a> [人物管理画面（同じウィンドウ内で切替）](./ledger-persons.md#人物管理画面同じウィンドウ内で切替)

<a id="人物とアカウントの対応付けapi実装画面未実装"></a> [人物とアカウントの対応付け](./ledger-participants.md#人物とアカウントの対応付けapi実装画面未実装)

<a id="人物管理apidb実装済み"></a> [人物管理API・DB（実装済み）](./ledger-person-api.md#人物管理apidb実装済み)

<a id="新版の業務モデルdb設計方針未実装"></a> [新版の業務モデル・DB設計方針（未実装）](./ledger-core-schema.md#新版の業務モデルdb設計方針未実装)

<a id="保存構成apiの初回定義へ実装済み"></a> [保存構成（APIの初回定義へ実装済み）](./ledger-core-schema.md#保存構成apiの初回定義へ実装済み)

<a id="家計簿作成と再送"></a> [家計簿作成と再送](./ledger-core-api.md#家計簿作成と再送)

<a id="作成参照apiと後続案"></a> [作成・参照APIと後続案](./ledger-core-api.md#作成参照apiと後続案)

<a id="アカウント単位の回数制限dec-049"></a> [アカウント単位の回数制限（DEC-049）](./ledger-core-api.md#アカウント単位の回数制限dec-049)

<a id="新規利用の方針dec-037046"></a> [新規利用の方針（DEC-037〜046）](./ledger-onboarding.md#新規利用の方針dec-037046)

<a id="登録ログインの3画面dec-050未実装"></a> [登録・ログインの3画面（DEC-050・未実装）](./ledger-onboarding.md#登録ログインの3画面dec-050未実装)

<a id="所属0件の家計簿作成画面"></a> [所属0件の家計簿作成画面](./ledger-onboarding.md#所属0件の家計簿作成画面)

<a id="所属1件以上で開く家計簿"></a> [所属1件以上で開く家計簿](./ledger-onboarding.md#所属1件以上で開く家計簿)

<a id="家計簿のホーム切替追加"></a> [家計簿のホーム・切替・追加](./ledger-onboarding.md#家計簿のホーム切替追加)

<a id="家計簿への招待の発行一覧取消api実装画面未実装"></a> [家計簿への招待の発行・一覧・取消](./ledger-invitations.md#家計簿への招待の発行一覧取消api実装画面未実装)

<a id="家計簿の参加確認結果未実装"></a> [家計簿の参加確認・結果（未実装）](./ledger-invitations.md#家計簿の参加確認結果未実装)

<a id="招待リンクの共通エラー画面未実装"></a> [招待リンクの共通エラー画面（未実装）](./ledger-invitations.md#招待リンクの共通エラー画面未実装)

<a id="アカウント操作の入口未実装"></a> [アカウント操作の入口（未実装）](./ledger-onboarding.md#アカウント操作の入口未実装)

<a id="新規利用と既存dbの境界"></a> [新規利用と既存DBの境界](./ledger-core-transition.md#新規利用と既存dbの境界)

<a id="windows版のapi対応を先行する実装中"></a> [Windows版のAPI対応を先行する（実装中）](./ledger-windows.md#windows版のapi対応を先行する実装中)

<a id="windows版の接続先設定"></a> [Windows版の接続先設定](./ledger-windows.md#windows版の接続先設定)

<a id="windows版の起動表示接続失敗時の操作"></a> [Windows版の起動表示・接続失敗時の操作](./ledger-windows.md#windows版の起動表示接続失敗時の操作)

<a id="windows版のログイン認証待ちブラウザー完了表示"></a> [Windows版のログイン・認証待ち・ブラウザー完了表示](./ledger-windows.md#windows版のログイン認証待ちブラウザー完了表示)

<a id="次の実装と確認"></a> [次の実装と確認](./ledger-core-transition.md#次の実装と確認)

<a id="後続で確定すること"></a> [後続で確定すること](./ledger-core-transition.md#後続で確定すること)

<a id="人物の管理dec-055未実装"></a> [人物の管理（DEC-055・APIとWindows画面実装）](./ledger-persons.md#人物の管理dec-055未実装)

<a id="人物とアカウントの対応付け一部確認済み未実装"></a> [人物とアカウントの対応付け](./ledger-participants.md#人物とアカウントの対応付け一部確認済み未実装)

<a id="人物管理apidbの具体案一部確認済み未実装"></a> [人物管理API・DB（実装済み）](./ledger-person-api.md#人物管理apidbの具体案一部確認済み未実装)

<a id="家計簿への招待の発行一覧取消未実装"></a> [家計簿への招待の発行・一覧・取消](./ledger-invitations.md#家計簿への招待の発行一覧取消未実装)

