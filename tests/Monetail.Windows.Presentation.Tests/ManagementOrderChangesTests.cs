using System.Windows.Input;
using Monetail.Windows.Client.Ledgers;
using Monetail.Windows.Client.Transport;
using Monetail.Windows.Presentation.Workspace;
using Xunit;
using static Monetail.Windows.Presentation.Tests.ManagementTestContext;

namespace Monetail.Windows.Presentation.Tests;

public sealed class ManagementOrderChangesTests
{
    [Theory]
    [InlineData(ManagementSection.People)]
    [InlineData(ManagementSection.Categories)]
    [InlineData(ManagementSection.Payments)]
    public async Task OrderChangesCompareIdsWithTheOriginalRevisionAcrossUndoAndAuthentication(ManagementSection section)
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var owner = first;
        var people = new PeopleInfo([
            new(first, "人物1", null, null, 0, Guid.NewGuid()),
            new(second, "人物2", null, null, 1, Guid.NewGuid())], owner, "Admin", Guid.NewGuid());
        var categories = new CategoriesInfo([
            new(first, "科目1", "Expense", "Standard", null, null, 0, Guid.NewGuid()),
            new(second, "科目2", "Income", "Exempt", null, null, 1, Guid.NewGuid())], Guid.NewGuid(), "Admin");
        var payments = new PaymentMethodsInfo([
            new(first, owner, "現金", 0, null, null, Guid.NewGuid()),
            new(second, owner, "銀行", 1, null, null, Guid.NewGuid())], [new(owner, Guid.NewGuid())], owner, "Admin");
        using var fixture = new ManagementTestContext(request => request.RequestUri!.AbsolutePath.EndsWith("/people")
            ? Json(people) : request.RequestUri.AbsolutePath.EndsWith("/categories") ? Json(categories) : Json(payments));
        using var model = new ManagementViewModel(fixture.Context, () => true, (_, _) => Task.CompletedTask, () => Task.CompletedTask, "本人");
        model.SelectedSection = section;
        ICommand down;
        ICommand up;
        ICommand cancel;
        Action begin;
        Func<Task> resume;
        switch (section)
        {
            case ManagementSection.People:
                model.People.Apply(people);
                begin = () =>
                {
                    model.People.OrderCommand.Execute(null);
                    model.People.SelectedOrder = model.People.OrderRows[0];
                };
                down = model.People.MoveDownCommand;
                up = model.People.MoveUpCommand;
                cancel = model.People.CancelOrderCommand;
                resume = () => model.People.ResumeAsync(null, default);
                break;
            case ManagementSection.Categories:
                model.Categories.Apply(categories);
                begin = () =>
                {
                    model.Categories.OrderCommand.Execute(null);
                    model.Categories.SelectedOrder = model.Categories.OrderRows[0];
                };
                down = model.Categories.MoveDownCommand;
                up = model.Categories.MoveUpCommand;
                cancel = model.Categories.CancelOrderCommand;
                resume = () => model.Categories.ResumeAsync(null, default);
                break;
            default:
                await model.Payments.LoadAsync(default);
                begin = () =>
                {
                    model.Payments.OrderCommand.Execute(owner);
                    model.Payments.SelectedOrder = model.Payments.OrderRows[0];
                };
                down = model.Payments.MoveDownCommand;
                up = model.Payments.MoveUpCommand;
                cancel = model.Payments.CancelOrderCommand;
                resume = () => model.Payments.ResumeAsync(null, default);
                break;
        }
        var notifications = 0;
        model.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(model.HasOrderChanges))
                notifications++;
        };
        begin();
        Assert.False(model.HasOrderChanges);
        down.Execute(null);
        Assert.True(model.HasOrderChanges);
        Assert.True(notifications > 0);
        up.Execute(null);
        Assert.False(model.HasOrderChanges);
        down.Execute(null);
        // 認証復帰で最新マスタが同じ順へ変わっても、元の版に対する編集差分を維持する。
        people = people with { People = people.People.Select(p => p with { DisplayOrder = 1 - p.DisplayOrder }).ToArray(), OrderRevision = Guid.NewGuid() };
        categories = categories with { Categories = categories.Categories.Select(p => p with { DisplayOrder = 1 - p.DisplayOrder }).ToArray(), OrderRevision = Guid.NewGuid() };
        payments = payments with { PaymentMethods = payments.PaymentMethods.Select(p => p with { DisplayOrder = 1 - p.DisplayOrder }).ToArray() };
        await resume();
        Assert.True(model.HasOrderChanges);
        up.Execute(null);
        Assert.False(model.HasOrderChanges);
        down.Execute(null);
        Assert.True(model.HasOrderChanges);
        cancel.Execute(null);
        Assert.False(model.HasOrderChanges);
        begin();
        Assert.False(model.HasOrderChanges);
        Assert.All(fixture.Requests, request => Assert.Equal(HttpMethod.Get, request.Method));
    }
}
