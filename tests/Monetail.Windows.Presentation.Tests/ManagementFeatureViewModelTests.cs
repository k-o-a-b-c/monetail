using System.Net;
using Monetail.Windows.Client.Ledgers;
using Monetail.Windows.Client.Transport;
using Monetail.Windows.Presentation.Management;
using Xunit;
using static Monetail.Windows.Presentation.Tests.ManagementTestContext;

namespace Monetail.Windows.Presentation.Tests;

public sealed class ManagementFeatureViewModelTests
{
    private static readonly CategoryInfo Category = new(Guid.NewGuid(), "食品", "Expense", "ReducedFood", null, null, 0, Guid.NewGuid());
    private static readonly CategoriesInfo Categories = new([Category], Guid.NewGuid(), "Admin");

    [Fact]
    public async Task CategoryDefaultsValidationConflictAndExplicitLatestKeepClientContracts()
    {
        var latest = Category with { Name = "最新科目", IncomeExpense = "Income", TaxCategoryCode = "Exempt", Revision = Guid.NewGuid() };
        using var fixture = new ManagementTestContext(_ => Json(new { code = "revision_conflict", latest }, HttpStatusCode.Conflict));
        using var model = new CategoriesManagementViewModel(fixture.Context);
        model.Apply(Categories);
        await model.AddCommand.ExecuteAsync(null);
        Assert.Equal("Expense", model.Income);
        Assert.Equal("Standard", model.Tax);
        Assert.Equal("", model.FromText);
        model.Name = "  ";
        Assert.False(await model.SaveEditorAsync(null, null));
        Assert.Equal("  ", model.Name);
        Assert.Empty(fixture.Requests);
        model.CloseEditor();
        model.Selected = model.Rows[0];
        await model.EditCommand.ExecuteAsync(null);
        model.Name = "未保存";
        Assert.False(await model.SaveEditorAsync(null, null));
        Assert.Equal("未保存", model.Name);
        Assert.Same(Category, model.Editing);
        Assert.Equal(latest, model.Latest);
        Assert.False(model.CanSave);
        model.AdoptLatestCommand.Execute(null);
        Assert.Equal(latest, model.Editing);
        Assert.Equal("Income", model.Income);
        Assert.Equal("Exempt", model.Tax);
        Assert.Single(fixture.Requests);
    }

    [Fact]
    public async Task CategoryResumeKeepsRawInputAndOriginalRevision()
    {
        using var fixture = new ManagementTestContext(_ => Json(Categories with { Categories = [Category with { Name = "別の保存値" }] }));
        using var model = new CategoriesManagementViewModel(fixture.Context);
        model.Apply(Categories);
        model.Selected = model.Rows[0];
        await model.EditCommand.ExecuteAsync(null);
        model.Name = "未保存";
        model.FromText = "2026/";
        await model.ResumeAsync(null, default);
        Assert.Same(Category, model.Editing);
        Assert.Equal("未保存", model.Name);
        Assert.Equal("2026/", model.FromText);
        Assert.True(model.CanSave);
        Assert.Single(fixture.Requests);
        Assert.Equal(HttpMethod.Get, fixture.Requests[0].Method);
    }

    [Theory]
    [InlineData("Admin", true)]
    [InlineData("Editor", false)]
    [InlineData("Viewer", false)]
    public void CategoryRoleAndOrderCommandsAreIndependentFromSelection(string role, bool writable)
    {
        using var fixture = new ManagementTestContext(_ => throw new InvalidOperationException());
        using var model = new CategoriesManagementViewModel(fixture.Context);
        model.Apply(Categories with { CurrentRole = role });
        Assert.Equal(writable, model.AddCommand.CanExecute(null));
        model.Selected = model.Rows[0];
        Assert.Equal(writable, model.EditCommand.CanExecute(null));
        Assert.Equal(writable, model.OrderCommand.CanExecute(null));
        fixture.Context.IsBusy = true;
        Assert.False(model.AddCommand.CanExecute(null));
    }

    [Fact]
    public async Task PaymentOwnerSelectionCreationRetryAndGroupPermissionsArePreserved()
    {
        var mine = Guid.NewGuid();
        var other = Guid.NewGuid();
        var person = new PersonInfo(mine, "本人", null, null, 0, Guid.NewGuid());
        var method = new PaymentMethodInfo(Guid.NewGuid(), mine, "現金", 0, null, null, Guid.NewGuid());
        var state = new PaymentMethodsInfo([method], [new(mine, Guid.NewGuid()), new(other, Guid.NewGuid())], mine, "Admin");
        var calls = 0;
        using var fixture = new ManagementTestContext(request => request.Method == HttpMethod.Get
            ? request.RequestUri!.AbsolutePath.EndsWith("/people") ? Json(new PeopleInfo([person], mine, "Admin", Guid.NewGuid())) : Json(state)
            : ++calls == 1 ? new(HttpStatusCode.ServiceUnavailable) : Json(new { paymentMethodId = method.PaymentMethodId, state.PaymentMethods, state.OwnerOrderRevisions, state.MyPersonId, state.CurrentRole }));
        using var model = new PaymentMethodsManagementViewModel(fixture.Context);
        await model.LoadAsync(default);
        Assert.Single(model.Groups);
        Assert.True(model.Groups[0].CanOrder);
        await model.AddCommand.ExecuteAsync(null);
        Assert.Null(model.ChosenOwner);
        Assert.False(model.CanSave);
        model.SelectedOwner = model.Owners[0];
        model.Name = "新しい現金";
        Assert.False(await model.SaveEditorAsync(null, null));
        var creation = model.Creation;
        Assert.NotNull(creation);
        Assert.True(await model.SaveEditorAsync(null, null));
        Assert.Same(creation, model.Creation);
        Assert.Equal(fixture.Requests[^2].Body, fixture.Requests[^1].Body);
        model.CloseEditor();
        model.Apply(state with { CurrentRole = "Editor" });
        Assert.True(model.OrderCommand.CanExecute(mine));
        Assert.False(model.OrderCommand.CanExecute(other));
    }

    [Fact]
    public async Task ParticipantConflictAndExplicitAdoptionDoNotFetchOrOverwriteRawChoice()
    {
        var member = new MemberInfo(Guid.NewGuid(), "参加者", "Viewer", null, null, null, DateTimeOffset.UtcNow, Guid.NewGuid());
        var choice = new MemberPersonCandidate(Guid.NewGuid(), "人物", true);
        var edit = new MemberEditInfo(member, [choice]);
        var latest = new MemberEditInfo(member with { Role = "Editor", Revision = Guid.NewGuid() }, [choice]);
        using var fixture = new ManagementTestContext(request => request.Method == HttpMethod.Get ? Json(edit)
            : Json(new { code = "revision_conflict", latest }, HttpStatusCode.Conflict));
        using var model = new ParticipantsManagementViewModel(fixture.Context);
        model.Apply(new([member], fixture.Context.AccountId, "Admin"));
        await model.EditCommand.ExecuteAsync(member);
        model.Role = "Admin";
        model.SelectedPerson = model.PersonChoices[1];
        Assert.False(await model.SaveEditorAsync());
        Assert.Equal("Admin", model.Role);
        Assert.Equal(choice.PersonId, model.SelectedPerson.PersonId);
        Assert.Equal(member.Revision, model.Editing!.Member.Revision);
        Assert.Equal(latest.Member, model.Latest!.Member);
        Assert.Equal(latest.PersonCandidates, model.Latest.PersonCandidates);
        Assert.False(model.CanSave);
        var adopted = model.Latest;
        model.AdoptLatestCommand.Execute(null);
        Assert.Same(adopted, model.Editing);
        Assert.Equal("Editor", model.Role);
        Assert.Equal(2, fixture.Requests.Count);
    }

    [Fact]
    public async Task InvitationUnknownResultRetainsDraftAcrossNormalPageLeaveAndDoesNotRetryAutomatically()
    {
        using var fixture = new ManagementTestContext(_ => new(HttpStatusCode.ServiceUnavailable));
        using var model = new ParticipantsManagementViewModel(fixture.Context);
        await model.IssueCommand.ExecuteAsync(null);
        var draft = model.InvitationDraft;
        Assert.NotNull(draft);
        Assert.Single(fixture.Requests);
        model.Leave();
        Assert.Same(draft, model.InvitationDraft);
        Assert.Single(fixture.Requests);
        await model.IssueCommand.ExecuteAsync(null);
        Assert.Equal(fixture.Requests[0].Body, fixture.Requests[1].Body);
    }
}
