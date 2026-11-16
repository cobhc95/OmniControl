using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;

namespace OmniControl.App.AppLists;

/// <summary>One row of the apps scope menu: its words, a muted hint at the right, a tick when it is the current choice, a "›" when it opens a page of lists.</summary>
public sealed class ScopeMenuRow
{
    public ScopeMenuRow(string label, string hint, bool isCurrent, bool opens, Action run)
    {
        Label = label;
        Hint = hint;
        IsCurrent = isCurrent;
        Opens = opens;
        Command = new RelayCommand(run);
    }

    public string Label { get; }
    public string Hint { get; }
    public bool HasHint => Hint.Length > 0;
    public bool IsCurrent { get; }
    public bool Opens { get; }
    public ICommand Command { get; }
}
