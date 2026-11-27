# レシートAPIの参照案内

APIの正本は[レシートAPI](specs/api/features/receipts.md)へ移行した。履歴候補と基本情報自動補完は[履歴API](specs/api/features/receipt-history.md)、物理DBは[レシートDB](ledger-receipt-schema.md)、画面操作は[レシート画面](ledger-receipt-ui.md)、実装状況は[API実装台帳](specs/api/README.md)を参照する。以下の旧見出しは既存リンク用の参照案内。

## 保存・取得・一覧の基本契約（DEC-224〜254）

[レシートAPI](specs/api/features/receipts.md)を参照。

## カーソルの実装境界（DEC-231）

[レシートAPIの一覧取得](specs/api/features/receipts.md#41-一覧get)を参照。

<a id="保存行を先頭にする開始位置指定dec-350実装済み"></a>

### 保存後の一覧復帰（DEC-370）

[レシートAPIの一覧取得](specs/api/features/receipts.md#41-一覧get)を参照。

### 一覧GETの内部実装（工程4）

[レシートAPIの一覧取得](specs/api/features/receipts.md#41-一覧get)を参照。

## 金額計算・参考額（DEC-156〜157）

[計算契約](12-calculation-contract.md#新レシートの明細別税率と計算境界)を参照。

### 工程2の実装と工程3への接続

[計算契約](12-calculation-contract.md#新レシートの明細別税率と計算境界)を参照。

### 複数税率の計算粒度（グループ参考額：DEC-156、グループ別人物按分：DEC-157）

[計算契約](12-calculation-contract.md#新レシートの明細別税率と計算境界)を参照。

## JSON・再送・権限・エラー（DEC-256〜262）

[整合性・再送](specs/api/features/receipts.md#7-整合性再送)を参照。

### 一覧GET：要約7フィールド（DEC-256）

[レシートAPIの一覧取得](specs/api/features/receipts.md#41-一覧get)を参照。

### 詳細GET：階層、型、エラー（DEC-257）

[詳細GET](specs/api/features/receipts.md#52-詳細get)を参照。

### 保存リクエストと無変更PUT（DEC-258）

[書込要求](specs/api/features/receipts.md#42-postputdelete)を参照。

### requestIdによる再送保証（DEC-259）

[整合性・再送](specs/api/features/receipts.md#7-整合性再送)を参照。

### 権限・エラー（DEC-260）

[競合・失敗応答](specs/api/features/receipts.md#6-競合失敗応答)を参照。

### 科目・明細の保存必須条件（DEC-262）

[レシートAPI](specs/api/features/receipts.md)を参照。

## DELETE成功再送・409応答（DEC-263〜264）

[整合性・再送](specs/api/features/receipts.md#7-整合性再送)を参照。

### DELETE成功後の再送権限（DEC-263）

[整合性・再送](specs/api/features/receipts.md#7-整合性再送)を参照。

### 409応答・競合後の2択（DEC-264）

[競合・失敗応答](specs/api/features/receipts.md#6-競合失敗応答)を参照。

## 不正カーソル（DEC-270）

[レシートAPIの一覧取得](specs/api/features/receipts.md#41-一覧get)を参照。

## 実装時の参照

[レシートAPI](specs/api/features/receipts.md)を参照。
