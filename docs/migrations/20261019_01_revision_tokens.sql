-- Monetail legacy: 古い編集内容の上書き防止（2026-10-19）
-- 税端数移行の後、バックアップから作ったテスト用コピーに一回だけ適用する。
-- 事前に3表のPRAGMA table_infoでrevision_tokenが未追加であることを確認する。
-- 再実行はduplicate column nameで拒否される。失敗時はトランザクションを
-- Rollbackし、更新前バックアップから復元する。旧アプリとの同時利用は行わない。
-- 既存行は更新版'0'として読む。金額・日時・タグ・割当・税設定は変更しない。
-- 新アプリは登録・更新ごとに新しいGUIDを保存する。FK/user_versionは維持する。

BEGIN IMMEDIATE;

ALTER TABLE t_receipts
  ADD COLUMN revision_token TEXT NOT NULL DEFAULT '0';

ALTER TABLE t_transfers
  ADD COLUMN revision_token TEXT NOT NULL DEFAULT '0';

ALTER TABLE t_balances
  ADD COLUMN revision_token TEXT NOT NULL DEFAULT '0';

COMMIT;
