using Monetail.Windows.Client.Input;
using Monetail.Windows.Client.Transport;

namespace Monetail.WinUI;

/// <summary>既存Clientの人物情報を表示へ変換する。保存や通信は行わない。</summary>
public sealed class PersonRow(PersonInfo person, bool isMe)
{
    public PersonInfo Person { get; } = person;
    public string Name { get; } = person.Name + (isMe ? "（本人）" : "");
    public string ValidFrom { get; } = DateTimeInput.FormatDate(person.ValidFrom);
    public string ValidTo { get; } = DateTimeInput.FormatDate(person.ValidTo);
}
