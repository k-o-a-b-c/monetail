using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Monetail.WinUI.Views.Management;

namespace Monetail.WinUI.Smoke;

public partial class App
{
    private async Task CheckManagementMvvm()
    {
        using var fixture = new SyntheticConnection(saved: true);
        var server = new PeopleServer();
        var root = await OpenPersonWindow(fixture, server);
        var page = ManagementFeature(root) as PeoplePage ?? throw new InvalidOperationException("人物Pageがありません。");
        var model = page.ViewModel;
        Check(root.FindName("PeopleList") is null && root.FindName("PersonDialog") is null &&
            FindManagementElement(root, "PersonDialog") is null,
            "人物一覧の内部参照と先行生成ダイアログをMainWindowに残さない");
        await InvokeAsync(() => Find<Button>(root, "AddPersonButton"));
        await WaitPersonEditorReady(root);
        var dialog = Find<ContentDialog>(root, "PersonDialog");
        Find<TextBox>(root, "PersonName").Text = "画面内の入力";
        await Wait(() => model.Name == "画面内の入力");
        var reads = server.Reads;
        var navigation = Find<NavigationView>(root, "ManagementNavigation");
        navigation.SelectedItem = Find<NavigationViewItem>(root, "AccountNavigation");
        Check(ReferenceEquals(page, ManagementFeature(root)) && ReferenceEquals(dialog, Find<ContentDialog>(root, "PersonDialog")) &&
            model.Name == "画面内の入力" && server.Reads == reads,
            "未承認の管理内遷移では同じ人物Page・ダイアログ・入力を維持する");
        await InvokeAsync(() => Find<Button>(root, "CancelPersonButton"));
        await Wait(() => EditorClosed(root));
        Check(FindManagementElement(root, "PersonDialog") is null && !model.HasEditor && model.Creation is null && model.Name == "",
            "通常キャンセルは編集状態とダイアログ参照を解放する");
        Find<CheckBox>(root, "IncludeInactivePeople").IsChecked = true;
        navigation.SelectedItem = Find<NavigationViewItem>(root, "AccountNavigation");
        await Wait(() => ManagementFeature(root) is AccountPage);
        var accountPage = (AccountPage)ManagementFeature(root)!;
        var management = ((Monetail.WinUI.Views.Workspace.ManagementPage)Find<FrameworkElement>(root, "ManagementPage")).ViewModel;
        var accountName = management.AccountName;
        Check(!ReferenceEquals(page, ManagementFeature(root)) && model.IncludeInactive,
            "通常移動は人物PageをFrameから外し、必要な一覧設定だけをViewModelへ保持する");
        navigation.SelectedItem = Find<NavigationViewItem>(root, "PeopleNavigation");
        await Wait(() => server.Reads == reads + 1 && ManagementFeature(root) is PeoplePage { IsLoaded: true });
        var reopened = (PeoplePage)ManagementFeature(root)!;
        Check(!ReferenceEquals(page, reopened) && ReferenceEquals(model, reopened.ViewModel) && model.IncludeInactive &&
            FindManagementElement(root, "PersonDialog") is null,
            "再訪は新しい人物Pageと同じ一覧モデルを使い、未訪問のダイアログを生成しない");
        management.AccountName = "離脱後の表示名";
        await Settle();
        Check(Find<TextBlock>(accountPage, "AccountName").Text == accountName,
            "通常離脱したログイン情報PageはViewModelの表示名通知を購読し続けない");
        management.AccountName = accountName;
        await InvokeAsync(() => Find<Button>(root, "AddPersonButton"));
        await WaitPersonEditorReady(root);
        var nameInput = Find<TextBox>(root, "PersonName");
        nameInput.Text = "連続401でも保持";
        fixture.RejectSession = true;
        await InvokeAsync(() => Find<Button>(root, "SavePersonButton"));
        await Wait(() => Visible(root, "LoginPage") && EditorClosed(root));
        Check(ReferenceEquals(reopened, ManagementFeature(root)) && ReferenceEquals(model, reopened.ViewModel) && model.Name == nameInput.Text,
            "401は元の人物Page・ViewModel・実コントロールを破棄しない");
        var creation = model.Creation;
        Check(creation is not null, "401で未完了の人物作成要求を保持する");
        var interruptedMaster = false;
        fixture.PeopleHandler = async (request, token) =>
        {
            if (!interruptedMaster && request.Method == HttpMethod.Get)
            {
                interruptedMaster = true;
                fixture.RejectSession = true;
                return new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.Unauthorized);
            }
            return await server.HandleAsync(request, token);
        };
        fixture.RejectSession = false;
        fixture.CompleteLogin = true;
        await InvokeAsync(() => Find<Button>(root, "LoginButton"));
        await Wait(() => interruptedMaster && Visible(root, "LoginPage") && Find<Button>(root, "LoginButton").IsEnabled);
        await fixture.CallbackTask!;
        Check(ReferenceEquals(reopened, ManagementFeature(root)) && ReferenceEquals(model, reopened.ViewModel) &&
            ReferenceEquals(nameInput, Find<TextBox>(root, "PersonName")) && ReferenceEquals(creation, model.Creation) && nameInput.Text == "連続401でも保持" && server.Writes.Count == 0,
            "マスタ取得中の連続401でも最初のPage・モデル・入力と作成要求を保持する");
        await LoginAgain(fixture, root);
        Check(ReferenceEquals(nameInput, Find<TextBox>(root, "PersonName")) && ReferenceEquals(reopened, ManagementFeature(root)) && server.Writes.Count == 0,
            "認証復帰は同じ入力コントロールを再利用し、自動保存・二重要求をしない");
        await InvokeAsync(() => Find<Button>(root, "CancelPersonButton"));
        await Wait(() => EditorClosed(root));
    }
}
