using System.Net;
using System.Net.Http.Json;
using Monetail.Windows.Client;
using Monetail.Windows.Client.Authentication;
using Monetail.Windows.Client.Ledgers;
using Monetail.Windows.Client.Transport;
using Monetail.Windows.Presentation.Management;
using Xunit;

namespace Monetail.Windows.Presentation.Tests;

public sealed class PeopleManagementViewModelTests
{
    private static readonly PersonInfo Person = new(Guid.NewGuid(), "本人", null, null, 0, Guid.NewGuid());
    private static readonly PersonInfo Other = new(Guid.NewGuid(), "別の人物", null, null, 1, Guid.NewGuid());
    private static readonly PeopleInfo People = new([Person, Other], Person.PersonId, "Admin", Guid.NewGuid());

    [Fact]
    public async Task SelectionCommandsAndClientValidationKeepRawInput()
    {
        using var fixture = new Fixture(_ => throw new InvalidOperationException("検証失敗時は通信しない"));
        var model = fixture.Model;
        model.Apply(People);
        Assert.False(model.EditCommand.CanExecute(null));
        model.Selected = model.Rows[0];
        Assert.True(model.EditCommand.CanExecute(null));
        await model.EditCommand.ExecuteAsync(null);
        Assert.Same(Person, model.Editing);
        model.Name = "  ";
        model.FromText = "未確定";
        Assert.False(await model.SaveEditorAsync(null, null));
        Assert.Equal("  ", model.Name);
        Assert.Equal("未確定", model.FromText);
        Assert.Equal("人物名を入力してください。", model.NameError);
        Assert.Same(Person, model.Editing);
        fixture.Context.IsBusy = true;
        Assert.False(model.CanSave);
        fixture.Context.IsBusy = false;
        Assert.True(model.CanSave);
    }

    [Fact]
    public async Task CreateRetryRetainsClientDraftAndSameRequestBodyAfterUnknownResult()
    {
        var bodies = new List<string>();
        using var fixture = new Fixture(request =>
        {
            bodies.Add(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
            return bodies.Count == 1 ? new(HttpStatusCode.ServiceUnavailable) : Json(People);
        });
        fixture.Model.Apply(People);
        await fixture.Model.AddCommand.ExecuteAsync(null);
        fixture.Model.Name = "  新しい人  ";
        Assert.False(await fixture.Model.SaveEditorAsync(null, null));
        var draft = fixture.Model.Creation;
        Assert.NotNull(draft);
        Assert.True(await fixture.Model.SaveEditorAsync(null, null));
        Assert.Same(draft, fixture.Model.Creation);
        Assert.Equal(bodies[0], bodies[1]);
        Assert.Equal("  新しい人  ", fixture.Model.Name);
    }

    [Fact]
    public async Task ConflictNeverOverwritesUntilExplicitAdoptionAndDoesNotGetAgain()
    {
        var latest = Person with { Name = "最新", Revision = Guid.NewGuid() };
        var calls = 0;
        using var fixture = new Fixture(_ =>
        {
            calls++;
            return Json(new { code = "revision_conflict", latest, field = (string?)null }, HttpStatusCode.Conflict);
        });
        var model = fixture.Model;
        model.Apply(People);
        model.Selected = model.Rows[0];
        await model.EditCommand.ExecuteAsync(null);
        model.Name = "保存されていない入力";
        Assert.False(await model.SaveEditorAsync(null, null));
        Assert.Equal("保存されていない入力", model.Name);
        Assert.Equal(Person.Revision, model.Editing!.Revision);
        Assert.Equal(latest, model.Latest);
        Assert.False(model.CanSave);
        model.AdoptLatestCommand.Execute(null);
        Assert.Equal(latest, model.Editing);
        Assert.Equal("最新", model.Name);
        Assert.True(model.CanSave);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task ResumeKeepsOrderIdsRevisionAndSelectionWhileRefreshingNames()
    {
        var changed = People with { People = [Person with { Name = "変更名" }, Other], OrderRevision = Guid.NewGuid() };
        using var fixture = new Fixture(_ => Json(changed));
        var model = fixture.Model;
        model.Apply(People);
        model.OrderCommand.Execute(null);
        model.SelectedOrder = model.OrderRows[0];
        model.MoveDownCommand.Execute(null);
        await model.ResumeAsync(null, default);
        Assert.Equal(new[] { Other.PersonId, Person.PersonId }, model.OrderRows.Select(row => row.Person.PersonId));
        Assert.Equal(People.OrderRevision, model.OrderRevision);
        Assert.Equal(Person.PersonId, model.SelectedOrder!.Person.PersonId);
        Assert.Equal("変更名", model.SelectedOrder.Person.Name);
        Assert.True(model.Restored);
    }

    [Fact]
    public async Task ResumePermissionLossPreservesEditorAndPreventsSaving()
    {
        using var fixture = new Fixture(_ => Json(People with { CurrentRole = "Viewer", MyPersonId = null }));
        var model = fixture.Model;
        model.Apply(People);
        model.Selected = model.Rows[0];
        await model.EditCommand.ExecuteAsync(null);
        model.Name = "入力を残す";
        await model.ResumeAsync(null, default);
        Assert.Same(Person, model.Editing);
        Assert.Equal("入力を残す", model.Name);
        Assert.False(model.CanSave);
        Assert.NotNull(model.SaveError);
    }

    private static HttpResponseMessage Json(object value, HttpStatusCode status = HttpStatusCode.OK) => new(status) { Content = JsonContent.Create(value) };
    private sealed class Fixture : IDisposable
    {
        private readonly HttpClient http;
        public ManagementContext Context { get; }
        public PeopleManagementViewModel Model { get; }
        public Fixture(Func<HttpRequestMessage, HttpResponseMessage> respond)
        {
            var endpoint = ApiEndpoint.Parse("https://synthetic-api.example");
            http = new(new Handler(respond));
            var api = new MonetailApiClient(endpoint, http);
            Context = new(api, new(api, endpoint, new Store()), new(),
                new(Guid.NewGuid(), "家計簿", "Admin", "r"), Guid.NewGuid(), endpoint.Value, ExecuteAsync);
            Model = new(Context);
        }
        private async Task ExecuteAsync(Func<Task> action, Action<string>? failed)
        {
            Context.IsBusy = true;
            try { await action(); }
            catch (ApiConnectionException) { failed?.Invoke("接続できませんでした。"); }
            finally { Context.IsBusy = false; }
        }
        public void Dispose() { Model.Dispose(); Context.Dispose(); http.Dispose(); }
    }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => Task.FromResult(respond(request));
    }
    private sealed class Store : ITokenStore
    {
        private TokenSet? current = new("access", "refresh", DateTimeOffset.UtcNow.AddMinutes(10));
        public TokenSet? Load(ApiEndpoint endpoint) => current;
        public void Save(ApiEndpoint endpoint, TokenSet tokens) => current = tokens;
        public void Clear() => current = null;
    }
}
