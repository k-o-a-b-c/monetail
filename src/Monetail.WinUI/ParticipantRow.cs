using Microsoft.UI.Xaml;
using Monetail.Windows.Client.Transport;

namespace Monetail.WinUI;

/// <summary>APIの順序と対象IDを保ち、識別用の内部IDを画面に出さない。</summary>
public sealed class ParticipantRow(MemberInfo member, bool isMe)
{
    public MemberInfo Member { get; } = member;
    public string Name { get; } = (member.DisplayName ?? "表示名未設定") + (isMe ? "（本人）" : "");
    public string Role { get; } = RoleName(member.Role);
    public string Person { get; } = member.PersonName ?? "未設定";
    public Visibility ExcludeVisibility { get; } = isMe ? Visibility.Collapsed : Visibility.Visible;
    internal static string RoleName(string role) => role switch { "Admin" => "管理者", "Editor" => "編集者", _ => "閲覧者" };
}

public sealed class LedgerInvitationRow(LedgerInvitationInfo invitation)
{
    public LedgerInvitationInfo Invitation { get; } = invitation;
    public string Expires { get; } = invitation.ExpiresAtUtc.ToLocalTime().ToString("yyyy/MM/dd HH:mm");
}
