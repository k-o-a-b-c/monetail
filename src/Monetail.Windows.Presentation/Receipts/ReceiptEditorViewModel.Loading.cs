using Monetail.Windows.Client.Ledgers;
using Monetail.Windows.Client.Receipts;
using Monetail.Windows.Client.Transport;
using Monetail.Windows.Presentation.Records;

namespace Monetail.Windows.Presentation.Receipts;

public sealed partial class ReceiptEditorViewModel
{
    // 未確定の日時欄はHeaderの最後の有効値で代用しない。生文字列はViewのまま保持する。
    public DateOnly? ConfirmedDate
    {
        get; private set;
    }
    public static async Task<ReceiptEditorViewModel> OpenAsync(RecordEditorContext context, Guid? id, CancellationToken token)
    {
        var initial = new ReceiptHeaderDraft
        {
            TransactionAt = DateTime.Today,
            ShopLocation = "",
            Memo = null,
            TaxInclusionMode = 0,
            TaxRoundingMode = "floor"
        };
        var ledger = context.LedgerId;
        var info = id is Guid receiptId ? await new ReceiptsWorkspace(context.Api, context.Session, ledger).GetAsync(receiptId, token) : null;
        await context.Masters.GetPeopleAsync(ledger, ct => new PeopleWorkspace(context.Api, context.Session, ledger).ListAsync(ct), token);
        await context.Masters.GetPaymentsAsync(ledger, ct => new PaymentMethodsWorkspace(context.Api, context.Session, ledger).ListAsync(ct), token);
        await context.Masters.GetCategoriesAsync(ledger, ct => new CategoriesWorkspace(context.Api, context.Session, ledger).ListAsync(ct), token);
        var taxes = await new TaxRatesWorkspace(context.Api, context.Session, ledger).ListAsync(token);
        token.ThrowIfCancellationRequested();
        ReceiptEditorViewModel? model = null;
        ReceiptCategoryDefaults Defaults(Guid category, DateTime? date)
        {
            var master = context.Masters.Categories(ledger)?.Categories.FirstOrDefault(c => c.CategoryId == category)
                ?? throw new ReceiptInputException("categoryId", "科目情報を取得できませんでした。");
            return new(master.IncomeExpense, model?.TaxHistory is { } history
                ? ReceiptTaxRules.Initial(history, master.TaxCategoryCode, model.ConfirmedDate) : ReceiptTaxSelection.Unselected);
        }
        var editor = info is null ? ReceiptEditorState.Create(initial, Defaults) : ReceiptEditorState.Edit(info, Defaults);
        model = new(editor, static () => default,
            () => (context.Masters.People(ledger), context.Masters.Payments(ledger), context.Masters.Categories(ledger), context.Masters.MyPersonId(ledger)),
            paymentId => context.Masters.PaymentName(ledger, paymentId))
        {
            TaxHistory = taxes,
            Context = context
        };
        return model;
    }
}
