namespace Monetail.Api.Features.Monetail.Ledgers;

/// <summary>家計簿の名前・作成者・更新版を保存する。</summary>
public sealed class Ledger
{
    /// <summary>家計簿名の最大文字数を定める。</summary>
    public const int NameMaxLength = 100;
    /// <summary>家計簿の識別子を保持する。</summary>
    public string Id { get; set; } = null!;
    /// <summary>前後の空白を除いた家計簿名を保持する。</summary>
    public string Name { get; set; } = null!;
    /// <summary>作成したアカウントの識別子を保持する。</summary>
    public string CreatedByAuthUserId { get; set; } = null!;
    /// <summary>作成日時をUTCで保持する。</summary>
    public DateTime CreatedAtUtc { get; set; }
    /// <summary>家計簿の更新版を保持する。</summary>
    public string Revision { get; set; } = null!;
    /// <summary>人物の構成と共通順の更新版を保持する。</summary>
    public string PeopleOrderRevision { get; set; } = Guid.NewGuid().ToString("N");
    /// <summary>科目の構成と共通順の更新版。家計簿名・人物順とは独立して照合する。</summary>
    public string CategoryOrderRevision { get; set; } = Guid.NewGuid().ToString("N");
}

/// <summary>家計簿への参加と家計簿内の役割を保存する。</summary>
public sealed class LedgerMember
{
    /// <summary>参加する家計簿の識別子を保持する。</summary>
    public string LedgerId { get; set; } = null!;
    /// <summary>参加するアカウントの識別子を保持する。</summary>
    public string AuthUserId { get; set; } = null!;
    /// <summary>Viewer・Editor・Adminの役割を保持する。</summary>
    public string Role { get; set; } = null!;
    /// <summary>参加日時をUTCで保持する。</summary>
    public DateTime JoinedAtUtc { get; set; }
    /// <summary>参加状態の更新版を保持する。</summary>
    public string Revision { get; set; } = null!;
    /// <summary>同じ家計簿の人物との任意の対応を保持する。</summary>
    public string? PersonId { get; set; }
}
