using System.Globalization;
using System.Text.Json.Nodes;
using LoupixDeck.PluginSdk;

namespace LoupixDeck.Plugin.WLED;

/// <summary>Command names. They are persisted in button configs: never rename them.</summary>
public static class WledCommandNames
{
    public const string Power = "Wled.Power";
    public const string PowerOn = "Wled.PowerOn";
    public const string PowerOff = "Wled.PowerOff";
    public const string SetBrightness = "Wled.SetBrightness";
    public const string BrightnessDial = "Wled.BrightnessDial";
    public const string Preset = "Wled.Preset";
    public const string PresetFolder = "Wled.PresetFolder";
    public const string Effect = "Wled.Effect";
    public const string EffectStep = "Wled.EffectStep";
    public const string EffectNext = "Wled.EffectNext";
    public const string EffectPrevious = "Wled.EffectPrevious";
    public const string EffectRandom = "Wled.EffectRandom";
    public const string EffectDial = "Wled.EffectDial";
    public const string Palette = "Wled.Palette";
    public const string PaletteStep = "Wled.PaletteStep";
    public const string PaletteNext = "Wled.PaletteNext";
    public const string PalettePrevious = "Wled.PalettePrevious";
    public const string PaletteRandom = "Wled.PaletteRandom";
    public const string PaletteDial = "Wled.PaletteDial";
    public const string SpeedDial = "Wled.SpeedDial";
    public const string IntensityDial = "Wled.IntensityDial";
}

/// <summary>
/// Base for all WLED commands. The first parameter is always the device MAC. Every entry point
/// is guarded: a failure is logged, never thrown into the host.
/// </summary>
public abstract class WledCommand(WledController controller) : IPluginCommand
{
    public const string Group = "WLED";
    public const string DeviceParameter = "Device";

    protected WledController Controller { get; } = controller;

    public abstract CommandDescriptor Descriptor { get; }

    public virtual ButtonTargets SupportedTargets => ButtonTargets.TouchButton | ButtonTargets.SimpleButton;

    public Task Execute(CommandContext ctx) => Guard(ctx, device => ExecuteOn(device, ctx));

    protected abstract Task ExecuteOn(WledDevice device, CommandContext ctx);

    protected WledDevice? Resolve(CommandContext ctx) =>
        ctx.Parameters.Length > 0 ? Controller.Registry.Find(ctx.Parameters[0]) : null;

    protected async Task Guard(CommandContext ctx, Func<WledDevice, Task> action)
    {
        try
        {
            WledDevice? device = Resolve(ctx);
            if (device == null)
            {
                ctx.Host.Logger.Warn($"{Descriptor.CommandName}: unknown WLED device '{Param(ctx, 0)}'.");
                return;
            }
            await action(device);
        }
        catch (Exception ex)
        {
            ctx.Host.Logger.Error($"{Descriptor.CommandName} failed.", ex);
        }
    }

    protected static string Param(CommandContext ctx, int index) =>
        index < ctx.Parameters.Length ? ctx.Parameters[index].Trim() : string.Empty;

    protected static int IntParam(CommandContext ctx, int index, int fallback) =>
        int.TryParse(Param(ctx, index), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : fallback;

    /// <param name="layout">The look the command brings to a button; null keeps the host's standard look.</param>
    protected static CommandDescriptor Describe(string name, string displayName, string description, string icon,
        ButtonLayoutDescriptor? layout, params CommandParameter[] extra) => new()
    {
        CommandName = name,
        DisplayName = displayName,
        Group = Group,
        Icon = icon,
        Description = description,
        ButtonLayout = layout,
        HiddenFromMenu = true,
        Parameters = [new CommandParameter(DeviceParameter, typeof(string)), .. extra],
        ParameterTemplate = "(" + string.Join(",", new[] { DeviceParameter }.Concat(extra.Select(p => p.Name)).Select(n => "{" + n + "}")) + ")",
    };

    /// <summary>
    /// Patch for all selected segments. A segment object without id (not an array) makes WLED
    /// apply it to every selected segment.
    /// </summary>
    protected static JsonObject SegmentPatch(string key, JsonNode value) =>
        new() { ["seg"] = new JsonObject { [key] = value } };

    /// <summary>"next" / "prev" / "random" as WLED's relative value syntax.</summary>
    protected static string StepValue(string direction) => direction.ToLowerInvariant() switch
    {
        "prev" or "previous" => "~-",
        "random" => "r",
        _ => "~",
    };

    protected static void Overlay(CommandContext ctx, string text)
    {
        if ((ctx.Target != ButtonTargets.RotaryEncoder) || (ctx.SourceIndex is not int rotary))
            return;
        int slot = ctx.Host.GetTouchSlotForRotary(rotary);
        if (slot >= 0)
            ctx.Host.OverlayTouchText(slot, text, TimeSpan.FromSeconds(1.5));
    }
}

/// <summary>Names of the power button states. Persisted in button configs: never rename them.</summary>
public static class WledPowerStates
{
    public const string Off = "Off";
    public const string On = "On";
    public const string Offline = "Offline";

    public static string Of(WledDevice device) => !device.Online ? Offline : device.On ? On : Off;
}

/// <summary>
/// Turns a device on, off or toggles it. The button gets one state per power state, each with its
/// own icon and caption layers; the controller switches the active state.
/// </summary>
public sealed class WledPowerCommand(WledController controller, Func<string, string> translate) : WledCommand(controller)
{
    public override CommandDescriptor Descriptor { get; } = new()
    {
        CommandName = WledCommandNames.Power,
        DisplayName = "Power",
        Group = Group,
        Icon = WledIcons.Power,
        Description = "Turns the WLED device on, off or toggles it (Mode: toggle, on, off).",
        HiddenFromMenu = true,
        Parameters =
        [
            new CommandParameter(DeviceParameter, typeof(string)),
            new CommandParameter("Mode", typeof(string)) { DefaultValue = "toggle" },
        ],
        ParameterTemplate = "({Device},{Mode})",
        // Index 0 is the resting state.
        States =
        [
            State(WledPowerStates.Off, "The device is off", WledIcons.BulbOutline, WledIcons.OffColor, null, translate),
            State(WledPowerStates.On, "The device is on", WledIcons.BulbOn, WledIcons.OnColor, "#3A2A00", translate),
            State(WledPowerStates.Offline, "The device is not reachable", WledIcons.BulbOffline, WledIcons.OfflineColor, null, translate),
        ],
        // Every state brings its own layers.
        ButtonLayout = new ButtonLayoutDescriptor { Mode = ButtonLayoutMode.None },
    };

    protected override Task ExecuteOn(WledDevice device, CommandContext ctx)
    {
        JsonNode on = Param(ctx, 1).ToLowerInvariant() switch
        {
            "on" => true,
            "off" => false,
            _ => "t",
        };
        return Controller.SendAsync(device, new JsonObject { ["on"] = on });
    }

    /// <summary>
    /// A state with an icon above a caption. The caption is translated once: the layout is stored on
    /// the button when the command is assigned.
    /// </summary>
    private static ButtonStateDescriptor State(string name, string description, string glyph, string color,
        string? background, Func<string, string> translate) => new()
    {
        Name = name,
        Description = description,
        Layout = WledIcons.IconWithCaption(glyph, color, translate(name), background),
    };
}

/// <summary>Turns a device on or off. Two commands, so each brings its own icon to the button.</summary>
public sealed class WledSetPowerCommand(WledController controller, bool on) : WledCommand(controller)
{
    public override CommandDescriptor Descriptor { get; } = on
        ? Describe(WledCommandNames.PowerOn, "Power On", "Turns the WLED device on.", WledIcons.BulbOn,
            WledIcons.IconWithCaption(WledIcons.BulbOn, WledIcons.OnColor))
        : Describe(WledCommandNames.PowerOff, "Power Off", "Turns the WLED device off.", WledIcons.BulbOff,
            WledIcons.IconWithCaption(WledIcons.BulbOff, WledIcons.OffColor));

    protected override Task ExecuteOn(WledDevice device, CommandContext ctx) =>
        Controller.SendAsync(device, new JsonObject { ["on"] = on });
}

/// <summary>Sets the brightness to a fixed percentage and turns the device on.</summary>
public sealed class WledSetBrightnessCommand(WledController controller) : WledCommand(controller)
{
    public override CommandDescriptor Descriptor { get; } = Describe(WledCommandNames.SetBrightness, "Set Brightness",
        "Sets the brightness in percent (1-100).", WledIcons.BrightnessSet,
        WledIcons.IconWithCaption(WledIcons.BrightnessSet, WledIcons.BrightnessColor),
        new CommandParameter("Brightness", typeof(int)) { DefaultValue = "50" });

    protected override Task ExecuteOn(WledDevice device, CommandContext ctx)
    {
        int percent = Math.Clamp(IntParam(ctx, 1, 50), 1, 100);
        int bri = Math.Clamp((int)Math.Round(percent * 2.55), 1, 255);
        return Controller.SendAsync(device, new JsonObject { ["on"] = true, ["bri"] = bri });
    }
}

/// <summary>Activates a preset by id.</summary>
public sealed class WledPresetCommand(WledController controller) : WledCommand(controller)
{
    public override CommandDescriptor Descriptor { get; } = Describe(WledCommandNames.Preset, "Activate Preset",
        "Activates a WLED preset by its id.", WledIcons.Preset,
        WledIcons.IconWithCaption(WledIcons.Preset, WledIcons.PresetColor),
        new CommandParameter("Preset", typeof(int)) { DefaultValue = "1" });

    protected override Task ExecuteOn(WledDevice device, CommandContext ctx) =>
        Controller.SendAsync(device, new JsonObject { ["ps"] = Math.Clamp(IntParam(ctx, 1, 1), 1, 250) });
}

/// <summary>Opens a folder listing the presets of the device.</summary>
public sealed class WledPresetFolderCommand(WledController controller) : WledCommand(controller)
{
    public override CommandDescriptor Descriptor { get; } = Describe(WledCommandNames.PresetFolder, "Preset Browser",
        "Opens a folder with all presets and effects of the device.", WledIcons.PresetBrowser,
        WledIcons.IconWithCaption(WledIcons.PresetBrowser, WledIcons.PresetColor));

    protected override Task ExecuteOn(WledDevice device, CommandContext ctx)
    {
        ctx.Host.OpenFolder(WledListFolder.Presets(Controller, device, ctx.Host));
        return Task.CompletedTask;
    }
}

/// <summary>Sets the effect of the selected segments by id.</summary>
public sealed class WledEffectCommand(WledController controller) : WledCommand(controller)
{
    public override CommandDescriptor Descriptor { get; } = Describe(WledCommandNames.Effect, "Set Effect",
        "Sets the effect of the selected segments by its id.", WledIcons.Effect,
        WledIcons.IconWithCaption(WledIcons.Effect, WledIcons.EffectColor),
        new CommandParameter("Effect", typeof(int)) { DefaultValue = "0" });

    protected override Task ExecuteOn(WledDevice device, CommandContext ctx) =>
        Controller.SendAsync(device, SegmentPatch("fx", Math.Max(0, IntParam(ctx, 1, 0))));
}

/// <summary>Sets the palette of the selected segments by id.</summary>
public sealed class WledPaletteCommand(WledController controller) : WledCommand(controller)
{
    public override CommandDescriptor Descriptor { get; } = Describe(WledCommandNames.Palette, "Set Palette",
        "Sets the color palette of the selected segments by its id.", WledIcons.Palette,
        WledIcons.IconWithCaption(WledIcons.Palette, WledIcons.PaletteColor),
        new CommandParameter("Palette", typeof(int)) { DefaultValue = "0" });

    protected override Task ExecuteOn(WledDevice device, CommandContext ctx) =>
        Controller.SendAsync(device, SegmentPatch("pal", Math.Max(0, IntParam(ctx, 1, 0))));
}

/// <summary>
/// Steps the effect or palette in one fixed direction. One command per direction, so each brings
/// its own icon to the button.
/// </summary>
public sealed class WledFixedStepCommand(WledController controller, string name, string displayName, string description,
    string key, string value, string icon, string color) : WledCommand(controller)
{
    public override CommandDescriptor Descriptor { get; } = Describe(name, displayName, description, icon,
        WledIcons.IconWithCaption(icon, color));

    protected override Task ExecuteOn(WledDevice device, CommandContext ctx) =>
        Controller.SendAsync(device, SegmentPatch(key, value));
}

/// <summary>
/// Steps the effect or palette: next, prev or random. Kept for buttons created before the
/// per-direction commands; the menu offers <see cref="WledFixedStepCommand"/> instead.
/// </summary>
public sealed class WledStepCommand(WledController controller, string name, string displayName, string key, string icon,
    string color) : WledCommand(controller)
{
    public override CommandDescriptor Descriptor { get; } = Describe(name, displayName,
        "Switches to the next, previous or a random entry (Direction: next, prev, random).", icon,
        WledIcons.IconWithCaption(icon, color),
        new CommandParameter("Direction", typeof(string)) { DefaultValue = "next" });

    protected override Task ExecuteOn(WledDevice device, CommandContext ctx) =>
        Controller.SendAsync(device, SegmentPatch(key, StepValue(Param(ctx, 1))));
}

/// <summary>Brightness on a dial. A press toggles power.</summary>
public sealed class WledBrightnessDialCommand(WledController controller) : WledCommand(controller), IAdjustmentCommand
{
    private const int Step = 13; // about 5 % of 255 per detent

    public override CommandDescriptor Descriptor { get; } = Describe(WledCommandNames.BrightnessDial, "Brightness",
        "Changes the brightness by dial. Pressing toggles power.", WledIcons.Brightness, null);

    public override ButtonTargets SupportedTargets => ButtonTargets.RotaryEncoder;

    public Task ApplyAdjustment(CommandContext ctx, int ticks) => Guard(ctx, device =>
    {
        if (!device.On && (ticks < 0))
            return Task.CompletedTask;

        // Turning up from off starts at the bottom instead of jumping to the old brightness.
        int start = device.On ? device.Brightness : 0;
        int bri = Math.Clamp(start + (ticks * Step), 1, 255);
        device.Brightness = bri;
        device.On = true;
        Controller.SendLatest(device, "bri", new JsonObject { ["on"] = true, ["bri"] = bri });
        Overlay(ctx, FormatPercent(bri));
        return Task.CompletedTask;
    });

    public Task ApplyReset(CommandContext ctx) => Guard(ctx, device =>
        Controller.SendAsync(device, new JsonObject { ["on"] = "t" }));

    protected override Task ExecuteOn(WledDevice device, CommandContext ctx) =>
        Controller.SendAsync(device, new JsonObject { ["on"] = "t" });

    public AdjustmentValue? GetValue(CommandContext ctx)
    {
        WledDevice? device = Resolve(ctx);
        if (device == null)
            return null;
        if (!device.On)
            return new AdjustmentValue(0, ctx.Host.Tr("Off"));
        return new AdjustmentValue(device.Brightness / 255.0, FormatPercent(device.Brightness));
    }

    private static string FormatPercent(int bri) => $"{(int)Math.Round(bri / 2.55)} %";
}

/// <summary>Effect speed or intensity (0-255) on a dial. A press resets to the WLED default 128.</summary>
public sealed class WledSegmentValueDialCommand(
    WledController controller, string name, string displayName, string description, string key, string icon,
    Func<WledDevice, int> read, Action<WledDevice, int> write)
    : WledCommand(controller), IAdjustmentCommand
{
    private const int Step = 8;
    private const int Default = 128;

    public override CommandDescriptor Descriptor { get; } = Describe(name, displayName, description, icon, null);

    public override ButtonTargets SupportedTargets => ButtonTargets.RotaryEncoder;

    public Task ApplyAdjustment(CommandContext ctx, int ticks) => Guard(ctx, device =>
    {
        int value = Math.Clamp(read(device) + (ticks * Step), 0, 255);
        Set(ctx, device, value);
        return Task.CompletedTask;
    });

    public Task ApplyReset(CommandContext ctx) => Guard(ctx, device =>
    {
        Set(ctx, device, Default);
        return Task.CompletedTask;
    });

    protected override Task ExecuteOn(WledDevice device, CommandContext ctx)
    {
        Set(ctx, device, Default);
        return Task.CompletedTask;
    }

    private void Set(CommandContext ctx, WledDevice device, int value)
    {
        write(device, value);
        Controller.SendLatest(device, key, SegmentPatch(key, value));
        Overlay(ctx, Format(value));
    }

    public AdjustmentValue? GetValue(CommandContext ctx)
    {
        WledDevice? device = Resolve(ctx);
        return device == null ? null : new AdjustmentValue(read(device) / 255.0, Format(read(device)));
    }

    private static string Format(int value) => $"{(int)Math.Round(value / 2.55)} %";
}

/// <summary>Effect or palette selection on a dial. A press picks a random entry.</summary>
public sealed class WledListDialCommand(
    WledController controller, string name, string displayName, string description, string key, string icon,
    Func<WledDevice, int> read, Action<WledDevice, int> write,
    Func<WledDevice, IReadOnlyList<string>> list, Func<WledDevice, int, string> nameOf)
    : WledCommand(controller), IAdjustmentCommand
{
    public override CommandDescriptor Descriptor { get; } = Describe(name, displayName, description, icon, null);

    public override ButtonTargets SupportedTargets => ButtonTargets.RotaryEncoder;

    public Task ApplyAdjustment(CommandContext ctx, int ticks) => Guard(ctx, device =>
    {
        int count = list(device).Count;
        if (count == 0)
        {
            // Names not loaded yet: let WLED step relatively.
            Controller.SendLatest(device, key, SegmentPatch(key, ticks > 0 ? "~" : "~-"));
            return Task.CompletedTask;
        }

        IReadOnlyList<string> names = list(device);
        int value = read(device);
        for (int remaining = Math.Abs(ticks), guard = 0; (remaining > 0) && (guard < count); guard++)
        {
            value = (((value + Math.Sign(ticks)) % count) + count) % count;
            if (!IsReserved(names[value]))
                remaining--;
        }

        write(device, value);
        Controller.SendLatest(device, key, SegmentPatch(key, value));
        Overlay(ctx, nameOf(device, value));
        return Task.CompletedTask;
    });

    public Task ApplyReset(CommandContext ctx) => Guard(ctx, device =>
        Controller.SendAsync(device, SegmentPatch(key, "r")));

    protected override Task ExecuteOn(WledDevice device, CommandContext ctx) =>
        Controller.SendAsync(device, SegmentPatch(key, "r"));

    /// <summary>WLED keeps removed effect ids as placeholder names.</summary>
    private static bool IsReserved(string name) => name is "RSVD" or "-" or "";

    public AdjustmentValue? GetValue(CommandContext ctx)
    {
        WledDevice? device = Resolve(ctx);
        if (device == null)
            return null;
        int count = list(device).Count;
        int value = read(device);
        return new AdjustmentValue(count > 1 ? value / (double)(count - 1) : 0, nameOf(device, value));
    }
}
