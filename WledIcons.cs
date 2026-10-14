using LoupixDeck.PluginSdk;

namespace LoupixDeck.Plugin.WLED;

/// <summary>
/// Material Design Icons code points (checked against the host's mdi-catalog.json) and the
/// icon-and-caption look the key commands bring to a button.
/// </summary>
public static class WledIcons
{
    public const string Group = "\U000F1051";          // mdi-led-strip-variant
    public const string Power = "\U000F0425";          // mdi-power
    public const string BulbOn = "\U000F06E8";         // mdi-lightbulb-on
    public const string BulbOff = "\U000F0E4F";        // mdi-lightbulb-off
    public const string BulbOutline = "\U000F0336";    // mdi-lightbulb-outline
    public const string BulbOffline = "\U000F0E50";    // mdi-lightbulb-off-outline
    public const string Brightness = "\U000F00DF";     // mdi-brightness-6
    public const string BrightnessSet = "\U000F0CF2";  // mdi-brightness-percent
    public const string Preset = "\U000F04CE";         // mdi-star
    public const string PresetBrowser = "\U000F1286";  // mdi-star-box-multiple
    public const string Effect = "\U000F0068";         // mdi-auto-fix
    public const string Palette = "\U000F08B5";        // mdi-palette-swatch
    public const string Speed = "\U000F04C5";          // mdi-speedometer
    public const string Intensity = "\U000F0196";      // mdi-contrast-box
    public const string Next = "\U000F04AD";           // mdi-skip-next
    public const string Previous = "\U000F04AE";       // mdi-skip-previous
    public const string Random = "\U000F076E";         // mdi-dice-multiple

    // Icon tints: the icon tells the command family apart at a glance.
    public const string OnColor = "#FFB300";
    public const string OffColor = "#8A8A8A";
    public const string OfflineColor = "#5A5A5A";
    public const string BrightnessColor = "#FFD54F";
    public const string PresetColor = "#FFC107";
    public const string EffectColor = "#E040FB";
    public const string PaletteColor = "#4FC3F7";

    // Pixel values for a 90 px key, the host's own icon-and-caption geometry; the host scales them.
    private const double IconScale = 0.5;
    private const int IconOffsetY = -9;
    private const int CaptionSize = 11;
    private const int CaptionOffsetY = 27;
    private const int CaptionBoxWidth = 88;
    private const int CaptionBoxHeight = 22;

    /// <summary>A tinted icon above a caption. A null caption keeps the name the host shows.</summary>
    public static ButtonLayoutDescriptor IconWithCaption(string glyph, string color, string? caption = null,
        string? background = null) => new()
    {
        Mode = ButtonLayoutMode.Custom,
        BackgroundColor = background,
        Layers =
        [
            new ButtonLayerDescriptor { Kind = ButtonLayerKind.Symbol, Glyph = glyph, Color = color, IconScale = IconScale, OffsetY = IconOffsetY },
            new ButtonLayerDescriptor
            {
                Kind = ButtonLayerKind.Text, Text = caption, TextSize = CaptionSize,
                OffsetY = CaptionOffsetY, BoxWidth = CaptionBoxWidth, BoxHeight = CaptionBoxHeight,
            },
        ],
    };
}
