using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace OmniControl.App.Pages.Volume.Easy.Flow;

/// <summary>
/// One card of the Engine step on screen. It is updated in place from <see cref="EasyEngineCard"/> whenever the facts change, so its
/// tag turns good and its button greys by itself when an install finishes.
/// </summary>
public sealed partial class EasyEngineItem : ObservableObject
{
    public EasyEngineItem(EasyEngine engine, Action<EasyEngine> pick, Func<EasyEngine, Task> get)
    {
        Engine = engine;
        PickCommand = new RelayCommand(() => pick(Engine), () => IsAvailable);
        GetCommand = new AsyncRelayCommand(() => get(Engine), () => CanGet && !IsBusy);
    }

    public EasyEngine Engine { get; }
    public IRelayCommand PickCommand { get; }
    public IAsyncRelayCommand GetCommand { get; }

    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _description = string.Empty;
    /// <summary>The facts line, or for a way the goal does not offer, the reason.</summary>
    [ObservableProperty] private string _facts = string.Empty;
    [ObservableProperty] private string _tag = string.Empty;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(TagIsCaution))] private bool _tagIsGood;
    [ObservableProperty] private bool _isAvailable = true;
    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private string _getLabel = string.Empty;
    [ObservableProperty] private bool _canGet;
    [ObservableProperty] private bool _isBusy;

    public bool TagIsCaution => !TagIsGood;

    public void Update(EasyEngineCard card, bool selected)
    {
        Name = card.Name;
        Description = card.Description;
        Facts = card.Reason ?? card.Facts;
        Tag = card.Tag;
        TagIsGood = card.TagIsGood;
        IsAvailable = card.Available;
        IsSelected = selected && card.Available;
        GetLabel = card.GetLabel;
        CanGet = card.CanGet;
        PickCommand.NotifyCanExecuteChanged();
        GetCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsBusyChanged(bool value) => GetCommand.NotifyCanExecuteChanged();
}
