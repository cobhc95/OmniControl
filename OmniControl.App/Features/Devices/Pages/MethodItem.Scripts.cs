using System.Net;
using OmniControl.App.Features.Devices;

namespace OmniControl.App.Features.Devices.Pages;

// The scripts method of a device: the address, the sign-in and the template with its Advanced command templates, and
// its set-up stages. Each field is saved when it is left (the password goes to Windows, never here).
public sealed partial class MethodItem
{
    [ObservableProperty] private string _address;
    [ObservableProperty] private string _email;
    [ObservableProperty] private bool _reserved;

    /// The password being typed; saved to Windows when the field is left, then cleared (it is never kept here).
    [ObservableProperty] private string _password = string.Empty;

    public bool HasPassword => _service.HasPassword(Record);
    public string PasswordWatermark => HasPassword ? "Saved · type to replace" : "Password";

    /// True when the address box holds something that is not an IP address, so the view can say so.
    public bool AddressLooksWrong => Address.Trim().Length > 0 && !IPAddress.TryParse(Address.Trim(), out _);

    public string RouterLinkText => _service.Setup.RouterPage is { } page ? $"Open {page}" : "Open your router's page";

    partial void OnAddressChanged(string value)
    {
        Record.Address = value.Trim();
        _service.Edited();
        Changed();
    }

    partial void OnEmailChanged(string value)
    {
        Record.Email = value.Trim();
        _service.Edited();
        Changed();
    }

    partial void OnReservedChanged(bool value)
    {
        Record.Reserved = value;
        _service.Edited();
        Changed();
    }

    /// When the password was stored in this run (null: unknown).
    [ObservableProperty] private DateTime? _passwordSavedAt;

    /// The Save pill or Enter: writes the typed password to Windows and empties the field; the password is only in Windows from then on.
    [RelayCommand]
    private void SavePassword() => StorePassword();

    private bool StorePassword()
    {
        if (Password.Length == 0) return true;
        if (!_service.SavePassword(Record, Password))
        {
            _report("Windows wouldn't keep the password. Try again.");
            return false;
        }
        Password = string.Empty;
        PasswordSavedAt = DateTime.Now;
        Changed();
        return true;
    }

    /// Before a test, which only uses the saved password: asks once when new text is waiting; false stops the test.
    private async Task<bool> SettlePasswordForTest()
    {
        if (Password.Length == 0) return true;
        return await _ask(new DialogMessage("Save the new password first?", "Save the new password first?", "Save and test")) && StorePassword();
    }

    // ---- The profile and its Advanced templates ----

    private ScriptProfile Profile => _service.Scripts.ProfileOf(Record);

    public string ProfileText => Profile.Name;

    /// The header chip: "Template: Tapo".
    public string TemplateChipText => $"Template: {Profile.Name}";

    /// "built-in · v1" (green) or "my profile".
    public IReadOnlyList<TagItem> ProfileTags => new[] { Profile.IsBuiltIn ? new TagItem($"built-in · v{Profile.Version}", TagTone.Good) : new TagItem("mine") };

    public string ProfileNote => Profile.IsBuiltIn ? "updated with OmniControl" : "yours, never changed by an update";

    /// The template menu: the built-in profiles, then the user's own after a line.
    public IReadOnlyList<MenuRowItem> ProfileOptions
    {
        get
        {
            var rows = ScriptProfiles.BuiltIn.Select(ProfileRow).ToList();
            if (_service.Profiles.Count > 0) rows.Add(MenuRowItem.Header("My templates", ruleAbove: true));
            rows.AddRange(_service.Profiles.Select(ProfileRow));
            rows.Add(new MenuRowItem("Manage templates…", string.Empty, false, "Opens Advanced: edit, save as your own, export or import.", new RelayCommand(() => ShowsAdvanced = true), RuleAbove: true));
            return rows;
        }
    }

    private MenuRowItem ProfileRow(ScriptProfile p) => new(p.Name, string.Empty, p.Id == Profile.Id, p.On, new RelayCommand(() => PickProfile(p)), TemplateTags(p), p.Brief, TagsOnOwnLine: false);

    /// "built-in" (green), the template's own tag ("slower"), or "mine".
    private static IReadOnlyList<TagItem> TemplateTags(ScriptProfile p) => p.IsBuiltIn
        ? (p.MenuTag.Length > 0 ? new[] { new TagItem("built-in", TagTone.Good), new TagItem(p.MenuTag, TagTone.Caution) } : new[] { new TagItem("built-in", TagTone.Good) })
        : new[] { new TagItem("mine") };

    private void PickProfile(ScriptProfile profile)
    {
        Record.ProfileId = profile.Id;
        _service.Edited();
        LoadDraft();
        RebuildScriptStages();
        Changed();
    }

    /// The Advanced pane under the Template row is open.
    [ObservableProperty] private bool _showsAdvanced;

    [RelayCommand]
    private void ToggleAdvanced()
    {
        if (!ShowsAdvanced) LoadDraft();
        ShowsAdvanced = !ShowsAdvanced;
    }

    public string AdvancedTitle => $"Advanced: command templates of {Profile.Name}";

    public string AdvancedNote => $"{KasaCommand.IpField}, {KasaCommand.EmailField}, {KasaCommand.PasswordField} are filled in; the password never appears in logs.";

    /// The templates shown in the Advanced pane: the user's own profile is edited live; a built-in one only as a draft until "Save as my profile…".
    [ObservableProperty] private string _draftOn = string.Empty;
    [ObservableProperty] private string _draftOff = string.Empty;
    [ObservableProperty] private string _draftState = string.Empty;

    /// The user's own profile's name (editable in the pane); empty for a built-in one.
    [ObservableProperty] private string _ownName = string.Empty;

    public bool IsOwnProfile => !Profile.IsBuiltIn;
    public bool CanResetProfile => !Profile.IsBuiltIn || DraftDiffers;
    private bool DraftDiffers => DraftOn != Profile.On || DraftOff != Profile.Off || DraftState != Profile.State;

    private bool _loadingDraft;

    private void LoadDraft()
    {
        _loadingDraft = true;
        (DraftOn, DraftOff, DraftState, OwnName) = (Profile.On, Profile.Off, Profile.State, Profile.IsBuiltIn ? string.Empty : Profile.Name);
        _loadingDraft = false;
    }

    partial void OnDraftOnChanged(string value) => DraftEdited();
    partial void OnDraftOffChanged(string value) => DraftEdited();
    partial void OnDraftStateChanged(string value) => DraftEdited();

    partial void OnOwnNameChanged(string value)
    {
        if (_loadingDraft || Profile.IsBuiltIn || value.Trim().Length == 0) return;
        Profile.Name = value.Trim();
        _service.Edited();
        Changed();
    }

    private void DraftEdited()
    {
        if (_loadingDraft) return;
        if (!Profile.IsBuiltIn)
        {
            (Profile.On, Profile.Off, Profile.State) = (DraftOn.Trim(), DraftOff.Trim(), DraftState.Trim());
            _service.Edited();
        }
        OnPropertyChanged(nameof(CanResetProfile));
    }

    private ScriptProfile Draft => new() { Name = Profile.Name, On = DraftOn.Trim(), Off = DraftOff.Trim(), State = DraftState.Trim(), Icon = Profile.Icon, Version = Profile.Version, NeedsSignIn = Profile.NeedsSignIn, SignInNote = Profile.SignInNote, Stages = Profile.Stages, Tips = Profile.Tips };

    [RelayCommand]
    private async Task TestTemplates()
    {
        if (!await SettlePasswordForTest()) return;
        var (problem, confirmed) = await _service.TestScriptAsync(_device, Record, Draft);
        _report(problem ?? (confirmed ? "It switched off and on, and the State command read both back." : "Both commands ran. Did the device switch off and on?"));
        Changed();
    }

    [RelayCommand]
    private void SaveAsOwn()
    {
        _service.Scripts.SaveAsOwn(Record, Draft, Profile);
        LoadDraft();
        RebuildScriptStages();
        Changed();
    }

    [RelayCommand]
    private void ResetProfile()
    {
        _service.Scripts.ResetToBuiltIn(Record);
        LoadDraft();
        RebuildScriptStages();
        Changed();
    }

    /// Writes the profile to <paramref name="path"/> (the view asked where).
    public void ExportProfile(string path)
    {
        if (_service.Scripts.ExportProfile(Draft, path) is { } problem) _report(problem);
    }

    /// Reads a profile from <paramref name="path"/> as the user's own and puts the device on it.
    public void ImportProfile(string path)
    {
        if (_service.Scripts.ImportProfile(Record, path).Problem is { } problem) _report(problem);
        LoadDraft();
        RebuildScriptStages();
        Changed();
    }

    public string ProfileFileName => $"{Profile.Name}.json";

    // ---- The set-up stages ----

    /// The stages generated from the template: Python, python-kasa, Fixed address, the template's own, Sign-in (only if it needs one), Test.
    private IReadOnlyList<SetupStageItem> MakeScriptStages(Action<SetupStageItem> open)
    {
        var stages = new List<SetupStageItem>
        {
            new(1, "Python", () => _service.Setup.PythonPath is not null, () => _service.Setup.PythonPath is not null ? "found" : "not found", open, "python"),
            new(2, "python-kasa", () => _service.Setup.KasaInstalled, () => _service.Setup.KasaInstalled ? "installed" : _service.Setup.Installing ? "installing…" : "not installed", open, "kasa"),
            new(3, "Fixed address", () => IPAddress.TryParse(Record.Address.Trim(), out _) && Record.Reserved,
                () => Record.Address.Trim().Length == 0 ? "no address yet" : $"{Record.Address.Trim()}{(Record.Reserved ? " · reserved" : " · not reserved yet")}", open, "address"),
        };
        foreach (var stage in Profile.Stages)
        {
            var id = stage.Id;
            stages.Add(new SetupStageItem(stages.Count + 1, stage.Title, () => Record.StagesDone.Contains(id), () => Record.StagesDone.Contains(id) ? "done" : "to do", open,
                TemplateKey + id, stage.Text, stage.Confirm, () => ConfirmTemplateStage(id)));
        }
        if (Profile.NeedsSignIn)
            stages.Add(new SetupStageItem(stages.Count + 1, "Sign-in", () => Record.Email.Trim().Length > 0 && HasPassword,
                () => Record.Email.Trim().Length == 0 ? "no account yet" : HasPassword ? $"{Record.Email.Trim()} · password saved" : $"{Record.Email.Trim()} · no password yet", open, "signin"));
        stages.Add(new SetupStageItem(stages.Count + 1, "Test", () => ScriptPassed, () => ScriptPassed ? "it switched" : "turns it off, then on again", open, "test"));
        return stages;
    }

    private const string TemplateKey = "template:";

    private SetupStageItem? ScriptStage(string key) => _scriptStages.Items.FirstOrDefault(s => s.Key == key);

    public SetupStageItem? PythonStage => ScriptStage("python");
    public SetupStageItem? KasaStage => ScriptStage("kasa");
    public SetupStageItem? AddressStage => ScriptStage("address");
    public SetupStageItem? SignInStage => ScriptStage("signin");
    public SetupStageItem? TestStage => ScriptStage("test");
    public IReadOnlyList<SetupStageItem> TemplateStageItems => _scriptStages.Items.Where(s => s.Key.StartsWith(TemplateKey, StringComparison.Ordinal)).ToList();

    public bool NeedsSignIn => Profile.NeedsSignIn;
    public string SignInWatermark => Profile.SignInNote.Length > 0 ? Profile.SignInNote : "Account email";

    private void ConfirmTemplateStage(string id)
    {
        if (!Record.StagesDone.Contains(id)) Record.StagesDone.Add(id);
        _service.Edited();
        AdvanceSetup();
    }

    /// The template changed: its stages are made again, and the one that was open stays open when it still exists.
    private void RebuildScriptStages()
    {
        var open = _scriptStages.Items.FirstOrDefault(s => s.IsOpen)?.Key;
        _scriptStages = new SetupStages(MakeScriptStages);
        _scriptStages.Refresh();
        if (open is not null && _scriptStages.Items.FirstOrDefault(s => s.Key == open) is { } same) _scriptStages.Open(same);
        else _scriptStages.OpenNext();
        foreach (var property in new[] { nameof(Stages), nameof(ScriptStageItems), nameof(PythonStage), nameof(KasaStage), nameof(AddressStage), nameof(SignInStage), nameof(TestStage), nameof(TemplateStageItems), nameof(NeedsSignIn), nameof(SignInWatermark), nameof(CanFinish) })
            OnPropertyChanged(property);
    }

    public bool CanInstall => _service.Setup.PythonPath is not null && !_service.Setup.Installing;
    public string InstallLabel => _service.Setup.Installing ? "Installing…" : "Install";

    private void RefreshScripts()
    {
        InstallCommand.NotifyCanExecuteChanged();
        foreach (var property in new[]
                 {
                     nameof(HasPassword), nameof(PasswordWatermark), nameof(AddressLooksWrong), nameof(RouterLinkText), nameof(ProfileText), nameof(TemplateChipText), nameof(ProfileTags), nameof(ProfileNote),
                     nameof(ProfileOptions), nameof(NeedsSignIn), nameof(SignInWatermark), nameof(AdvancedTitle), nameof(IsOwnProfile), nameof(CanResetProfile), nameof(ProfileFileName), nameof(CanInstall), nameof(InstallLabel),
                 })
            OnPropertyChanged(property);
    }

    [RelayCommand(CanExecute = nameof(CanInstall))]
    private async Task Install()
    {
        if (await _service.Setup.InstallAsync() is { } problem) _report(Guard.SeeLog(problem, DeviceService.LogFile));
        AdvanceSetup();
    }

    [RelayCommand]
    private void OpenRouter()
    {
        if (_service.Setup.OpenRouterPage() is { } problem) _report(problem);
    }

    [RelayCommand]
    private void OpenPythonPage()
    {
        if (_service.Setup.OpenPythonPage() is { } problem) _report(problem);
    }
}
