using OmniControl.App.Features.Input.Mouse;
using OmniControl.Core.Engine.Pointer;

namespace OmniControl.App.Features.Input.Pages.Mouse;

/// The Pointer settings group: one card per entry of <see cref="PointerControls"/> (Windows' own mouse settings, changed live), two
/// cards that open Windows' pages for what can't be set reliably from here, an inner group with one card per pointer of the scheme,
/// and at the bottom "Reset to Windows defaults" and "Restore my original values". Every card shows what Windows has now: they are
/// read again when the page opens or comes back on screen, after OmniControl's own changes, and after a write Windows refused.
public sealed partial class MousePageViewModel
{
    private const string CustomScheme = "Custom";
    private const string WindowsPageButton = "Open";
    private static readonly FileKind PointerFiles = new("Choose a pointer", "Pointers", CursorSchemes.FilePatterns);

    private readonly PointerSettingsService _pointer;
    private readonly PointerActions _pointerActions;
    private readonly Action<string> _report;
    private readonly Dictionary<PointerValue, SliderItem> _pointerSliders = new();
    private readonly Dictionary<PointerValue, ToggleItem> _pointerSwitches = new();
    private readonly List<(CursorRole Role, FileItem Card)> _pointerFiles = new();
    private ChoiceItem _schemeCard = null!;   // made by BuildPointerGroup, from the constructor
    private ButtonItem _restoreCard = null!;
    private IReadOnlyList<CursorScheme> _schemes = Array.Empty<CursorScheme>();
    private readonly List<(object Card, string Keywords)> _pointerSearch = new();
    private const string PointerSizeLabel = "Pointer size and colour", ScrollInactiveLabel = "Scroll inactive windows",
        RestoreOriginalLabel = "Restore my original values", ResetDefaultsLabel = "Reset to Windows defaults";
    private const string PointerMoreKeywords = "pointer shape scheme file size colour color scroll inactive windows reset restore defaults";

    /// The Pointer settings cards, with the pointers of the scheme as an inner group.
    public ParentGroup PointerGroup { get; private set; } = null!;

    /// The two cards under the pointers: reset to Windows' defaults, restore the user's own values.
    public IReadOnlyList<object> PointerFooter { get; private set; } = Array.Empty<object>();

    private void BuildPointerGroup()
    {
        foreach (var control in PointerControls.All) _pointerSearch.Add((CardFor(control), control.Keywords));
        var cards = _pointerSearch.Select(e => e.Card).ToList();
        cards.Add(new ButtonItem(PointerSizeLabel, WindowsPageButton,
            "Set in Windows' Accessibility settings.",
            () => OpenWindowsPage(PointerSettings.WindowsPointerLookPage)) { Half = true });
        cards.Add(new ButtonItem(ScrollInactiveLabel, WindowsPageButton,
            "Set in Windows' mouse settings.",
            () => OpenWindowsPage(PointerSettings.WindowsMousePage)) { Half = true });

        foreach (var role in CursorRoles.All)
            _pointerFiles.Add((role, new FileItem(role.Name, PointerFiles, path => ChoosePointer(role, path), "Default", () => SetPointer(role, null))
                { Half = true, EmptyText = "Built-in pointer" }));
        var shapes = new ItemGroup("Mouse/Pointer/Shapes", "Pointers", _pointerFiles.Select(f => (object)f.Card).ToList());

        _restoreCard = new ButtonItem(RestoreOriginalLabel, "Restore",
            "Back to before OmniControl changed them.", _pointerActions.RestoreOriginal) { Half = true };
        PointerFooter = new object[]
        {
            new ButtonItem(ResetDefaultsLabel, "Reset", "Back to how Windows starts.", _pointerActions.ResetToDefaults) { Half = true },
            _restoreCard,
        };

        _pointerSearch.AddRange(cards.Skip(PointerControls.All.Count).Concat(_pointerFiles.Select(f => (object)f.Card)).Concat(PointerFooter)
            .Select(card => (card, PointerMoreKeywords)));

        PointerGroup = new ParentGroup("Mouse/Pointer", PointerControls.GroupName, $"{cards.Count} settings", cards)
        {
            Tip = "Windows' own mouse settings: they change at once, stay after a restart and show what Windows has now. Your values from before OmniControl's "
                  + "first change are kept, so Restore can put them all back. Swapping the buttons asks to be kept and goes back by itself if you don't.",
        };
        PointerGroup.Subgroups.Add(shapes);

        _followed.Follow<Action>(h => _pointerActions.Changed += h, h => _pointerActions.Changed -= h, LoadPointerSettings);
        _followed.Follow<Action>(h => _pointer.SwapChanged += h, h => _pointer.SwapChanged -= h, LoadSwap);
        LoadPointerSettings();
    }

    private object CardFor(PointerControl control)
    {
        switch (control.Kind)
        {
            case PointerKind.Number:
                var range = control.Range;
                var slider = new SliderItem(control.Label, range.Default, v => SetPointerValue(control, v), _report,
                    range.Min, range.Max, control.Unit, range.Default) { Half = true };
                _pointerSliders[control.Value!.Value] = slider;
                return slider;
            case PointerKind.Switch:
                var toggle = new ToggleItem(control.Label, false, on => OnPointerSwitch(control, on)) { Half = true };
                _pointerSwitches[control.Value!.Value] = toggle;
                return toggle;
            default:
                _schemeCard = new ChoiceItem(control.Label, Array.Empty<string>(), string.Empty, OnSchemePicked) { Half = true };
                return _schemeCard;
        }
    }

    /// Reads every setting and pointer from Windows and shows it, without writing anything.
    private void LoadPointerSettings()
    {
        foreach (var control in PointerControls.All.Where(c => c.Value is not null))
        {
            var value = control.Value!.Value;
            var now = _pointer.Read(value);
            if (_pointerSliders.TryGetValue(value, out var slider))
            {
                if (now is { } n) { slider.Load(n); slider.Availability.MakeAvailable(); }
                else slider.Availability.MakeUnavailable("Windows didn't say.");
            }
            else if (now is { } on) _pointerSwitches[value].Load(on == 1);
        }
        UpdatePointerNeeds();
        LoadPointers();
        UpdateRestoreCard();
    }

    /// The scheme in force (or "Custom") and each pointer's file.
    private void LoadPointers()
    {
        _schemes = _pointer.Schemes();
        var current = _pointer.CurrentScheme(_schemes);
        var options = _schemes.Select(s => s.Label).ToList();
        if (current is null) options.Insert(0, CustomScheme);
        _schemeCard.Load(options, current?.Label ?? CustomScheme);
        foreach (var (role, card) in _pointerFiles) card.Load(_pointer.PointerFile(role));
    }

    private void LoadSwap()
    {
        if (_pointer.Read(PointerValue.Swap) is { } swapped) _pointerSwitches[PointerValue.Swap].Load(swapped == 1);
    }

    /// A card that only applies while another switch is on (or off) is greyed with the reason while it does not.
    private void UpdatePointerNeeds()
    {
        foreach (var control in PointerControls.All.Where(c => c.Needs is not null))
        {
            var needs = control.Needs!;
            var slider = _pointerSliders[control.Value!.Value];
            if (_pointerSwitches[needs.Value].IsOn != needs.On) slider.Availability.MakeUnavailable(needs.Reason);
            else if (slider.Availability.Reason == needs.Reason) slider.Availability.MakeAvailable();
        }
    }

    private void UpdateRestoreCard()
    {
        if (_pointer.HasOriginal) _restoreCard.Availability.MakeAvailable();
        else _restoreCard.Availability.MakeUnavailable("Nothing changed yet.");
    }

    private OperationResult SetPointerValue(PointerControl control, int value)
    {
        _pointer.Set(control.Value!.Value, value);
        UpdateRestoreCard();
        return OperationResult.Ok();
    }

    private void OnPointerSwitch(PointerControl control, bool on)
    {
        if (control.Value == PointerValue.Swap)
        {
            _pointerActions.AskKeepSwap(on);
            return;
        }
        _pointer.Set(control.Value!.Value, on ? 1 : 0);
        UpdatePointerNeeds();
        UpdateRestoreCard();
    }

    private void OnSchemePicked(string label)
    {
        if (_schemes.FirstOrDefault(s => s.Label == label) is { } scheme) _pointerActions.ApplyScheme(scheme);
    }

    private void ChoosePointer(CursorRole role, string path)
    {
        if (_pointer.ProblemWith(path) is { } problem) _report(problem);
        else SetPointer(role, path);
    }

    /// Uses <paramref name="path"/> for one pointer, or Windows' default pointer for it when null.
    private void SetPointer(CursorRole role, string? path) => Guard.Fire("Mouse: pointer", async () =>
    {
        if (!await _pointer.SetPointer(role, path)) _report($"Windows didn't accept the new {role.Name} pointer, so it stays as before.");
        LoadPointers();
        UpdateRestoreCard();
    }, _report);

    private void OpenWindowsPage(string page)
    {
        if (WebPage.Open(page, _report) is { } problem) _report(problem);
    }
}
