using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using OmniControl.App.Services;

namespace OmniControl.App.Controls;

/// <summary>
/// One row of a <see cref="MenuRowList"/>: its words (shown whole, wrapping), a "Get it" style link at the right (may be empty), whether it is the
/// one that is on now, a tooltip and what it does; optionally tags (after the words, or on their own line under them with
/// <see cref="TagsOnOwnLine"/>), one muted line of detail, a dimmed look, or a header row (a muted section title) with a thin line above it.
/// </summary>
public sealed record MenuRowItem(string Text, string Link, bool IsOn, string Tip, ICommand Command, IReadOnlyList<TagItem>? Tags = null, string Detail = "", bool Dim = false, bool IsHeader = false, bool RuleAbove = false, bool TagsOnOwnLine = false, bool IsSwitch = false)
{
    /// <summary>A muted section title with no command.</summary>
    public static MenuRowItem Header(string text, bool ruleAbove = false) => new(text, string.Empty, false, string.Empty, NoCommand, IsHeader: true, RuleAbove: ruleAbove);

    private static readonly ICommand NoCommand = new CommunityToolkit.Mvvm.Input.RelayCommand(() => { });

    public bool IsRow => !IsHeader;
    public IReadOnlyList<TagItem> InlineTags => TagsOnOwnLine ? Array.Empty<TagItem>() : Tags ?? Array.Empty<TagItem>();
    public IReadOnlyList<TagItem> OwnLineTags => TagsOnOwnLine ? Tags ?? Array.Empty<TagItem>() : Array.Empty<TagItem>();
    public bool HasOwnLineTags => OwnLineTags.Count > 0;
    public double RowOpacity => Dim ? 0.6 : 1;
    public bool HasLink => Link.Length > 0;

    /// <summary>A row with a switch at its right (shown by <see cref="IsOn"/>) instead of a tick at its left.</summary>
    public bool ShowsTick => IsOn && !IsSwitch;
    public bool HasDetail => Detail.Length > 0;
}

/// <summary>Layout and wiring only: the rows of a small menu; any row click raises <see cref="Picked"/> so the owner can close its pop-down (the row's own command runs as well).</summary>
public partial class MenuRowList : UserControl
{
    public static readonly StyledProperty<string?> TitleProperty = AvaloniaProperty.Register<MenuRowList, string?>(nameof(Title));

    public MenuRowList() => InitializeComponent();

    /// <summary>The muted title above the rows (empty: none).</summary>
    public string? Title { get => GetValue(TitleProperty); set => SetValue(TitleProperty, value); }

    public IEnumerable<MenuRowItem>? ItemsSource { get => Rows.ItemsSource as IEnumerable<MenuRowItem>; set => Rows.ItemsSource = value; }

    public event EventHandler? Picked;

    private void OnRowClick(object? sender, RoutedEventArgs e) => Picked?.Invoke(this, EventArgs.Empty);
}
