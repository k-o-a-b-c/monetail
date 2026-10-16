-- Monetail legacy (VB.NET / SQLite)
-- 店舗別の「初期値」とレシートごとの税端数モード（2026-10-17）
-- ステータス: 実装前の移行SQL案。実DBには未適用。
--
-- 【適用前】
-- 1. 元DBをバックアップし、そのコピーだけでテストする。
-- 2. PRAGMA table_info('m_shop'); PRAGMA table_info('t_receipts'); で列未追加を確認。
-- 3. 既存DBへ一回だけ適用。二度実行すると「duplicate column name」になる。
-- 4. 古いアプリとのDB共有を止め、新しいアプリのテストと同時に導入する。
--
-- 【設計原則】
-- ・t_receipts.tax_rounding_modeが会計計算の正本。店舗マスタ登録は任意。
-- ・m_shop.tax_rounding_modeは登録済み店舗からの新規入力時の初期値のみ。
-- ・t_receipts.shop_codeやFKは追加しない。
-- ・既存行の端数方式はfloorとして維持。過去のtotalは再計算・UPDATEしない。
-- ・SQLite接続のPRAGMA foreign_keysおよびuser_versionは変更しない。

BEGIN IMMEDIATE;

ALTER TABLE m_shop
  ADD COLUMN tax_rounding_mode TEXT NOT NULL DEFAULT 'floor'
  CHECK (tax_rounding_mode IN ('floor', 'half_up'));

ALTER TABLE t_receipts
  ADD COLUMN tax_rounding_mode TEXT NOT NULL DEFAULT 'floor'
  CHECK (tax_rounding_mode IN ('floor', 'half_up'));

COMMIT;

-- 移行後の読取確認（SQLファイルに自動実行させず、必要時に別途実行）
-- PRAGMA table_info('m_shop');
-- PRAGMA table_info('t_receipts');
-- SELECT tax_rounding_mode, COUNT(*) FROM m_shop GROUP BY tax_rounding_mode;
-- SELECT tax_rounding_mode, COUNT(*) FROM t_receipts GROUP BY tax_rounding_mode;
-- PRAGMA integrity_check;
--
-- 失敗時は更新前バックアップを復元し、旧・新アプリの同時運用を行わない。
