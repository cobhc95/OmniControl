using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using OmniControl.App.ViewModels;

namespace OmniControl.App.Views;

public partial class ShellView : UserControl
{
    private TextBox? _search;
    private ListBox? _tabs;

    public ShellView()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            _search = this.FindControl<TextBox>("SearchBox");
            _tabs = this.FindControl<ListBox>("TabList");
            if (_search != null)
            {
                // No GotFocus open: focusing the box alone never switches pages.
                // Inline results appear only while the box holds a typed query (see VM).
                // Inline mode never auto-closes on blur — users click/drag the live controls.
                _search.KeyDown += OnSearchKeyDown;
            }
            if (_tabs != null)
            {
                _tabs.KeyDown += OnTabKeyDown;
            }
            // Start focus on tabs, not search, so popup stays closed like mockups.
            Dispatcher.UIThread.Post(() => _tabs?.Focus(), DispatcherPriority.Background);
        };
    }

    private void OnTabKeyDown(object? sender, KeyEventArgs e)
    {
        if (_tabs == null) return;
        var idx = _tabs.SelectedIndex;
        if (e.Key == Key.Left)
        {
            _tabs.SelectedIndex = idx <= 0 ? _tabs.ItemCount - 1 : idx - 1;
            e.Handled = true;
        }
        else if (e.Key == Key.Right)
        {
            _tabs.SelectedIndex = idx < 0 || idx >= _tabs.ItemCount - 1 ? 0 : idx + 1;
            e.Handled = true;
        }
        else if (e.Key == Key.Enter || e.Key == Key.Space)
        {
            // selection already bound; just keep focus
            e.Handled = true;
        }
    }

    /// <summary>Keyboard-focus the real control behind a search result (arrows/Enter then drive it).</summary>
    private void FocusSearchTarget(string? focusName)
    {
        if (string.IsNullOrWhiteSpace(focusName)) return;
        Dispatcher.UIThread.Post(() =>
        {
            Control? target = null;
            if (this.FindControl<ContentControl>("PageHost")?.Content is Control page)
                target = page.FindControl<Control>(focusName);
            target ??= this.FindControl<Control>(focusName);
            if (target != null) { target.Focus(); return; }
            // Tab swap may realize the page a frame later; retry once.
            Dispatcher.UIThread.Post(() =>
            {
                Control? t = null;
                if (this.FindControl<ContentControl>("PageHost")?.Content is Control p)
                    t = p.FindControl<Control>(focusName);
                t ??= this.FindControl<Control>(focusName);
                t?.Focus();
            }, DispatcherPriority.Background);
        }, DispatcherPriority.Render);
    }

    private void OnSearchKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not ShellViewModel vm) return;
        if (e.Key == Key.Escape) { vm.CloseSearchCommand.Execute(null); e.Handled = true; }
        else if (e.Key == Key.Enter && vm.IsSearchOpen)
        {
            // Inline mode: Enter jumps to the first live control; then Tab/arrows drive it.
            FocusSearchTarget(vm.Search.FirstFocusName);
            e.Handled = true;
        }
        else if (e.Key == Key.Tab && e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            // Shift+Tab closes search to go back
            vm.CloseSearchCommand.Execute(null);
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (DataContext is not ShellViewModel vm) return;
        // Ctrl+K opens search from anywhere
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key == Key.K)
        {
            e.Handled = true;
            Dispatcher.UIThread.Post(() =>
            {
                _search?.Focus();
                _search?.SelectAll();
                vm.OpenSearchCommand.Execute(null);
            });
            return;
        }
        // Typing anywhere (printable) opens search and starts the query
        if (e.Key == Key.Escape && vm.IsSearchOpen) { vm.CloseSearchCommand.Execute(null); e.Handled = true; return; }
        if (e.Handled) return;
        var focused = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();
        if (focused is TextBox) return;
        if (e.KeyModifiers != 0) return;
        var ch = e.Key switch
        {
            Key.A => "a", Key.B => "b", Key.C => "c", Key.D => "d", Key.E => "e", Key.F => "f",
            Key.G => "g", Key.H => "h", Key.I => "i", Key.J => "j", Key.K => "k", Key.L => "l",
            Key.M => "m", Key.N => "n", Key.O => "o", Key.P => "p", Key.Q => "q", Key.R => "r",
            Key.S => "s", Key.T => "t", Key.U => "u", Key.V => "v", Key.W => "w", Key.X => "x",
            Key.Y => "y", Key.Z => "z", Key.Space => " ", Key.D0 => "0", Key.D1 => "1",
            _ => null
        };
        if (ch == null) return;
        e.Handled = true;
        Dispatcher.UIThread.Post(() =>
        {
            vm.SearchText = ch;
            if (_search != null)
            {
                _search.Focus();
                _search.CaretIndex = _search.Text?.Length ?? 0;
            }
            vm.OpenSearchCommand.Execute(null);
        });
    }
}
