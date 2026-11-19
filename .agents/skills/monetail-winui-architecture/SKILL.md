---
name: monetail-winui-architecture
description: "monetailのWinUI 3画面追加・遷移・オンデマンド生成・MVVM責務分離・MainWindow分離・構造レビュー用。API・DBのみの変更には適用しない。"
---

# monetail WinUIの画面構成と責務分離

本Skillは**画面構成と標準実装を選ぶ際の判断基準**を扱う。仕様承認は`AGENTS.md`、画面・操作は`UI_DESIGN.md`、現状と分離候補は`docs/architecture-winui.md`、業務契約は対象機能の正本を参照する。具体的なAPI・XAML・LOB・コード品質・実行テストは[`winui-design`](../winui-design/SKILL.md)、[`winui-code-review`](../winui-code-review/SKILL.md)、[`windows-lob-xaml`](../windows-lob-xaml/SKILL.md)等を参照し、本Skillへ詳細を複製しない。

## 責務の境界

| 所有者 | 担当すること |
| --- | --- |
| `MainWindow` | 共通ナビゲーション、家計簿・アカウント切替、画面間の未保存離脱確認、Windowの寿命 |
| 画面の`UserControl`/`Page`（View） | XAML、データバインド、コントロール固有の入力・選択・フォーカス/IME・イベント接続 |
| `Monetail.Windows.Presentation`等のViewModel | 表示用の状態投影、入力可否・検証結果の表示、Command、画面内の非同期表示状態。既存Clientの状態を正本として利用 |
| `Monetail.Windows.Client` | 既存の業務モデル・編集下書き、API通信、認証・権限、保存、競合と復旧の契約 |

境界は**分離先の目標**であり、既存コードがこの構成になっているという意味ではない。既存Clientの下書き・更新版・401復帰などの状態をWinUIへコピーしない。画面固有の生入力・表示状態だけは必要に応じてViewが保持する。

## MVVMの適用基準

- **MVVMを基本とする**：一覧・入力フォームなど表示状態や操作が複雑な画面は、既存の`Monetail.Windows.Presentation`と`CommunityToolkit.Mvvm`を優先してViewModelに表示状態・Commandを集約する。単純な表示・ダイアログ・UIイベントのためだけにViewModelを新設しない。既存画面は対象境界の移行時に適用する。
- **Viewに残す処理**：`Page`/`UserControl`のコードビハインドはコントロール初期化、フォーカス・IME・文字選択・スクロール、WinUIイベントやダイアログ表示の調停などUI固有処理を担当してよい。業務判断・保存処理や一覧データの独自再計算をViewへ置かない。
- **ViewModelの独立性**：表示用の投影・入力可否・検証結果・Commandを扱い、`Window`/`Page`/`TextBox`等のWinUIコントロールや他画面を参照しない。API・認証・編集下書き・更新版・競合状態の正本は既存Clientに残し、ViewModelへ複製しない。既存ViewModelを優先し、同じ状態をViewとViewModelの両方で管理しない。
- **バインディングと複雑化の抑制**：既知の型は原則`{x:Bind}`を使い、動的表示は適切な`Mode`と変更通知を指定する。UI固有の入力動作までCommand化・バインド化することを目的にしない。新規ViewModel、Service、DI、変換層が必要なら、既存実装で代替できない理由を説明する。

## WinUI 3の標準実装を選ぶ基準

- **標準の実例から選ぶ**：XAML・コントロール・画面遷移を実装する前に、対象画面の目的を整理し、`winui-design`に従ってWinUI Gallery・Microsoft公式サンプルの該当パターンを確認する。利用可能なら`winapp find-ui`で実例を探し、採用するAPIは復元済みWinUIアプリの参照に対して`winapp find-api --project-dir`で確認する。ツールを使えない場合はMicrosoft Learnや対象プロジェクトの参照で代替確認し、未確認事項を報告する。**ツールの実行・API確認を行ったと偽らない**。
- **用途に合う組み込みコントロール**：確定済み画面設計を守り、管理の左メニューは`NavigationView`、集計種別切替は`SelectorBar`、一覧・表は原則`ListView`（列表示は`Grid`ベースの項目テンプレート）など、WinUI 3の標準機能を先に評価する。検索候補は`AutoSuggestBox`、確認は`ContentDialog`、非モーダルな状態・エラーは`InfoBar`等を用途で選び、WPFの`DataGrid`など別フレームワークのコントロールをWinUI標準と誤認しない。数値入力も精度や既存の[入力仕様](../../../docs/59-input-controls-design.md)を優先し、`NumberBox`を無条件に採用しない。
- **WinUI 3固有の作法**：XAML/C#は`Microsoft.UI.Xaml`と現行Windows App SDKのAPIを使い、WPFの`System.Windows`やUWPの`Windows.UI.Xaml`の方法を流用しない。コンパイル時に型が分かる場合は`{x:Bind}`を優先し、変更される値は実効`Mode`（既定は`OneTime`、`x:DefaultBindMode`の継承に注意）と変更通知を確認する。型付き`DataTemplate`では`x:DataType`を使用する。入力確定タイミングは既存のIME・キー操作仕様を守る。
- **Fluent Designとアクセシビリティ**：標準スタイル・`ThemeResource`・意味に沿ったレイアウトを優先し、独自の固定色・テンプレート・装飾を安易に作らない。Light/Darkに加えてHigh Contrast、キーボード・フォーカス・スクリーンリーダー、アイコンのみのボタンの名前、必要な`AutomationProperties`、狭幅、読み込み中・空・エラー状態を対象画面に応じて検証する。色だけで状態を伝えない。
- **標準動作を再実装しない**：コントロール既定の選択・スクロール・ホイール・フォーカス・キー操作、一覧の仮想化を優先する。標準コントロールやGalleryのパターンで要件を満たせない場合だけ、既存仕様との具体的な差分と追加する独自処理の必要性を示す。外部UIライブラリ、新しい制御基盤、独自`ControlTemplate`は標準機能で代替できないことを確認してから検討する。

## 画面構成・生成・寿命の原則

- **標準ナビゲーション**：同一Windowの主領域は原則WinUI 3の`Frame`と`Page`で`Frame.Navigate`する。共通上部はShellに置き、管理・設定の左メニューは承認済み設計どおり`NavigationView`を使用する。画面内の再利用部品は`UserControl`とし、単なる部品をページ化しない。例外は必要性と対象画面を明示する。
- **必要時だけ生成**：未訪問の`Page`・画面単位の`UserControl`・`ContentDialog`を起動時や`MainWindow.InitializeComponent`で先行生成しない。画面は初回遷移時、ダイアログは操作時に生成する。全画面を同時配置して`Visibility`で切り替えない（画面内部の部分表示切替は可）。
- **通常離脱時は解放可能にする**：`Page.NavigationCacheMode`は原則`Disabled`とし、通常離脱後の画面インスタンスをWindowのフィールド・独自キャッシュ・未解除のイベント購読等で常駐させない。再訪は新たに生成する。入力・選択・スクロールなど復元が必要な値は、その機能の確定契約に従って正しい所有者へ保持し、安易な状態コピーや汎用キャッシュを増やさない。
- **認証復帰等の限定例外**：既存のレシート401復帰は元のView/VM/入力コントロールを保持する契約であり、画面の強制破棄・再生成で壊さない。こうした実コントロール保持が必要な場合だけ、生成済み画面を復帰または破棄まで一時保持してよい。保持理由・終了条件・参照の解放方法を実装とPRで明示し、通常の画面キャッシュに広げない。
- **遷移を一か所で調停**：共通ナビゲーション、未保存離脱確認、認証・家計簿切替の許否はShell/Window側で調停する。確認取消時は元の画面・入力・メニュー選択を維持し、許可前に`Frame.Navigate`や画面破棄をしない。画面内の操作は画面が所有し、Windowが子画面の`x:Name`へ直接触れない。
- **標準動作優先**：WinUI 3標準の遷移・選択・フォーカス・スクロール・ダイアログ動作を先に検証する。独自ルーター、画面レジストリ、フラグ、イベント中継層を先回りで作らず、標準機能で満たせない要件と最小限の補完処理を説明する。

## 実装・レビュー手順

1. 対象の`MainWindow.xaml`領域、partial、Clientモデル、対応スモークテストから、画面の生成時点・遷移経路・内部`x:Name`参照・状態・イベント購読の所有者を確認する。
2. 分離する責務とWindow側に残す責務を決める。`partial`ファイルの移動だけでは分離ではない。Viewから`MainWindow`や他画面の内部コントロールを参照しない。
3. Viewの公開面を**必要な表示データと操作通知**に限定する（`Views/LedgerChoiceView`を一例とする）。Window/Client全体を渡さない。複雑な画面では既存ViewModelの再利用を優先し、Viewの業務処理・ViewModelのWinUIコントロール依存・編集状態の二重管理がないか点検する。単純なUI処理を機械的にMVVM化しない。
4. **1 PRで1画面境界を基本**とし、共通Shell/`Frame`導入は独立した工程にする。構造変更に無関係な画面仕様変更を混ぜず、未保存保護、401復帰、競合、選択・キー操作・フォーカス等の既存契約を維持する。
5. PRでは生成前の未訪問画面が存在しないこと、通常の離脱・再訪、未保存確認の取消、401復帰中の一時保持と終了、古い非同期応答の混入防止、イベント解放を対象に応じて検証する。さらに標準コントロール選定・WinUI 3 APIの確認根拠、標準から外れた理由、テーマ・キーボード・アクセシビリティへの影響を対象に応じて確認する。既存スモークを含む実施可能なテストは`tests/README.md`に従い、未実施のUI/寿命検証を明示する。
6. PRではMVVMの役割分担、削減した直接依存、増減したフィールド・フラグ・イベント・抽象化、状態重複とコード総量を比較する。行数の減少だけで成功としない。自動構造検査が導入されるまではレビューで新規違反を止め、**CIが未検出の項目を合格済みと報告しない**。

分離の検討順序は[`docs/architecture-winui.md`](../../../docs/architecture-winui.md#winuiの責務棚卸しと段階的分離検討順序未実装)に置き、このSkillに複製しない。
