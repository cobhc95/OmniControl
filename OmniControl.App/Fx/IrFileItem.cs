using CommunityToolkit.Mvvm.ComponentModel;
using OmniControl.App.Pages;

namespace OmniControl.App.Fx;

/// <summary>The impulse response file card of an IR Loader effect: shows the chosen file and lets the user pick another.</summary>
public sealed partial class IrFileItem : ObservableObject, IEditorItem
{
    private readonly Action<string> _save;

    public IrFileItem(string path, Action<string> save)
    {
        _path = path;
        _save = save;
    }

    public string Label => "Impulse response file";

    [ObservableProperty, NotifyPropertyChangedFor(nameof(FileName))] private string _path;

    public string FileName => Path.Length == 0 ? "No file chosen" : System.IO.Path.GetFileName(Path);

    public void Choose(string path)
    {
        Path = path;
        _save(path);
    }
}
