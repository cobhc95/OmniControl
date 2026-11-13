namespace OmniControl.Core.Engine.Apo;

/// <summary>What one step of an APO install or uninstall does.</summary>
public enum ApoStepKind
{
    /// <summary>Set a string value (HKLM, 64-bit view). Value = the text.</summary>
    SetString,
    /// <summary>Set a multi-string value. Value = string[].</summary>
    SetMultiString,
    /// <summary>Set a DWORD value. Value = int.</summary>
    SetDword,
    DeleteValue,
    DeleteKey,
    /// <summary>Copy the file at Name (source) to Target.</summary>
    CopyFile,
    /// <summary>Write Value (text) to the file Target.</summary>
    WriteFile,
    /// <summary>Create the folder Target and give the Users group modify rights (so OmniControl can write the config).</summary>
    CreateSharedFolder,
    DeleteFolder,
    /// <summary>regsvr32 /s Target.</summary>
    RegisterDll,
    /// <summary>regsvr32 /u /s Target.</summary>
    UnregisterDll,
    /// <summary>Stop and start the Windows Audio service so audiodg loads the new effects.</summary>
    RestartAudioService,
}

/// <summary>One change to the PC, with the sentence a preview prints for it.</summary>
public sealed record ApoStep(ApoStepKind Kind, string Target, string? Name, object? Value, string Description);

/// <summary>
/// Every change an install or uninstall would make, worked out by reading the PC only. <see cref="Problems"/> are reasons it must
/// not run; a plan with problems is shown but never applied.
/// </summary>
public sealed record ApoPlan(string Title, IReadOnlyList<ApoStep> Steps, IReadOnlyList<string> Problems)
{
    public bool CanApply => Problems.Count == 0 && Steps.Count > 0;

    /// <summary>The plan as the plain text that "Preview changes" and the helper's dry run print.</summary>
    public string Describe()
    {
        var lines = new List<string> { Title };
        if (Problems.Count > 0)
        {
            lines.Add("It can't be done:");
            lines.AddRange(Problems.Select(p => "  - " + p));
        }
        if (Steps.Count > 0)
        {
            lines.Add(Problems.Count > 0 ? "It would make these changes:" : "These changes will be made, in this order:");
            lines.AddRange(Steps.Select((s, i) => $"  {i + 1}. {s.Description}"));
        }
        return string.Join(Environment.NewLine, lines);
    }
}
