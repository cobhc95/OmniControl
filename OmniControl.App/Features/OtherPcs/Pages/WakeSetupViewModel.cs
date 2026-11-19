using OmniControl.App.Features.OtherPcs;
using OmniControl.Core.Engine.Wake;

namespace OmniControl.App.Features.OtherPcs.Pages;

/// One row of the wake set-up window as it is drawn: a mark, its words, a muted detail, a muted note, the one pill it may have and the honest result of the last try.
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

    /// The pill: accent-outlined when the row is needed, plain when it is optional or the test.
    public bool ButtonAccent => IsNeeded;

    public bool ButtonVisible { get; }

    public string ButtonText { get; }

    public string Tip { get; }

    public RelayCommand Command { get; }
}

/// The wake set-up of one PC: what it last reported row by row, and the asks that change it. The PC is an
/// <see cref="IWakeTarget"/>: a paired PC answers through <see cref="OtherPcsService"/> behind its own prompt, this PC is read and fixed here behind
/// Windows' prompt; this only shows the answers, honestly, per row. While a paired PC is off the window shows its last report and offers nothing.
public sealed partial class WakeSetupViewModel : ObservableObject, IDisposable
{
    private readonly IWakeTarget _target;
    private readonly Dictionary<string, string> _results = new();
    private CancellationTokenSource? _stop;

    public WakeSetupViewModel(IWakeTarget target)
    {
        _target = target;
        target.Changed += Refresh;
        Refresh();
        if (target.ReadOnOpen)
        {
            if (target.Facts is null) CheckAgain(); // never read: wait for it
            else _ = Guard.RunAsync("Other PCs: wake check", () => _target.CheckAsync(CancellationToken.None)); // the last answer shows at once; this refreshes it
        }
    }

    public ObservableCollection<WakeRowItem> Rows { get; } = new();

    public string Name => _target.Name;

    public string Heading => $"Set up wake · {Name}";

    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsOffline))] private bool _isOnline;

    public bool IsOffline => !IsOnline;

    /// Why nothing can be changed now: it says how the PC went away (one sentence, also the rows' tooltip).
    public string OfflineNote => _target.OfflineNote;

    /// The one thin line about what is happening or how the last ask ended.
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasStatus))] private string _status = string.Empty;

    public bool HasStatus => Status.Length > 0;

    [ObservableProperty] private bool _busy;

    /// OmniControl changed something there that "Undo my changes" can put back.
    public bool CanUndo => _target.Facts?.CanUndo == true;

    private void Refresh()
    {
        IsOnline = _target.IsOnline;
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Heading));
        OnPropertyChanged(nameof(OfflineNote));
        OnPropertyChanged(nameof(CanUndo));
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
        var stop = _stop!.Token;
        try
        {
            var reply = await _target.CheckAsync(stop);
            End(stop.IsCancellationRequested ? "Stopped." : reply.Ok ? string.Empty : reply.Why);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            End("The check could not finish."); // the waiting never outlives a failure
            throw;
        }
    }

    private async Task FixAsync(IReadOnlyList<WakeFix> fixes)
    {
        if (fixes.Count == 0)
        {
            Status = "Nothing here needs fixing.";
            return;
        }
        if (!Begin(_target.IsOwn ? "Fixing…" : $"Waiting for {Name} to allow it…")) return;
        var stop = _stop!.Token;
        try
        {
            var reply = await _target.FixAsync(fixes, stop);
            if (stop.IsCancellationRequested)
            {
                Status = "Stopped. Putting back anything changed…";
                reply = await _target.UndoAsync();
                End(reply.Ok || !_target.IsOnline ? string.Empty : reply.Why);
                return;
            }
            foreach (var row in WakeSetupRows.Of(_target.Facts, Name, _target.CanTest).Where(r => r.Fix is { } fix && fixes.Contains(fix) && r.Kind != WakeRowKind.Done))
                _results[row.Id] = reply.Ok ? "It still reads as off after the change." : reply.Why;
            End(reply.Ok ? string.Empty : reply.Why);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            End("The change could not finish.");
            throw;
        }
    }

    [RelayCommand]
    private void Undo() => _ = Guard.RunAsync("Other PCs: wake undo", UndoAsync);

    private async Task UndoAsync()
    {
        if (!Begin(_target.IsOwn ? "Putting back…" : $"Waiting for {Name} to allow it…")) return;
        try
        {
            var reply = await _target.UndoAsync();
            End(reply.Ok ? "Put back." : reply.Why);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            End("The undo could not finish.");
            throw;
        }
    }

    /// Stops what is being checked or fixed; anything half-done is put back.
    [RelayCommand]
    private void Stop() => _stop?.Cancel();

    private async Task TestAsync()
    {
        if (!Begin($"Waiting for {Name} to allow the test…")) return;
        var words = await _target.TestAsync();
        _results[WakeSetupRows.TestId] = words;
        End(string.Empty);
    }

    /// Starts an ask: false while another one runs (the buttons are never disabled, a second press is simply ignored).
    private bool Begin(string words)
    {
        if (Busy || !IsOnline) return false;
        Busy = true;
        _stop = new CancellationTokenSource();
        _results.Clear();
        Status = words;
        return true;
    }

    private void End(string words)
    {
        _stop?.Dispose();
        _stop = null;
        Busy = false;
        Status = words;
        Refresh();
    }

    public void Dispose()
    {
        _target.Changed -= Refresh;
        _stop?.Cancel();
    }
}
