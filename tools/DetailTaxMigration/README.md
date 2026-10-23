# 明細税率のコピー移行ツール

実行方法・変更内容・失敗時の契約は[41 コピーDB移行](../../docs/41-detail-tax-copy-migration.md)。[42の新Windows版](../../docs/42-detail-tax-application.md)が移行後DBへ対応しています。旧版では開けません。Windows自動検証は完了しました。手動確認と実期間データ確認は未完了です。

元DBを読取専用で開き、新規のコピーDBと比較CSVを作成します。コピー側の`t_receipts.total`もNULLを含めて保持し、再計算値は比較用の監査テーブルとCSVだけへ記録します。実運用DBの置換やアプリ起動時の自動移行は行いません。
