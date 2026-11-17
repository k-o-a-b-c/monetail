---
name: monetail-winui-architecture
description: "monetailのWinUI 3画面追加・MainWindow分離・構造レビュー用。画面とClientの責務、状態の二重管理、依存増加を防ぐ。API・DBのみの変更には適用しない。"
---

# monetail WinUIの責務分離

本Skillは**構造の判断**だけを扱う。仕様承認は`AGENTS.md`、画面・操作は`UI_DESIGN.md`、現状と分離候補は`docs/architecture-winui.md`、業務契約は対象機能の正本を参照する。WinUI API/標準UI/LOB/コード品質/実行テストは既存`winui-*`・`windows-lob-xaml` Skillを使い、本Skillへ複製しない。

## 責務の境界

| 所有者 | 担当すること |
| --- | --- |
| `MainWindow` | 共通ナビゲーション、家計簿・アカウント切替、画面間の未保存離脱確認、Windowの寿命 |
| 画面の`UserControl`/`Page` | その画面のXAML、表示、選択、入力・フォーカス/IMEと操作イベント |
| `Monetail.Windows.Client` | 既存の業務モデル、API通信、認証・権限、保存、競合と復旧の契約 |

境界は**分離先の目標**であり、既存コードがこの構成になっているという意味ではない。既存Clientの下書き・更新版・401復帰などの状態をWinUIへコピーしない。画面固有の生入力・表示状態だけは必要に応じてViewが保持する。

## 実装・レビュー手順

1. 対象の`MainWindow.xaml`領域、partial、Clientモデル、対応スモークテストから、画面の内部`x:Name`参照と状態・イベントの所有者を確認する。
2. 分離する責務とWindow側に残す責務を決める。`partial`ファイルの移動だけでは分離ではない。Viewから`MainWindow`や他画面の内部コントロールを参照しない。
3. Viewの公開面を**必要な表示データと操作通知**に限定する（`Views/LedgerChoiceView`を一例とする）。Window/Client全体を渡さない。ViewModel、Service、DI、新イベント層は必要性を説明できる場合に限る。MVVM化そのものを目的にしない。
4. **1 PRで1画面境界を基本**とし、構造変更に画面仕様変更・再設計を混ぜない。変更前後で未保存保護、401復帰、競合、選択・キー操作・フォーカス等の既存契約を維持する。
5. PRでは削減した直接依存、増減したフィールド・フラグ・イベント・抽象化、状態重複とコード総量を比較する。行数の減少だけで成功としない。対象テストは`tests/README.md`に従って実施し、未実施を明示する。

分離の検討順序は[`docs/architecture-winui.md`](../../../docs/architecture-winui.md#winuiの責務棚卸しと段階的分離検討順序未実装)に置き、このSkillに複製しない。
