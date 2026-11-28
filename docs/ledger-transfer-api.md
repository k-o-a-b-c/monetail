# 振替APIの参照案内

APIの正本は[振替API](specs/api/features/transfers.md)・[振替履歴API](specs/api/features/transfer-history.md)へ移行した。物理DBは[振替DB](ledger-transfer-db.md)、画面操作は[振替画面](ledger-transfer-ui.md)、実装状況は[API実装台帳](specs/api/README.md)を参照する。以下の旧見出しは既存リンク用の参照案内。

## 実装範囲

[API実装台帳](specs/api/README.md)を参照。

### 新規POSTの実装

[API実装台帳](specs/api/README.md)を参照。

### 更新PUTの実装

[API実装台帳](specs/api/README.md)を参照。

### 削除DELETEの実装

[API実装台帳](specs/api/README.md)を参照。

### 振替名候補GETの実装

[API実装台帳](specs/api/README.md)を参照。

### 自動補完GETの実装

[API実装台帳](specs/api/README.md)を参照。

## 権限（確定・API強制）

[エンドポイントと権限](specs/api/features/transfers.md#2-エンドポイントと権限)を参照。

## 振替APIの基本契約（2026-11-15確定）

[振替API](specs/api/features/transfers.md)を参照。

### APIパス・成功ステータス

[振替API](specs/api/features/transfers.md)を参照。

### JSON応答と書込リクエスト（2026-11-15確定）

[振替API](specs/api/features/transfers.md)を参照。

### 一覧検索とカーソル

[一覧GET](specs/api/features/transfers.md#41-一覧get)を参照。

### 振替名候補・自動補完API（2026-11-15確定）

[振替履歴API](specs/api/features/transfer-history.md)を参照。

### 自動補完GETの権限境界（DEC-280・2026-11-15確認済み）

[振替履歴API](specs/api/features/transfer-history.md)を参照。

### 共通エラー・再送・レート制限（2026-11-15確定）

[競合・失敗応答](specs/api/features/transfers.md#6-競合失敗応答)・[整合性・再送](specs/api/features/transfers.md#7-整合性再送)を参照。

### 保存・削除の再送と競合

[競合・失敗応答](specs/api/features/transfers.md#6-競合失敗応答)・[整合性・再送](specs/api/features/transfers.md#7-整合性再送)を参照。
