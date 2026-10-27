# monetail-legacy

明細・分類・対象者を細かく記録するWindows向け家計簿と、Web化に向けたAPIのリポジトリです。

- Windows版: VB.NET / WinForms / .NET Framework 4.8 / SQLite
- API: C# / ASP.NET Core / .NET 10。実装範囲は[認証契約](docs/43-passkey-authentication-design.md)と[家計簿設計](docs/58-ledger-foundation-design.md)を参照

## まず読むもの

[文書の入口・更新先](docs/INDEX.md)から必要な正本だけを選んでください。

| 用途 | 文書 |
| --- | --- |
| 次に進めること | [ロードマップ](docs/07-roadmap.md) |
| APIの認証仕様 | [認証・アカウント管理](docs/43-passkey-authentication-design.md) |
| 新規利用・家計簿の設計 | [家計簿設計](docs/58-ledger-foundation-design.md) |
| APIを起動・運用する | [起動・設定・管理コマンド](docs/42-api-foundation.md) |
| 検証する | [テスト](tests/README.md) |
| Windows版を操作する | [操作ガイド](docs/09-user-guide.md) |

## 開発・起動

Windows版は[ソリューション](HouseholdAccountsBeta/HouseholdAccountsBeta.sln)をVisual Studioで開き、NuGet復元後にビルドします。DBは含みません。[接続設定](docs/19-database-startup-settings.md)に従ってバックアップしたコピーを指定し、新規セットアップにはマスタの準備も必要です。

APIは.NET 10で`dotnet run --project src/Monetail.Api --urls http://127.0.0.1:5080`、Dockerは`docker compose up --build -d`で稼働確認できます。既定はDB不要の`/health`だけです。認証を有効にする設定・永続化・復旧は[運用手順](docs/42-api-foundation.md)を参照してください。

実データ・秘密・.envをコミットしないでください。自動テストの成功と、実端末・実DB・本番配備の確認は区別します。
