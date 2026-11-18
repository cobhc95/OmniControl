using CommunityToolkit.Mvvm.ComponentModel;

namespace OmniControl.App.Screenshot;

/// <summary>What the shared save dialog shows and edits: its title, the file name (with its ending), the folder and the "Always save here" tick.</summary>
public sealed partial class SaveFileModel : ObservableObject
{
    private readonly string _extension;

    public string Title { get; }

    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanSave))] private string _name;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanSave))] private string _folder;

    [ObservableProperty] private bool _alwaysHere;

    public SaveFileModel(string title, string name, string extension, string folder)
    {
        Title = title;
        _extension = extension;
        _name = name + extension;
        _folder = folder;
    }

    public bool CanSave => CleanName.Length > 0 && Folder.Trim().Length > 0;

    /// <summary>The typed name without the characters Windows does not allow and without the ending (it is always the format's own).</summary>
    public string CleanName
    {
        get
        {
            var name = Name.Trim();
            if (name.EndsWith(_extension, StringComparison.OrdinalIgnoreCase)) name = name[..^_extension.Length];
            var invalid = Path.GetInvalidFileNameChars();
            return new string(name.Where(c => !invalid.Contains(c)).ToArray()).Trim('.', ' ');
        }
    }
}
