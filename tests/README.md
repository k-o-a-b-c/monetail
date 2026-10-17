# 税端数処理の自動テスト

リポジトリのルートで実行します。

```sh
dotnet run --project tests/CalculationTests/CalculationTests.vbproj
python3 -m unittest discover -s tests -p 'test_migration.py' -v
```

計算テストは .NET 8 SDK を使い、実アプリの VB ソースを直接コンパイルします。JSON の全11ケース、税率別合算、旧 `floor` の負数・浮動小数点境界、手入力値引・率値引、収入／支出、人別参考配分、残高推算を検証します。残高推算の入出力だけをメモリ上で供給し、計算処理は製品コードを実行します。

マイグレーションテストは Python 標準ライブラリの SQLite で実際のスキーマと移行 SQL を実行し、既存行・割当・合計の保持、2列だけの追加、既定値・制約、PRAGMA の保持、再実行拒否を検証します。

Windows と .NET Framework 4.8 がある環境では次も実行します。

```powershell
dotnet run --project tests/PersistenceTests/PersistenceTests.vbproj
```

こちらは実際の `DataAccess`、SQLite プロバイダー（製品と同じ 1.0.113.1）、登録画面、店舗設定画面、独自コントロールを直接コンパイルします。一時DBで新規保存／編集、一覧・残高用の両読込、履歴候補、店舗既定値の独立性、更新失敗時のロールバック、新規／編集取消／流用の画面状態を検証します。確認ダイアログを開かない画面操作は実際のイベント処理を呼び出します。既存DBは使用しません。

GitHub Actions の `Tests` は上記テストと、Windows で既存ソリューションの NuGet 復元・ビルドを実行します。
