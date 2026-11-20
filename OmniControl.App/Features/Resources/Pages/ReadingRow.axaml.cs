using System.ComponentModel;
using Avalonia.Controls.Primitives;
using Avalonia.VisualTree;

namespace OmniControl.App.Features.Resources.Pages;

/// Wiring only: fills the card's chip menus (network adapter, temperature sensor) with the card's rows when they open, redraws them while open when the rows change, and closes them after a pick.
public partial class ReadingRow : UserControl
{
    private readonly ChipLink _adapter, _sensor;

    public ReadingRow()
    {
        InitializeComponent();
        _adapter = new ChipLink(AdapterChip, NetworkAdapterMenu.Title, card => card.Adapter, this);
        _sensor = new ChipLink(SensorChip, SensorMenu.Title, card => card.Sensor, this);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        _adapter.Follow(DataContext as ReadingCard);
        _sensor.Follow(DataContext as ReadingCard);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _adapter.Attach();
        _sensor.Attach();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _adapter.Detach();
        _sensor.Detach();
    }

    /// One chip and its menu list, fed by one of the card's <see cref="MenuChoiceItem"/>s.
    private sealed class ChipLink
    {
        private readonly MenuRowList _list;
        private readonly Func<ReadingCard, MenuChoiceItem?> _pick;
        private readonly Control _owner;
        private MenuChoiceItem? _item;
        private Flyout? _flyout;
        private bool _open;

        public ChipLink(ChipMenu chip, string title, Func<ReadingCard, MenuChoiceItem?> pick, Control owner)
        {
            (_pick, _owner) = (pick, owner);
            _list = new MenuRowList { Title = title };
            _list.Picked += (_, _) => _flyout?.Hide();
            chip.Menu = _list;
            chip.MenuOpened += OnOpened;
            chip.MenuClosed += OnClosed;
        }

        public void Follow(ReadingCard? card)
        {
            Detach();
            _item = card is null ? null : _pick(card);
            if (_owner.IsAttachedToVisualTree()) Attach();
        }

        public void Attach()
        {
            if (_item is not null) _item.PropertyChanged += OnItemChanged;
        }

        public void Detach()
        {
            if (_item is not null) _item.PropertyChanged -= OnItemChanged;
        }

        private void OnItemChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MenuChoiceItem.Rows) && _open && _item is not null) _list.ItemsSource = _item.Rows;
        }

        private void OnOpened(object? sender, Flyout flyout)
        {
            if (_item is null) return;
            (_flyout, _open) = (flyout, true);
            _item.OpenChanged?.Invoke(true);
            _list.ItemsSource = _item.Rows;
        }

        private void OnClosed(object? sender, EventArgs e)
        {
            if (_open) _item?.OpenChanged?.Invoke(false);
            (_flyout, _open) = (null, false);
        }
    }
}
