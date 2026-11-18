using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OmniControl.App.OtherPcs;
using OmniControl.App.Services;
using OmniControl.Core.Engine.Wake;

namespace OmniControl.App.Pages.OtherPcs;

/// <summary>One row of the wake set-up window as it is drawn (docs/mockups/set-up-wake.html): a mark, its words, a muted detail, a muted note, the one pill it may have and the honest result of the last try.</summary>
public sealed class WakeRowItem
{
    public WakeRowItem(WakeRowSpec spec, string result, bool online, string offlineTip, RelayCommand command)
    {
        Spec = spec;
        Result = result;
        Command = command;
        Tip = online ? string.Empty : offlineTip;
        var offersFix = spec.Fix is not null && spec.Kind is WakeRowKind.Needed or WakeRowKind.Optional;
        ButtonVisible = online && (offersFix || spec.Kind == WakeRowKind.Test);
        ButtonText = spec.Kind == WakeRowKind.Test ? "Start test" : "Fix";
    }

    public WakeRowSpec Spec { get; }

    public string Label => Spec.Label;

    public string Detail => Spec.Detail;

    public bool HasDetail => Spec.Detail.Length > 0;

    public string Note => Spec.Note;

    public bool HasNote => Spec.Note.Length > 0;

    public string Result { get; }

    public bool HasResult => Result.Length > 0;

    public bool IsDone => Spec.Kind == WakeRowKind.Done;

    public bool IsNeeded => Spec.Kind == WakeRowKind.Needed;

    public bool IsOptional => Spec.Kind == WakeRowKind.Optional;

    public bool IsYou => Spec.Kind == WakeRowKind.You;

    public bool IsTest => Spec.Kind == WakeRowKind.Test;

    /// <summary>The pill: accent-outlined when the row is needed, plain when it is optional or the test.</summary>
    public bool ButtonAccent => IsNeeded;

    public bool ButtonVisible { get; }

    public string ButtonText { get; }

    public string Tip { get; }

    public RelayCommand Command { get; }
}

/// <summary>
/// The wake set-up of one PC (docs/mockups/set-up-wake.html, this-pc-wake.html): what it last reported row by row, and the asks that change it. The PC is an
/// <see cref="IWakeTarget"/>: a paired PC answers through <see cref="OtherPcsService"/> behind its own prompt, this PC is read and fixed here behind
/// Windows' prompt; this only shows the answers, honestly, per row. While a paired PC is off the window shows its last report and offers nothing.
/// </summary>
public sealed partial class WakeSetupViewModel : ObservableObject, IDisposable
{
    private readonly IWakeTarget _target;
    private readonly Dictionary<string, string> _results = new();

    public WakeSetupViewModel(IWakeTarget target)
    {
        _target = target;
        target.Changed += Refresh;
        Refresh();
        if (target.ReadOnOpen) CheckAgain();
    }

    public ObservableCollection<WakeRowItem> Rows { get; } = new();

    public string Name => _target.Name;

    public string Heading => $"Set up wake · {Name}";

    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsOffline))] private bool _isOnline;

    public bool IsOffline => !IsOnline;

    /// <summary>Why nothing can be changed now: it says how the PC went away (one sentence, also the rows' tooltip).</summary>
    public string OfflineNote => _target.OfflineNote;

    /// <summary>The one thin line about what is happening or how the last ask ended.</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasStatus))] private string _status = string.Empty;

    public bool HasStatus => Status.Length > 0;

    [ObservableProperty] private bool _busy;

    private void Refresh()
    {
        IsOnline = _target.IsOnline;
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Heading));
        OnPropertyChanged(nameof(OfflineNote));
        Rows.Clear();
        foreach (var spec in WakeSetupRows.Of(_target.Facts, Name, _target.CanTest))
        {
            var row = spec;
            var command = new RelayCommand(() => Run(row));
            Rows.Add(new WakeRowItem(spec, _results.GetValueOrDefault(spec.Id, string.Empty), IsOnline, OfflineNote, command));
        }
    }

    private void Run(WakeRowSpec row)
    {
        if (row.Id == WakeSetupRows.TestId) _ = Guard.RunAsync("Other PCs: wake test", TestAsync);
        else if (row.Fix is { } fix) _ = Guard.RunAsync("Other PCs: wake fix", () => FixAsync(new[] { fix }));
    }

    [RelayCommand]
    private void FixAll() => _ = Guard.RunAsync("Other PCs: wake fix", () => FixAsync(WakeSetupRows.Required(Rows.Select(r => r.Spec))));

    [RelayCommand]
    private void CheckAgain() => _ = Guard.RunAsync("Other PCs: wake check", CheckAsync);

    private async Task CheckAsync()
    {
        if (!Begin(_target.IsOwn ? "Checking…" : $"Asking {Name} to read its network card again…")) return;
        var reply = await _target.CheckAsync();
        End(reply.Ok ? string.Empty : reply.Why);
    }

    private async Task FixAsync(IReadOnlyList<WakeFix> fixes)
    {
        if (fixes.Count == 0)
        {
            Status = "Nothing here needs fixing.";
            return;
        }
        if (!Begin(_target.IsOwn ? "Waiting for your OK…" : $"Waiting for {Name} to allow it…")) return;
        var reply = await _target.FixAsync(fixes);
        foreach (var row in WakeSetupRows.Of(_target.Facts, Name, _target.CanTest).Where(r => r.Fix is { } fix && fixes.Contains(fix) && r.Kind != WakeRowKind.Done))
            _results[row.Id] = reply.Ok ? "It still reads as off after the change." : reply.Why;
        End(reply.Ok ? string.Empty : reply.Why);
    }

    private async Task TestAsync()
    {
        if (!Begin($"Waiting for {Name} to allow the test…")) return;
        var words = await _target.TestAsync();
        _results[WakeSetupRows.TestId] = words;
        End(string.Empty);
    }

    /// <summary>Starts an ask: false while another one runs (the buttons are never disabled, a second press is simply ignored).</summary>
    private bool Begin(string words)
    {
        if (Busy || !IsOnline) return false;
        Busy = true;
        _results.Clear();
        Status = words;
        return true;
    }

    private void End(string words)
    {
        Busy = false;
        Status = words;
        Refresh();
    }

    public void Dispose() => _target.Changed -= Refresh;
}
