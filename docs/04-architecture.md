# 04 アーキテクチャ・開発環境

更新日: 2026-11-01 / 現行Windows版・APIの構成と、新版Windows版の実装範囲を区別する。検証状況は[tests/README.md](../tests/README.md)を参照する。

## 分野別の参照先

必要な対象の文書だけを参照してください。実装済み・未実装・未了承の区別は分割先を正本とし、この入口に重複して記載しません。

| 対象 | 正本 |
| --- | --- |
| 旧VB.NET / WinForms・SQLite・処理フロー・起動・検証詳細 | [旧Windows版](architecture-legacy.md) |
| 新Windows版・WinUI 3・Client・配布の方針 | [新Windows版](architecture-winui.md) |
| ASP.NET Core API・機能間の依存・将来の責務分離案 | [API責務](architecture-api.md) |
| リポジトリ内の配置・CI・関連資料 | [配置とCI](architecture-repository.md) |

## 旧見出しリンクの互換案内

既存の `04-architecture.md#...` はここから分割先へ辿れます。新規参照は上表の正本へ直接リンクしてください。

<a id="構成"></a> [構成](architecture-legacy.md#構成)

<a id="新版windows版の構成"></a> [新版Windows版の構成](architecture-winui.md#新版windows版の構成)

<a id="新版windows版の配布削除未実装"></a> [新版Windows版の配布・削除（未実装）](architecture-winui.md#新版windows版の配布削除未実装)

<a id="ディレクトリ"></a> [ディレクトリ](architecture-repository.md#ディレクトリ)

<a id="代表的な処理フロー"></a> [代表的な処理フロー](architecture-legacy.md#代表的な処理フロー)

<a id="起動開発上の注意"></a> [起動・開発上の注意](architecture-legacy.md#起動開発上の注意)

<a id="自動検証"></a> [自動検証](architecture-repository.md#自動検証)

<a id="apiの責務と依存"></a> [APIの責務と依存](architecture-api.md#apiの責務と依存)

<a id="後続の責務分離案未了承未実装"></a> [後続の責務分離案（未了承・未実装）](architecture-api.md#後続の責務分離案未了承未実装)

<a id="関連する詳細資料"></a> [関連する詳細資料](architecture-repository.md#関連する詳細資料)

<a id="根拠"></a> [根拠](architecture-legacy.md#根拠)
