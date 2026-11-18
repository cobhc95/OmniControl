using OmniControl.App.Services;

namespace OmniControl.App.OtherPcs;

// The unfinished "New PC" tabs (docs/mockups/pair-pc-in-tab.html): each is kept in other-pcs.json with how far it got (the way chosen, the
// address or PC typed or picked), so the person can come back to it, also after a restart. Only the tab's x removes it; pairing turns it into the PC's tab.
public sealed partial class OtherPcsService
{
    /// <summary>The unfinished New PC tabs, (the order of all tabs is <see cref="TabOrder"/>).</summary>
    public IReadOnlyList<SetupTab> SetupTabs => _settings.SetupTabs.ToList();

    /// <summary>"+": opens a new, empty New PC tab.</summary>
    public SetupTab AddSetupTab()
    {
        var tab = new SetupTab { Id = Guid.NewGuid().ToString("N") };
        _settings.SetupTabs.Add(tab);
        _settings.TabOrder.Add(tab.Id);
        Save();
        RaiseChanged();
        return tab;
    }

    /// <summary>The person changed something in a New PC tab: it is kept (nothing is saved when it is the same as before).</summary>
    public void UpdateSetupTab(string id, bool internet, string address, string target)
    {
        if (_settings.SetupTabs.FirstOrDefault(t => t.Id == id) is not { } tab) return;
        if (tab.Internet == internet && tab.Address == address && tab.Target == target) return;
        (tab.Internet, tab.Address, tab.Target) = (internet, address, target);
        Save();
    }

    /// <summary>The tab's x: the tab goes and, when pairing belonged to it, pairing is given up.</summary>
    public void RemoveSetupTab(string id)
    {
        var owned = Pairing.TabId == id;
        if (!DropSetupTab(id)) return;
        if (owned && Pairing.IsActive) ClosePairing();
        else RaiseChanged();
    }

    // Takes the tab out and saves; false when there was none.
    private bool DropSetupTab(string id)
    {
        if (_settings.SetupTabs.RemoveAll(t => t.Id == id) == 0) return false;
        _settings.TabOrder.Remove(id);
        Save();
        return true;
    }

    // A paired PC's tab takes the place its New PC tab had.
    private void TakePlace(string tabId, string pcId)
    {
        var at = _settings.TabOrder.IndexOf(tabId);
        if (at < 0) return;
        _settings.TabOrder.Remove(pcId);
        _settings.TabOrder.Insert(_settings.TabOrder.IndexOf(tabId), pcId);
    }
}
