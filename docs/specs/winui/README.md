# 新WinUI 3 機能別仕様の入口（移行台帳）

対象は **Windows 11 / WinUI 3 の画面とWindowsクライアントの操作**。既存の仕様を機能単位で読みやすくするための移行台帳であり、**このファイル自体は機能仕様の正本ではない**。

**現在は移行準備段階**。下表の機能別新仕様書はまだ作成しておらず、既存文書が引き続き正本である。「未移行」は文書の状態であり、機能が未実装という意味ではない。実装状態と画面IDは[画面台帳](../../screen-inventory.md)で確認する。

## 分割の原則

- **機能別を基本単位**とする。画面一覧・入力・保存など、同じ機能に属する操作をまとめる。
- 機能内の独立した画面・操作フロー（例：レシート一覧、レシート編集）は、十分に大きい場合だけ別ファイルに分ける。単純な機能を画面・ロジックごとに機械的に細分化しない。
- **画面仕様**には表示、初期状態、フォーカス、操作、遷移、読み込み・空・エラー状態を書く。**Windows側の振る舞い**には入力補完、編集状態、保存結果の扱い、競合・認証復帰などを書く。ただし重複する業務規則は一か所だけに置く。
- API・DB・認証・計算などWinUI固有でない契約は既存正本をリンクするだけにし、WinUI側へ複写しない。実装クラス（Page / ViewModel / Client）の配置を機能仕様で固定しない。
- 共通操作・見た目・キー操作は共通仕様へ寄せ、機能別には例外や追加条件だけを書く。
- 既存の確定事項は変更しない。「確定済み」「提案」「未決定」を区別し、旧Windows版の挙動を無断で新WinUIの確定事項にしない。

## 配置（移行時に必要なファイルだけ追加）

```text
docs/specs/winui/
  README.md                  # この移行台帳
  common/                    # 共通仕様の移行先（後続）
    ui.md                    # 共通の見た目・アクセシビリティ
    input.md                 # 日付・時刻・Enter・IME
    navigation.md            # 画面の共通遷移・シェル
  features/
    <feature>/
      README.md              # 機能の入口。小規模機能は本文もここ
      list.md                # 必要なら一覧画面
      editor.md              # 必要なら登録・編集画面
      behavior.md            # 必要ならWindows固有の操作ロジック
```

上記は**配置方針**であり、存在しないファイルへの参照を追加する指示ではない。個々の機能は最小のファイル数から始め、内容が独立して実装・検証できる場合だけ分割する。

## 機能別の移行一覧

| 新しい機能の単位 | 対応画面（[画面台帳](../../screen-inventory.md)） | 移行前に照合する主な既存正本 | 文書移行 |
| --- | --- | --- | --- |
| `connection` 接続先・接続失敗 | A01〜A03、M14 | [Windows接続](../../ledger-windows.md)、[Windows認証](../../auth-windows-oauth.md) | 未移行 |
| `authentication` ログイン・ログイン情報 | A04〜A05、M15 | [Windows接続](../../ledger-windows.md)、[Windows認証](../../auth-windows-oauth.md)、[新規利用](../../ledger-onboarding.md) | 未移行 |
| `ledger` 家計簿選択・作成・切替 | A06〜A08 | [新規利用](../../ledger-onboarding.md)、[家計簿API](../../ledger-core-api.md) | 未移行 |
| `record-home` 記録ホーム・共通フィルター | R01 | [振替WinUI](../../ledger-transfer-ui.md)、[共通UI](../../../UI_DESIGN.md) | 未移行 |
| `receipt` レシート一覧・登録・編集 | R02〜R03 | [レシートWinUI](../../ledger-receipt-ui.md)、[候補](../../ledger-receipt-suggestions.md)、[レシートAPI](../../ledger-receipt-api.md) | 未移行 |
| `transfer` 振替一覧・登録・編集 | R04〜R05 | [振替WinUI](../../ledger-transfer-ui.md)、[振替API](../../ledger-transfer-api.md) | 未移行 |
| `balance` 残高一覧・登録・編集 | R06〜R07 | [共通UIの残高仕様](../../../UI_DESIGN.md)、[残高API](../../ledger-balance-api.md)、[画面台帳](../../screen-inventory.md) | 未移行 |
| `payment-memo` 支払メモ | 記録画面内の共通パネル（独立画面IDなし） | [共通UIの支払メモ](../../../UI_DESIGN.md)、[画面台帳](../../screen-inventory.md) | 未移行 |
| `analytics` 科目別・収支・残高集計と内訳 | R08〜R09 | [共通UIの集計](../../../UI_DESIGN.md)、[画面台帳](../../screen-inventory.md)、[旧版／新版の比較](../../legacy-new-final-parity-matrix.md) | 未移行 |
| `person` 人物管理 | M01〜M03 | [人物](../../ledger-persons.md)、[競合と後続](../../ledger-person-conflicts.md) | [草案作成・正本未切替](features/person/README.md) |
| `category` 科目管理 | M04〜M06 | [家計簿DB・モデル](../../ledger-core-schema.md)、[共通UI](../../../UI_DESIGN.md) | 未移行 |
| `payment-method` 支払方法管理 | M07〜M09 | [家計簿DB・モデル](../../ledger-core-schema.md)、[共通UI](../../../UI_DESIGN.md) | 未移行 |
| `participant` 参加者管理 | M10〜M11 | [参加者](../../ledger-participants.md)、[参加者API契約](../api/features/members.md) | 未移行 |
| `invitation` 招待管理 | M12〜M13 | [招待](../../ledger-invitations.md)、[共通UI](../../../UI_DESIGN.md) | 未移行 |

共通仕様の移行元は[UI_DESIGN.md](../../../UI_DESIGN.md)、[入力コントロール設計](../../59-input-controls-design.md)、[画面台帳](../../screen-inventory.md)。WinUIの実装上の責務は[構成](../../architecture-winui.md)と[monetail WinUI architecture Skill](../../../.agents/skills/monetail-winui-architecture/SKILL.md)を参照する。

## 1機能ずつの移行手順

1. 対象機能の既存正本、画面台帳、現行WinUI・Client実装、必要に応じて旧Windows版を照合し、**確定済み・提案・未決定**を分類する。決定履歴は疑義のある決定番号だけ確認する。
2. 新しい機能別仕様に、**対象範囲・表示／入力・操作フロー・状態遷移・エラー／復旧・受入条件・未決定事項**を整理する。業務契約は既存正本へリンクし、同じ規則を二重記述しない。
3. 旧節と新節を対応付け、確定事項の欠落・意味の変更・参照切れをレビューする。矛盾・不明点は解消を推測せず、未決定として確認する。
4. **レビュー・了承を得てから、対象機能単位で正本を切り替える。** `docs/INDEX.md` と画面台帳のリンクを更新し、旧文書の移行済み内容は新正本への案内に置き換える。異なる機能の正本やAPI契約は触らない。
5. この表を「移行済み」に更新する。検証条件は[tests/README.md](../../../tests/README.md)に従い、未実施の確認は未実施と報告する。

### 正本切替の完了条件

- 対象機能の**確定済み操作・初期値・保存／削除・競合／401・権限・未保存保護**に欠落と意図しない変更がない。
- 同じ規則を新旧の2か所で保守せず、必要な旧リンク・画面台帳・`docs/INDEX.md` から正本へ到達できる。
- 提案・未決定は確定事項に混入せず、受入条件から実装・テスト範囲を判断できる。
- 文書整理だけのPRで、WinUI・Client・APIコードや業務契約を暗黙に変更しない。

最初の移行候補は**人物管理**（一覧・編集・並び替えが一機能内で完結し、文書形式の試行に向く）。この候補は作業順序の提案であり、本文移行や正本切替を承認済みとみなさない。
