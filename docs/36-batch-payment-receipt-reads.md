# 支払方法別レシートの階層一括読込（LEG-026）

更新: 2026-10-21。残高推算等で使う`GetPaymentReceipts`を対象とする。

## 変更と維持する条件

親→グループ→明細→対象者の個別SELECTを、LEFT JOINの1 SELECTへ変更する。[35 GetReceipts](35-batch-receipt-reads.md)のSELECT列・JOINと階層の組立を`ReceiptHierarchySelect`／`ReadReceiptHierarchy`へ共通化し、両入口から使う。子のない親・グループ、対象者なしの明細を保持し、複合キーと並び順で重複生成を防ぐ。

- 口座コードは完全一致。`GetReceipts`の「-1は全口座」という条件を流用しない。実際にコード-1の口座があれば、その口座のレシートだけを読む。
- 通常は`dateFrom <= date <= dateTo`、`endExclusive=True`では`dateFrom <= date < dateTo`。小数秒や文字列の比較方法、空期間・逆転期間を維持する。
- 現行DDLで改修前の表走査が返す**Seq昇順**をORDER BYで明示する。子はグループSeq・明細Seq・対象者コードの昇順。一覧用`GetReceipts`の日時降順とは分ける。
- 推算は従来どおりレシートごとに整数を加減する。最終値が範囲内でも途中で超過するケースを、順序変更や合算方法の変更で隠さない。
- マスタ参照、全ヘッダー・子の値、NULL文字列、小数割引率、税設定・計算・更新版を維持する。入力・保存・DDL・接続設定・画面操作は変更しない。

`LoadReceiptChildData`は従来の個別読込を維持する。振替・残高・支払メモのSQL一括化は今回の対象に含めない。

## 検証と計測

`PaymentReceiptReadTests.ReadLegacy`に改修前の親SELECTを残し、未変更の製品`LoadReceiptChildData`で子を読む。新しい共通JOINに依存しない参照として、回帰と同一CI内の性能比較に使う。実SQLite・FK ON/OFFで、固定期待値と参照読込の両方により、空階層・複数対象者・同じ子番号の別親・Seq=0・小数割引／複数税率／非課税負額・NULLタグ・版・マスタ参照を比較する。

通常／上端除外、日付境界・小数秒、空／逆転期間、複数口座・負の口座コード・該当なしと階層1 SELECTを確認する。正負それぞれで、日付順なら相殺されるがSeq順では途中超過する推算と、金額修正後の復帰も確認する。全保存値・型・版・スキーマ・PRAGMAの保持と書込0回を検証する。既存の日末推算・小数秒TEXT・月年／閏日・最小最大日時の回帰も継続する。

[34 計測方法](34-receipt-read-performance.md)の4データケースを、一覧読込・改修前の支払方法別参照・改修後の支払方法別読込で各々実行する。CSVの`reader`列で3経路を区別し、各ケースを新規DB／DataAccessで開始する。支払方法別の期間は月初／年初から翌月初／翌年初の直前まで。合成データには最終秒内の小数部分がないため、従来の一覧の上端条件と同じ対象件数になる。

キャッシュ空／保持済みのSELECT数と、Traceを外した5回の読込時間を記録する。SQL数・時間の固定しきい値は設けない。推算画面全体や利用者の実DBの速度ではなく、同じCIマシン内の読込経路比較として評価する。

実測値・実行コミット・CIは[この改修PR](https://github.com/k-o-a-b-c/monetail-legacy/pulls?q=is%3Apr+head%3Aperf%2Fbatch-payment-receipt-reads)、現在のテスト件数・実行方法・最新結果は[tests/README.md](../tests/README.md)に記録する。
