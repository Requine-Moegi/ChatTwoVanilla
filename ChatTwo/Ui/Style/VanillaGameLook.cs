using System.Numerics;
using ChatTwo.Code;
using ChatTwo.Util;
using Dalamud.Game.Config;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace ChatTwo.Ui.Style;

/// <summary>
/// Reads the live game settings that the vanilla chat log follows: UI theme, log transparency, and font size.
/// </summary>
public static class VanillaGameLook
{
    public const string UldBase = "ui/uld/";

    /// <summary>
    /// Theme folders used by the client. Index 0 is the unthemed path (<c>ui/uld/...</c>, Dark).
    /// Index n &gt; 0 is <c>ui/uld/img{n:D2}/</c>, matching Dalamud's ULD browser.
    /// The official theme list is Dark, Light, Classic FF, Clear Blue, Clear White, Clear Green,
    /// Clear Grey, Clear Pink. Later themes are requested the same way and fall back when the file is absent.
    /// </summary>
    public static readonly string[] ThemeNames =
    [
        "Dark",
        "Light",
        "Classic FF",
        "Clear Blue",
        "Clear White",
        "Clear Green",
        "Clear Grey",
        "Clear Pink",
    ];

    public static int TextureThemeIndex()
    {
        var color = TryUint(SystemConfigOption.ColorThemeType);
        var asset = TryUint(SystemConfigOption.UiAssetType);
        // ColorThemeType is the theme dropdown (AtkUIColorHolder.ActiveColorThemeType).
        // UiAssetType is the texture-set index. They match after a theme change; if a
        // mod splits them, textures follow the asset index.
        if (asset != null && color != null && asset.Value != color.Value)
            return (int)asset.Value;
        if (color != null)
            return (int)color.Value;
        if (asset != null)
            return (int)asset.Value;
        return 0;
    }

    public static string ThemeName(int index)
        => index >= 0 && index < ThemeNames.Length ? ThemeNames[index] : $"theme {index}";

    /// <summary>
    /// True when the graphics option that selects high-resolution UI textures is not the default.
    /// The value is named TextureRezoType. Zero prefers the standard sheet; any other value prefers <c>_hr1</c>.
    /// The drawer still checks which file exists, and UV scale follows the file that actually loaded.
    /// </summary>
    public static bool PreferHighResolutionTextures()
    {
        var rezo = TryUint(SystemConfigOption.TextureRezoType);
        return rezo is > 0;
    }

    public static uint? ColorThemeType => TryUint(SystemConfigOption.ColorThemeType);
    public static uint? UiAssetType => TryUint(SystemConfigOption.UiAssetType);
    public static uint? TextureRezoType => TryUint(SystemConfigOption.TextureRezoType);

    /// <summary>
    /// Log Window Transparency. The stored permeation rate is treated as 0 (opaque) through 100 (clear).
    /// Values above 100 are treated as a 0–255 byte instead. Text is not multiplied by this alpha.
    /// </summary>
    public static float LogBackgroundAlpha()
    {
        var rate = TryUint(UiConfigOption.LogPermeationRate);
        return PermeationToAlpha(rate);
    }

    public static float LogInputAlpha()
    {
        var rate = TryUint(UiConfigOption.LogPermeationRateInput);
        return rate == null ? LogBackgroundAlpha() : PermeationToAlpha(rate);
    }

    /// <summary>
    /// Factor passed into the log nine-grid. The node's own alpha (127/255 on the log panel) is
    /// multiplied by this afterwards, which is the current look and what the game does to the node.
    /// The override slider replaces the permeation factor. It does not replace the node alpha:
    /// 100% is the texture at its designed translucency, matching the game at permeation 0.
    /// A popped-out tab with its own opacity still replaces this factor for that window.
    /// </summary>
    public static float LogBackgroundOpacityFactor()
    {
        if (Plugin.Config.UseGameLogOpacity)
            return LogBackgroundAlpha();

        return Math.Clamp(Plugin.Config.LogBackgroundOpacity / 100f, 0f, 1f);
    }

    /// <summary>
    /// Color laid over one tab's log background. Null leaves the log texture alone.
    /// The game's log texture is nearly black, so this is not a multiply.
    /// </summary>
    public static Vector4? TabLogBackgroundTint(Tab? tab)
    {
        if (tab is not { LogBackgroundTint: true })
            return null;

        var stored = tab.LogBackgroundColor == 0 ? 0xFFFFFFFFu : tab.LogBackgroundColor;
        var rgb = ColourUtil.RgbaToVector3(stored);
        return new Vector4(rgb, 1f);
    }

    /// <summary>
    /// Chat log font size in the same point-sized unit Chat 2 stores in <c>FontSizeV2</c>.
    /// Current clients appear to store the point size directly (12, 14, 16, …). A small integer
    /// is treated as a slider index into the sizes the game ships.
    /// </summary>
    public static float LogFontSizePt()
    {
        var raw = TryUint(UiConfigOption.LogFontSize);
        if (raw is null or 0)
            return 12f;

        if (raw.Value is >= 8 and <= 36)
            return raw.Value;

        // Older clients stored a slider index rather than a point size.
        ReadOnlySpan<float> ladder = [10f, 12f, 14f, 16f, 18f, 20f, 23f];
        if (raw.Value < (uint)ladder.Length)
            return ladder[(int)raw.Value];

        return 14f;
    }

    /// <summary>
    /// The game's interface scale, from <c>AtkUnitBase.GetGlobalUIScale</c>.
    /// This is the scale the chat log itself uses. It is not Dalamud's global scale,
    /// which can be set higher and was stretching Axis on top of the point size.
    /// </summary>
    public static float GameUiScale()
    {
        try
        {
            var scale = AtkUnitBase.GetGlobalUIScale();
            if (float.IsFinite(scale) && scale is >= 0.25f and <= 4f)
                return scale;
        }
        catch (Exception)
        {
            // The signature is not resolved yet. Callers fall back to 1.
        }

        return 1f;
    }

    /// <summary>
    /// The game keeps a font size per log panel. The chat window follows <c>LogFontSize</c>.
    /// The other values are only here so a log can show when a panel disagrees with it.
    /// </summary>
    public static string DescribeLogFont()
    {
        static string One(string name, UiConfigOption option)
        {
            var raw = TryUint(option);
            return raw == null ? $"{name}=unread" : $"{name}={raw.Value}";
        }

        return $"{One("LogFontSize", UiConfigOption.LogFontSize)} resolved={LogFontSizePt():0.##}pt " +
               $"{One("LogFontSizeForm", UiConfigOption.LogFontSizeForm)} " +
               $"{One("Log2", UiConfigOption.LogFontSizeLog2)} " +
               $"{One("Log3", UiConfigOption.LogFontSizeLog3)} " +
               $"{One("Log4", UiConfigOption.LogFontSizeLog4)}";
    }

    public static IEnumerable<string> CandidatePaths(VanillaPart part, int theme, bool preferHd)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in RawCandidates(part, theme))
        {
            var hr = ToHr1(path);
            var sd = StripHr1(path);
            var ordered = preferHd ? new[] { hr, sd } : new[] { sd, hr };
            foreach (var candidate in ordered)
            {
                if (seen.Add(candidate))
                    yield return candidate;
            }
        }
    }

    public static Vector4? ResolveChannelColor(ChatType type)
    {
        if (Plugin.Config.ChatColours.TryGetValue(type, out var overridden))
            return ColourUtil.RgbaToVector4(overridden);

        if (Plugin.Config.VanillaStyle)
        {
            var game = Plugin.Instance.Functions.Chat.GetChannelColor(type);
            if (game != null)
                return ColourUtil.RgbaToVector4(game);
        }

        return ColourUtil.RgbaToVector4(type.DefaultColor());
    }

    /// <summary>
    /// The channel the input box should follow right now, including a temporary /command switch.
    /// </summary>
    public static ChatType ActiveInputChatType(Tab tab, string chatInput)
    {
        var type = tab.CurrentChannel.UseTempChannel
            ? tab.CurrentChannel.TempChannel.ToChatType()
            : tab.CurrentChannel.Channel.ToChatType();

        if (!chatInput.Trim().StartsWith('/'))
            return type;

        var command = chatInput.Split(' ')[0];
        if (Plugin.Instance.Commands.TextCommandChannels.TryGetValue(command, out var mapped))
            return mapped;

        return type;
    }

    /// <summary>
    /// Color written into the dark pixels of the input texture, mixed toward the channel hue by
    /// the intensity slider. Null leaves the texture alone: the setting is off, the intensity is
    /// zero, or the channel is white or gray (Say's default). The light rim is not recolored.
    /// </summary>
    public static Vector4? InputBackgroundTint(ChatType type, float intensityPercent)
    {
        var amount = Math.Clamp(intensityPercent / 100f, 0f, 1f);
        if (amount <= 0.001f)
            return null;

        var color = ResolveChannelColor(type);
        if (color == null)
            return null;

        var r = color.Value.X;
        var g = color.Value.Y;
        var b = color.Value.Z;
        var max = MathF.Max(r, MathF.Max(g, b));
        var min = MathF.Min(r, MathF.Min(g, b));
        var delta = max - min;
        // Say's default is (247, 247, 247). Gray system text is the same. Those stay untinted.
        if (max <= 0.001f || delta / max < 0.12f)
            return null;

        float hue;
        if (r >= g && r >= b)
            hue = (g - b) / delta;
        else if (g >= r && g >= b)
            hue = (b - r) / delta + 2f;
        else
            hue = (r - g) / delta + 4f;
        hue = ((hue % 6f) + 6f) % 6f / 6f;

        // The untinted box is nearly black. Intensity moves that floor toward a dark channel hue
        // so the interior reads as that channel without turning into a bright field.
        var floor = new Vector3(0.035f, 0.035f, 0.04f);
        var vivid = HsvToRgb(hue, 0.9f, 0.34f);
        var rgb = Vector3.Lerp(floor, vivid, amount);
        return new Vector4(rgb, 1f);
    }

    private static Vector3 HsvToRgb(float h, float s, float v)
    {
        var sector = h * 6f;
        var i = (int)MathF.Floor(sector);
        var f = sector - i;
        var p = v * (1f - s);
        var q = v * (1f - s * f);
        var t = v * (1f - s * (1f - f));
        return (i % 6) switch
        {
            0 => new Vector3(v, t, p),
            1 => new Vector3(q, v, p),
            2 => new Vector3(p, v, t),
            3 => new Vector3(p, q, v),
            4 => new Vector3(t, p, v),
            _ => new Vector3(v, p, q),
        };
    }

    private static IEnumerable<string> RawCandidates(VanillaPart part, int theme)
    {
        var path = part.BasePath.Replace('\\', '/');
        if (theme > 0
            && part.AssetsHaveThemeSupport
            && (part.ThemeMask & (1 << (theme - 1))) != 0
            && path.StartsWith(UldBase, StringComparison.OrdinalIgnoreCase))
        {
            yield return $"{UldBase}img{theme:D2}/{path[UldBase.Length..]}";
        }

        yield return path;
    }

    private static float PermeationToAlpha(uint? rate)
    {
        if (rate == null)
            return Plugin.Config.WindowAlpha / 100f;

        if (rate.Value <= 100)
            return 1f - rate.Value / 100f;

        return 1f - Math.Clamp(rate.Value, 0, 255) / 255f;
    }

    private static string ToHr1(string path)
    {
        if (path.Contains("_hr1.", StringComparison.OrdinalIgnoreCase))
            return path;

        var dot = path.LastIndexOf('.');
        return dot < 0 ? path : path[..dot] + "_hr1" + path[dot..];
    }

    private static string StripHr1(string path)
        => path.Replace("_hr1.", ".", StringComparison.OrdinalIgnoreCase);

    private static uint? TryUint(SystemConfigOption option)
        => Plugin.GameConfig.TryGet(option, out uint value) ? value : null;

    private static uint? TryUint(UiConfigOption option)
        => Plugin.GameConfig.TryGet(option, out uint value) ? value : null;
}
