using OmniControl.App.Shared.Contracts;

namespace OmniControl.App.Features.Devices;

/// Reading devices' states on their own, so the tray tiles show real states without the page. At start-up and every so often is each device's
/// automation rule ("Check Desk lamp": when OmniControl starts or every 5 minutes, do Check Desk lamp's state), made from its auto-check choice
/// once and edited by it since; when the page or tray panel opens and after a hardware change stay here. One reading run at a time; the reading
/// itself is the service's (Scripts only, never waking an emulator). UI thread.
public sealed class DeviceAutoChecks
{
    private readonly DeviceSettings _settings;
    private readonly DeviceSetup _setup;
    private readonly Action _edited;
    private readonly Func<DeviceRecord, Task> _check;
    private readonly FeatureRulesSlot _rules;
    private Task? _probed;
    private bool _running;

    /// <param name="edited">Saves the device list after a choice changed.</param>
    /// <param name="check">One automatic read of a device (it skips what is not ready or is in its set-up).</param>
    /// <param name="rules">The automation rules the timed checks run as (empty while automation is off).</param>
    internal DeviceAutoChecks(DeviceSettings settings, DeviceSetup setup, Action edited, Func<DeviceRecord, Task> check, FeatureRulesSlot rules)
    {
        (_settings, _setup, _edited, _check, _rules) = (settings, setup, edited, check, rules);
        rules.Changed += OnRulesChanged;
    }

    /// The device's auto-check changed (a pick, the switch or its rule): the page's chips read it again.
    public event Action? Changed;

    /// The rule's name and its own action ("Check Desk lamp's state").
    public static string RuleName(DeviceRecord device) => $"Check {DeviceService.DisplayName(device)}";

    private void OnRulesChanged()
    {
        MakeRules();
        Changed?.Invoke();
    }

    /// Each device's checks become its rule the first time (from its choice, so nothing is lost); a new device's too.
    public void MakeRules()
    {
        if (_rules.Current is not { } rules) return;
        var made = false;
        foreach (var device in _settings.Devices.Where(d => !d.ChecksInRule && AutoChecks.CanAuto(d)).ToList())
        {
            var choice = AutoChecks.Of(AutoChecks.Effective(_settings, device) ?? AutoChecks.Default);
            rules.Ensure(DeviceService.CheckActionId(device), RuleName(device), Repeats(choice), Triggers(Repeats(choice) ? choice : AutoChecks.Of(AutoChecks.Default)));
            device.ChecksInRule = made = true;
        }
        if (made) _edited();
    }

    private static bool Repeats(AutoChecks.Choice choice) => choice.AtStart || choice.Every is not null;

    private static FeatureTrigger[] Triggers(AutoChecks.Choice choice) =>
        (choice.AtStart ? new FeatureTrigger[] { new FeatureTrigger.Starts() } : Array.Empty<FeatureTrigger>())
        .Concat(choice.Every is { } every ? new FeatureTrigger[] { new FeatureTrigger.Every((int)every.TotalMinutes) } : Array.Empty<FeatureTrigger>()).ToArray();

    /// The device's rule as it is now, or null (deleted, or automation off).
    public FeatureRuleView? RuleOf(DeviceRecord device) => _rules.Current?.Find(DeviceService.CheckActionId(device));

    /// The choice a device follows now: its rule's, when the rule is on (null when the rule's triggers match no choice); otherwise "when the
    /// page opens" if that was picked, else off. Null too for a device that is read by hand only (simulation only).
    public AutoCheck? Of(DeviceRecord device)
    {
        if (AutoChecks.Effective(_settings, device) is not { } picked) return null;
        if (RuleOf(device) is not { On: true } rule) return picked == AutoCheck.WhenOpens ? AutoCheck.WhenOpens : AutoCheck.Off;
        return AutoChecks.All.FirstOrDefault(c => Repeats(c) && c.AtStart == rule.AtStart && (int?)c.Every?.TotalMinutes == rule.Every)?.Value;
    }

    /// The rule's own times in a few words when they match no choice ("every 10 min").
    public string RuleWords(DeviceRecord device) => RuleOf(device) is not { } rule ? AutoChecks.ManualOnly
        : rule.Every is { } every ? $"{(rule.AtStart ? "start, " : string.Empty)}every {every} min" : rule.AtStart ? "start only" : "by its rule";

    /// Opens Automation where the device's rule is.
    public void EditRule(DeviceRecord device) => _rules.Current?.Edit(DeviceService.CheckActionId(device));

    public void Pick(DeviceRecord device, AutoCheck value)
    {
        AutoChecks.Pick(_settings, device, value);
        foreach (var each in _settings.AutoCheckSame ? _settings.Devices.ToList() : new List<DeviceRecord> { device }) Apply(each, value);
        Edited();
    }

    /// Puts a choice in the device's rule: its start and repeat triggers, on or off ("when the page opens" and Off switch it off).
    private void Apply(DeviceRecord device, AutoCheck value)
    {
        if (_rules.Current is not { } rules || !AutoChecks.CanAuto(device)) return;
        var choice = AutoChecks.Of(value);
        var id = DeviceService.CheckActionId(device);
        if (rules.Find(id) is null)
        {
            if (Repeats(choice)) rules.Ensure(id, RuleName(device), true, Triggers(choice));
            return;
        }
        if (Repeats(choice))
        {
            foreach (var trigger in Triggers(choice)) rules.SetTrigger(id, trigger);
            if (!choice.AtStart) rules.RemoveTrigger<FeatureTrigger.Starts>(id);
            if (choice.Every is null) rules.RemoveTrigger<FeatureTrigger.Every>(id);
        }
        rules.SetOn(id, Repeats(choice));
    }

    public bool OnTrayOf(DeviceRecord device) => AutoChecks.OnTray(_settings, device);

    public void PickOnTray(DeviceRecord device, bool value)
    {
        AutoChecks.PickOnTray(_settings, device, value);
        Edited();
    }

    public void SetSame(DeviceRecord device, bool same)
    {
        AutoChecks.SetSame(_settings, device, same);
        if (same && Of(device) is { } value) Pick(device, value);
        else Edited();
    }

    private void Edited()
    {
        _edited();
        Changed?.Invoke();
    }

    /// Python and python-kasa are looked for once before the first automatic read, and only when a device uses Scripts (else when the
    /// Devices page first opens).
    public Task ProbedAsync() => _probed ??= _settings.Devices.Any(AutoChecks.CanAuto) ? _setup.RefreshAsync() : Task.CompletedTask;

    /// The page opened: devices set to "When the page or tray panel opens" read their state (Scripts only).
    public async Task CheckWhenShownAsync()
    {
        foreach (var device in _settings.Devices.ToList())
            if (Of(device) is { } choice && AutoChecks.Of(choice).WhenShown) await _check(device);
    }

    /// Reads the state of every device that has an auto-check, due or not (the network may just have come back); one reading run at a time.
    public async Task CheckAllNowAsync()
    {
        if (_running) return;
        _running = true;
        try
        {
            await ProbedAsync();
            foreach (var device in _settings.Devices.ToList())
                if (AutoChecks.CanAuto(device)) await _check(device);
        }
        finally { _running = false; }
    }

    /// Smart devices was switched off: the rules are no longer followed here.
    public void Stop() => _rules.Changed -= OnRulesChanged;
}
