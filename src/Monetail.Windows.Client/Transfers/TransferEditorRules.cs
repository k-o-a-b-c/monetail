using Monetail.Windows.Client.Ledgers;
using Monetail.Windows.Client.Transport;

namespace Monetail.Windows.Client.Transfers;

/// <summary>画面用の既存マスタ判定。保存確定時の最終検証はAPIが行う。</summary>
public static class TransferEditorRules
{
    public static bool CanCreate(PaymentMethodsInfo methods) => methods.CurrentRole == "Admin" || methods.CurrentRole == "Editor" && methods.MyPersonId is not null;
    public static bool CanUse(PaymentMethodsInfo methods, Guid paymentId) => methods.PaymentMethods.FirstOrDefault(p => p.PaymentMethodId == paymentId) is { } payment &&
        (methods.CurrentRole == "Admin" || methods.CurrentRole == "Editor" && methods.MyPersonId == payment.OwnerPersonId);
    public static bool IsActive(DateOnly day, DateOnly? from, DateOnly? to) => (from is null || from <= day) && (to is null || day <= to);
    public static void Validate(TransferSnapshot snapshot, PeopleInfo people, PaymentMethodsInfo methods)
    {
        var day = DateOnly.FromDateTime(snapshot.TransactionAt);
        void Check(Guid id, string field)
        {
            var payment = methods.PaymentMethods.FirstOrDefault(p => p.PaymentMethodId == id);
            var person = people.People.FirstOrDefault(p => p.PersonId == payment?.OwnerPersonId);
            if (payment is null || person is null) throw new TransferInputException(field, "支払方法と所有人物を確認してください。");
            if (!CanUse(methods, id)) throw new TransferInputException(field, "使用できる支払方法を選択してください。");
            if (!IsActive(day, payment.ValidFrom, payment.ValidTo) || !IsActive(day, person.ValidFrom, person.ValidTo))
                throw new TransferInputException(field, "取引日に有効な支払方法と人物を選択してください。");
        }
        Check(snapshot.FromPaymentMethodId, "fromPaymentMethodId"); Check(snapshot.ToPaymentMethodId, "toPaymentMethodId");
    }
    /// <summary>保存後だけの条件調整。英数字以外は変換しない（APIの振替名検索契約）。</summary>
    public static bool NameMatches(string name, string query) => SearchText(name).Contains(SearchText(query), StringComparison.Ordinal);
    private static string SearchText(string text)
    {
        var characters = text.ToCharArray();
        for (var i = 0; i < characters.Length; i++)
        {
            var value = characters[i];
            if (value is >= '\uff10' and <= '\uff19' or >= '\uff21' and <= '\uff3a' or >= '\uff41' and <= '\uff5a') value = (char)(value - 0xfee0);
            if (value is >= 'a' and <= 'z') value = (char)(value - ('a' - 'A'));
            characters[i] = value;
        }
        return new(characters);
    }
}
