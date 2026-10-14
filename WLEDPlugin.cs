using LoupixDeck.PluginSdk;

namespace LoupixDeck.Plugin.WLED;

public sealed class WLEDPlugin : LoupixPlugin, IMenuContributor, IPluginSettingsPage
{
    private const string ManualHostsKey = "manualHosts";
    private const string PollSecondsKey = "pollSeconds";
    private const string MdnsKey = "mdnsDiscovery";

    private IPluginHost? _host;
    private WledController? _controller;
    private List<IPluginCommand> _commands = [];

    public override PluginMetadata Metadata { get; } = new()
    {
        Id = "wled",
        Name = "WLED",
        Version = new Version(1, 0, 0),
        SdkVersion = SdkInfo.Version,
        Author = "RadiatorTwo",
        Description = "Controls WLED LED controllers over the JSON API.",
        Icon = LoadIcon()
    };

    /// <summary>The plugin icon (icon.png, embedded). Missing data only costs the icon.</summary>
    private static byte[]? LoadIcon()
    {
        using Stream? stream = typeof(WLEDPlugin).Assembly.GetManifestResourceStream("LoupixDeck.Plugin.WLED.icon.png");
        if (stream == null)
            return null;

        using MemoryStream buffer = new();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    public override void Initialize(IPluginHost host)
    {
        _host = host;
        WledController controller = _controller = new WledController(host);
        controller.Registry.Load();

        _commands =
        [
            new WledPowerCommand(controller, host.Tr),
            new WledSetPowerCommand(controller, on: true),
            new WledSetPowerCommand(controller, on: false),
            new WledSetBrightnessCommand(controller),
            new WledBrightnessDialCommand(controller),
            new WledPresetCommand(controller),
            new WledPresetFolderCommand(controller),
            new WledEffectCommand(controller),
            new WledStepCommand(controller, WledCommandNames.EffectStep, "Step Effect", "fx", WledIcons.Effect, WledIcons.EffectColor),
            new WledFixedStepCommand(controller, WledCommandNames.EffectNext, "Next Effect",
                "Switches to the next effect.", "fx", "~", WledIcons.Next, WledIcons.EffectColor),
            new WledFixedStepCommand(controller, WledCommandNames.EffectPrevious, "Previous Effect",
                "Switches to the previous effect.", "fx", "~-", WledIcons.Previous, WledIcons.EffectColor),
            new WledFixedStepCommand(controller, WledCommandNames.EffectRandom, "Random Effect",
                "Switches to a random effect.", "fx", "r", WledIcons.Random, WledIcons.EffectColor),
            new WledListDialCommand(controller, WledCommandNames.EffectDial, "Effect",
                "Selects the effect by dial. Pressing picks a random effect.", "fx", WledIcons.Effect,
                d => d.Effect, (d, v) => d.Effect = v, d => d.Effects, (d, v) => d.EffectName(v)),
            new WledPaletteCommand(controller),
            new WledStepCommand(controller, WledCommandNames.PaletteStep, "Step Palette", "pal", WledIcons.Palette, WledIcons.PaletteColor),
            new WledFixedStepCommand(controller, WledCommandNames.PaletteNext, "Next Palette",
                "Switches to the next color palette.", "pal", "~", WledIcons.Next, WledIcons.PaletteColor),
            new WledFixedStepCommand(controller, WledCommandNames.PalettePrevious, "Previous Palette",
                "Switches to the previous color palette.", "pal", "~-", WledIcons.Previous, WledIcons.PaletteColor),
            new WledFixedStepCommand(controller, WledCommandNames.PaletteRandom, "Random Palette",
                "Switches to a random color palette.", "pal", "r", WledIcons.Random, WledIcons.PaletteColor),
            new WledListDialCommand(controller, WledCommandNames.PaletteDial, "Palette",
                "Selects the color palette by dial. Pressing picks a random palette.", "pal", WledIcons.Palette,
                d => d.Palette, (d, v) => d.Palette = v, d => d.Palettes, (d, v) => d.PaletteName(v)),
            new WledSegmentValueDialCommand(controller, WledCommandNames.SpeedDial, "Effect Speed",
                "Changes the effect speed by dial. Pressing resets it.", "sx", WledIcons.Speed, d => d.Speed, (d, v) => d.Speed = v),
            new WledSegmentValueDialCommand(controller, WledCommandNames.IntensityDial, "Effect Intensity",
                "Changes the effect intensity by dial. Pressing resets it.", "ix", WledIcons.Intensity, d => d.Intensity, (d, v) => d.Intensity = v),
        ];

        // Only commands that show device state need a redraw when it changes.
        foreach (IPluginCommand command in _commands.Where(c => c is IDisplayImageCommand or IAdjustmentCommand))
            controller.RegisterRefreshCommand(command.Descriptor.CommandName);

        ApplySettings();
        controller.Start();
    }

    public override void Shutdown()
    {
        _controller?.Dispose();
        _controller = null;
    }

    public override IEnumerable<IPluginCommand> GetCommands() => _commands;

    public override IReadOnlyList<CommandGroupDescriptor> GetCommandGroups() =>
    [
        new CommandGroupDescriptor
        {
            Group = WledCommand.Group,
            Description = "WLED LED controllers",
            Icon = WledIcons.Group,
            Section = CommandGroupSection.Plugins,
        },
    ];

    private void ApplySettings()
    {
        if ((_host == null) || (_controller == null))
            return;

        long seconds = _host.Settings.Get(PollSecondsKey, 3L);
        _controller.PollInterval = TimeSpan.FromSeconds(Math.Clamp(seconds, 1, 60));

        _ = _controller.ResolveManualHostsAsync(_host.Settings.Get<string>(ManualHostsKey));
        if (MdnsEnabled)
            _ = _controller.Discovery.ProbeAsync();
        else
            _controller.Discovery.Stop();
    }

    #region Menu

    public Task<IReadOnlyList<MenuNode>> GetMenuNodes(ButtonTargets target)
    {
        IReadOnlyList<WledDevice> devices = _controller?.Registry.All ?? [];
        // Root nodes become top-level menu groups, so no devices means no nodes at all.
        if (devices.Count == 0)
            return Task.FromResult<IReadOnlyList<MenuNode>>([]);

        bool dial = target.HasFlag(ButtonTargets.RotaryEncoder);
        bool key = target.HasFlag(ButtonTargets.TouchButton) || target.HasFlag(ButtonTargets.SimpleButton);

        List<MenuNode> nodes = devices.Select(d => new MenuNode
        {
            Name = d.DisplayName,
            Children = DeviceNodes(d, dial, key),
        }).ToList();
        return Task.FromResult<IReadOnlyList<MenuNode>>(nodes);
    }

    private List<MenuNode> DeviceNodes(WledDevice d, bool dial, bool key)
    {
        List<MenuNode> nodes = [];
        if (dial)
        {
            nodes.Add(Leaf(Tr("Brightness"), WledCommandNames.BrightnessDial, d));
            nodes.Add(Leaf(Tr("Effect"), WledCommandNames.EffectDial, d));
            nodes.Add(Leaf(Tr("Palette"), WledCommandNames.PaletteDial, d));
            nodes.Add(Leaf(Tr("Effect Speed"), WledCommandNames.SpeedDial, d));
            nodes.Add(Leaf(Tr("Effect Intensity"), WledCommandNames.IntensityDial, d));
        }

        if (!key)
            return nodes;

        nodes.Add(Leaf(Tr("Power Toggle"), WledCommandNames.Power, d, ("Mode", "toggle")));
        nodes.Add(Leaf(Tr("Power On"), WledCommandNames.PowerOn, d));
        nodes.Add(Leaf(Tr("Power Off"), WledCommandNames.PowerOff, d));
        nodes.Add(Leaf(Tr("Preset Browser"), WledCommandNames.PresetFolder, d));

        // Groups follow the single commands.
        nodes.Add(new MenuNode
        {
            Name = Tr("Set Brightness"),
            Children = new[] { 10, 25, 50, 75, 100 }
                .Select(p => Leaf($"{p} %", WledCommandNames.SetBrightness, d, ("Brightness", p.ToString())))
                .ToList(),
        });
        nodes.Add(new MenuNode
        {
            Name = Tr("Presets"),
            Children = d.Presets.Count > 0
                ? d.Presets.Select(p => Leaf(p.Value, WledCommandNames.Preset, d, ("Preset", p.Key.ToString()))).ToList()
                : [Leaf(Tr("Preset by id"), WledCommandNames.Preset, d)],
        });
        nodes.Add(new MenuNode
        {
            Name = Tr("Effects"),
            Children =
            [
                Leaf(Tr("Next Effect"), WledCommandNames.EffectNext, d),
                Leaf(Tr("Previous Effect"), WledCommandNames.EffectPrevious, d),
                Leaf(Tr("Random Effect"), WledCommandNames.EffectRandom, d),
                .. NamedLeaves(d.Effects, WledCommandNames.Effect, "Effect", d),
            ],
        });
        nodes.Add(new MenuNode
        {
            Name = Tr("Palettes"),
            Children =
            [
                Leaf(Tr("Next Palette"), WledCommandNames.PaletteNext, d),
                Leaf(Tr("Previous Palette"), WledCommandNames.PalettePrevious, d),
                Leaf(Tr("Random Palette"), WledCommandNames.PaletteRandom, d),
                .. NamedLeaves(d.Palettes, WledCommandNames.Palette, "Palette", d),
            ],
        });
        return nodes;
    }

    private static IEnumerable<MenuNode> NamedLeaves(IReadOnlyList<string> names, string command, string parameter, WledDevice d) =>
        names.Select((name, id) => (name, id))
            .Where(e => e.name is not ("RSVD" or "-" or ""))
            .Select(e => Leaf(e.name, command, d, (parameter, e.id.ToString())));

    private static MenuNode Leaf(string name, string command, WledDevice d, params (string Key, string Value)[] parameters)
    {
        Dictionary<string, string> values = new(StringComparer.OrdinalIgnoreCase) { [WledCommand.DeviceParameter] = d.Mac };
        foreach ((string k, string v) in parameters)
            values[k] = v;
        return new MenuNode { Name = name, CommandName = command, Parameters = values };
    }

    private string Tr(string english) => _host?.Tr(english) ?? english;

    #endregion

    #region Settings page

    public IReadOnlyList<PluginSettingDescriptor> SettingsSchema { get; } =
    [
        new()
        {
            Key = ManualHostsKey,
            Label = "Manual hosts",
            Kind = PluginSettingKind.Text,
            Description = "IP addresses or host names of WLED devices, separated by commas. Only needed when mDNS does not find them.",
            DefaultValue = "",
        },
        new()
        {
            Key = MdnsKey,
            Label = "mDNS discovery",
            Kind = PluginSettingKind.Toggle,
            Description = "Finds WLED devices on the local network automatically.",
            DefaultValue = false,
        },
        new()
        {
            Key = PollSecondsKey,
            Label = "Poll interval (seconds)",
            Kind = PluginSettingKind.Number,
            Description = "How often the device state is read to keep buttons up to date (1-60).",
            DefaultValue = 3L,
        },
    ];

    public IReadOnlyList<PluginSettingAction> SettingsActions =>
    [
        new()
        {
            Label = "Search for devices",
            Invoke = SearchAsync,
        },
    ];

    public void OnSettingsSaved() => ApplySettings();

    private bool MdnsEnabled => _host?.Settings.Get(MdnsKey, false) ?? false;

    private async Task<string> SearchAsync()
    {
        if ((_controller == null) || (_host == null))
            return string.Empty;

        WledDevice?[] manual = await _controller.ResolveManualHostsAsync(_host.Settings.Get<string>(ManualHostsKey));
        int found = MdnsEnabled ? await _controller.Discovery.ScanOnceAsync() : 0;
        int total = _controller.Registry.All.Count;
        int unreachable = manual.Count(d => d == null);

        string result = MdnsEnabled
            ? string.Format(_host.Tr("{0} device(s) found via mDNS, {1} known in total."), found, total)
            : string.Format(_host.Tr("{0} device(s) known."), total);
        if (unreachable > 0)
            result += " " + string.Format(_host.Tr("{0} manual host(s) not reachable."), unreachable);
        return result;
    }

    #endregion
}
