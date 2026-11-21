using System.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Monetail.Windows.Presentation.Lists;

/// <summary>共通の表示枠が読む状態。行・選択・検索条件・通信を含めない。</summary>
public readonly record struct ListViewportPosition(double Horizontal, double Vertical);

public interface IRecordListState : INotifyPropertyChanged
{
    ListViewportPosition ViewPosition { get; set; }
    bool IsInitialLoading
    {
        get;
    }
    bool IsEmpty
    {
        get;
    }
    bool IsInitialFailure
    {
        get;
    }
    bool IsContinuationLoading
    {
        get;
    }
    bool IsContinuationFailure
    {
        get;
    }
    bool CanContinue
    {
        get;
    }
    bool IsFocused
    {
        get;
    }
    string ErrorText
    {
        get;
    }
    IAsyncRelayCommand RetryCommand
    {
        get;
    }
    IAsyncRelayCommand ContinueCommand
    {
        get;
    }
}
