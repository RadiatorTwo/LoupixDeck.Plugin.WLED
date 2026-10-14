using System.Text.Json.Nodes;
using LoupixDeck.PluginSdk;

namespace LoupixDeck.Plugin.WLED;

/// <summary>
/// Paged folder listing the presets or effects of one device. A press activates the entry; the
/// active entry is highlighted. The first dial pages through long lists.
/// </summary>
public sealed class WledListFolder : FolderProviderBase
{
    private static readonly PluginColor ActiveColor = PluginColor.FromRgb(255, 140, 0);
    private static readonly PluginColor EntryColor = PluginColor.FromRgb(30, 30, 30);
    private static readonly PluginColor NavColor = PluginColor.FromRgb(60, 60, 90);

    private readonly WledController _controller;
    private readonly WledDevice _device;
    private readonly IPluginHost _host;
    private readonly bool _presets;
    private int _page;

    private WledListFolder(WledController controller, WledDevice device, IPluginHost host, bool presets)
    {
        _controller = controller;
        _device = device;
        _host = host;
        _presets = presets;
    }

    public static WledListFolder Presets(WledController controller, WledDevice device, IPluginHost host) =>
        new(controller, device, host, presets: true);

    public override string Title => _device.DisplayName;

    public override IReadOnlyDictionary<int, RotaryOverride> RotaryOverrides => new Dictionary<int, RotaryOverride>
    {
        [0] = new RotaryOverride
        {
            Label = _host.Tr("Page"),
            OnLeft = () => TurnPage(-1),
            OnRight = () => TurnPage(1),
            GetValue = () => new AdjustmentValue(PageCount > 1 ? _page / (double)(PageCount - 1) : 0,
                $"{_page + 1}/{PageCount}"),
        },
    };

    public override void OnEnter() => _ = ReloadAsync();

    private async Task ReloadAsync()
    {
        await _controller.LoadListsAsync(_device);
        RaiseEntriesChanged();
    }

    private IReadOnlyList<(int Id, string Name)> Items => _presets
        ? _device.Presets.Select(p => (p.Key, p.Value)).ToList()
        : _device.Effects.Select((name, id) => (id, name)).Where(e => e.name is not ("RSVD" or "-" or "")).ToList();

    private int Active => _presets ? _device.Preset : _device.Effect;

    /// <summary>Slots that are not the host's back slot.</summary>
    private int Capacity => _host.FolderGrid.TotalSlots - 1;

    /// <summary>The presets folder carries one fixed entry that opens the effects folder.</summary>
    private int FixedEntries => _presets ? 1 : 0;

    private bool Paged => (Items.Count + FixedEntries) > Capacity;

    private int PerPage => Math.Max(1, Paged ? Capacity - FixedEntries - 2 : Capacity - FixedEntries);

    private int PageCount => Math.Max(1, (Items.Count + PerPage - 1) / PerPage);

    private Task TurnPage(int delta)
    {
        int page = Math.Clamp(_page + delta, 0, PageCount - 1);
        if (page != _page)
        {
            _page = page;
            RaiseEntriesChanged();
        }
        return Task.CompletedTask;
    }

    public override IReadOnlyList<FolderEntry> BuildEntries()
    {
        FolderGridInfo grid = _host.FolderGrid;
        List<FolderEntry> entries = [];
        int index = 0;

        void Add(string text, PluginColor back, Func<Task>? onPress = null, IFolderProvider? opens = null, bool bold = false)
        {
            int slot = grid.SlotForIndex(index++);
            if (slot >= 0)
                entries.Add(new FolderEntry { SlotIndex = slot, Text = text, BackColor = back, TextSize = 13, Bold = bold, OnPress = onPress, OpensFolder = opens });
        }

        if (_presets)
            Add(_host.Tr("Effects") + " ›", NavColor, opens: new WledListFolder(_controller, _device, _host, presets: false), bold: true);

        IReadOnlyList<(int Id, string Name)> items = Items;
        if (items.Count == 0)
        {
            Add(_device.Online ? _host.Tr("No entries") : _host.Tr("Offline"), EntryColor);
            return entries;
        }

        _page = Math.Clamp(_page, 0, PageCount - 1);
        foreach ((int id, string name) in items.Skip(_page * PerPage).Take(PerPage))
            Add(name, id == Active ? ActiveColor : EntryColor, () => Activate(id), bold: id == Active);

        if (Paged)
        {
            Add("◀", NavColor, () => TurnPage(-1));
            Add("▶", NavColor, () => TurnPage(1));
        }

        return entries;
    }

    private async Task Activate(int id)
    {
        JsonObject patch = _presets
            ? new JsonObject { ["ps"] = id }
            : new JsonObject { ["seg"] = new JsonObject { ["fx"] = id } };
        await _controller.SendAsync(_device, patch);
        RaiseEntriesChanged();
    }
}
