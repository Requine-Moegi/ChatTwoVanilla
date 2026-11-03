using System.Numerics;
using ChatTwo.Code;
using ChatTwo.Util;
using Dalamud.Game.Config;

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
