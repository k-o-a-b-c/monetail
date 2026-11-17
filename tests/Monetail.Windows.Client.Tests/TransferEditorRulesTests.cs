using Monetail.Windows.Client.Ledgers;
using Monetail.Windows.Client.Transfers;
using Monetail.Windows.Client.Transport;
using Xunit;

namespace Monetail.Windows.Client.Tests;

public sealed class TransferEditorRulesTests
{
    [Theory]
    [InlineData("Admin", true, true, true)] [InlineData("Editor", true, true, false)] [InlineData("Editor", false, false, false)] [InlineData("Viewer", true, false, false)]
    public void PermissionPreviewDistinguishesBothOwnersAndUnmappedEditor(string role, bool mapped, bool mineAllowed, bool otherAllowed)
    {
        var mine = Guid.NewGuid(); var other = Guid.NewGuid(); var from = Guid.NewGuid(); var to = Guid.NewGuid();
        var methods = new PaymentMethodsInfo([new(from, mine, "現金", 0, null, null, Guid.NewGuid()), new(to, other, "銀行", 0, null, null, Guid.NewGuid())], [], mapped ? mine : null, role);
        Assert.Equal(mineAllowed, TransferEditorRules.CanCreate(methods)); Assert.Equal(mineAllowed, TransferEditorRules.CanUse(methods, from)); Assert.Equal(otherAllowed, TransferEditorRules.CanUse(methods, to));
        Assert.Equal(mineAllowed, TransferEditorRules.CanUseOwner(methods, mine)); Assert.Equal(otherAllowed, TransferEditorRules.CanUseOwner(methods, other));
        Assert.False(TransferEditorRules.CanUse(methods, Guid.NewGuid()));
    }
    [Theory]
    [InlineData(-1, false)] [InlineData(0, true)] [InlineData(1, true)] [InlineData(2, false)]
    public void MasterPeriodsIncludeBothBoundariesAndNeverRewriteDraft(int offset, bool valid)
    {
        var day = new DateOnly(2026, 11, 1); var owner = Guid.NewGuid(); var from = Guid.NewGuid(); var to = Guid.NewGuid();
        var people = new PeopleInfo([new(owner, "本人", day, day.AddDays(1), 0, Guid.NewGuid())], owner, "Admin", Guid.NewGuid());
        var methods = new PaymentMethodsInfo([new(from, owner, "現金", 0, day, day.AddDays(1), Guid.NewGuid()), new(to, owner, "銀行", 1, null, null, Guid.NewGuid())], [], owner, "Admin");
        var editor = TransferEditorState.Create(day.AddDays(offset)); editor.Draft.Name = "元表記"; editor.Draft.SetAmount(10); editor.SelectFrom(from); editor.SelectTo(to);
        var before = editor.PrepareSave(TransferSaveKind.Register);
        if (valid) TransferEditorRules.Validate(before.Snapshot, people, methods);
        else Assert.Equal("fromPaymentMethodId", Assert.Throws<TransferInputException>(() => TransferEditorRules.Validate(before.Snapshot, people, methods)).Field);
        Assert.Equal(before, editor.PrepareSave(TransferSaveKind.Register));
    }
}
