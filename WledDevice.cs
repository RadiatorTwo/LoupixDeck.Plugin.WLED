namespace LoupixDeck.Plugin.WLED;

/// <summary>
/// A WLED controller. <see cref="Mac"/>, <see cref="Name"/> and <see cref="Host"/> are
/// persisted (as <see cref="StoredDevice"/>); everything else is a runtime cache filled by the
/// poller and by the responses of state changes.
/// </summary>
public sealed class WledDevice
{
    /// <summary>Stable identity (lowercase hex, no separators, as WLED reports it).</summary>
    public string Mac { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    /// <summary>Host name or IP address, optionally with ":port".</summary>
    public string Host { get; set; } = string.Empty;

    public bool Online { get; set; }
    public bool On { get; set; }
    public int Brightness { get; set; }
    public int Preset { get; set; } = -1;
    public int Effect { get; set; }
    public int Palette { get; set; }
    public int Speed { get; set; }
    public int Intensity { get; set; }

    public IReadOnlyList<string> Effects { get; set; } = [];
    public IReadOnlyList<string> Palettes { get; set; } = [];
    public IReadOnlyList<KeyValuePair<int, string>> Presets { get; set; } = [];

    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? Host : Name;

    public string EffectName(int id) => id >= 0 && id < Effects.Count ? Effects[id] : id.ToString();

    public string PaletteName(int id) => id >= 0 && id < Palettes.Count ? Palettes[id] : id.ToString();
}

/// <summary>The persisted part of a <see cref="WledDevice"/>. Add fields only as optional.</summary>
public sealed class StoredDevice
{
    public string Mac { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Host { get; set; } = string.Empty;
}
