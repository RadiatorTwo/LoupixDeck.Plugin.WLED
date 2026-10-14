using LoupixDeck.PluginSdk;

namespace LoupixDeck.Plugin.WLED;

/// <summary>
/// Known WLED devices keyed by MAC. Buttons reference a device by MAC, so a device keeps
/// working after its IP changes. The list is persisted so buttons work before discovery finishes.
/// </summary>
public sealed class WledDeviceRegistry(IPluginSettings settings)
{
    private const string SettingsKey = "devices";

    private readonly Lock _lock = new();
    private readonly Dictionary<string, WledDevice> _devices = new(StringComparer.OrdinalIgnoreCase);

    public void Load()
    {
        List<StoredDevice> stored = settings.Get<List<StoredDevice>>(SettingsKey) ?? [];
        lock (_lock)
        {
            foreach (StoredDevice d in stored.Where(d => !string.IsNullOrWhiteSpace(d.Mac)))
                _devices[d.Mac] = new WledDevice { Mac = d.Mac, Name = d.Name, Host = d.Host };
        }
    }

    public IReadOnlyList<WledDevice> All
    {
        get
        {
            lock (_lock)
                return _devices.Values.OrderBy(d => d.DisplayName, StringComparer.CurrentCultureIgnoreCase).ToList();
        }
    }

    public WledDevice? Find(string? mac)
    {
        if (string.IsNullOrWhiteSpace(mac))
            return null;
        lock (_lock)
            return _devices.GetValueOrDefault(mac.Trim());
    }

    /// <summary>Adds a device or updates its name/host. Returns the registered instance.</summary>
    public WledDevice Upsert(WledInfo info, string host)
    {
        WledDevice device;
        bool changed;
        lock (_lock)
        {
            if (_devices.TryGetValue(info.Mac, out WledDevice? existing))
            {
                changed = (existing.Host != host) || (existing.Name != info.Name);
                existing.Host = host;
                existing.Name = info.Name;
                device = existing;
            }
            else
            {
                device = new WledDevice { Mac = info.Mac, Name = info.Name, Host = host };
                _devices[info.Mac] = device;
                changed = true;
            }
        }

        if (changed)
            Persist();
        return device;
    }

    private void Persist()
    {
        List<StoredDevice> stored;
        lock (_lock)
            stored = _devices.Values.Select(d => new StoredDevice { Mac = d.Mac, Name = d.Name, Host = d.Host }).ToList();

        settings.Set(SettingsKey, stored);
        settings.Save();
    }
}
