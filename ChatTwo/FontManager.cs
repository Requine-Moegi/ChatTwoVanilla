using System.Text;
using ChatTwo.Http;
using Dalamud.Interface;
using Dalamud.Interface.GameFonts;
using Dalamud.Interface.ManagedFontAtlas;
using Dalamud.Interface.Utility;
using Dalamud.Bindings.ImGui;

namespace ChatTwo;

public class FontManager
{
    public IFontHandle Axis = null!;
    public IFontHandle AxisItalic = null!;

    private IFontHandle? _vanillaAxis;
    private readonly Dictionary<int, IFontHandle> _fixedAxis = new();
    private IFontHandle? _vanillaAxisItalic;
    private float _vanillaAxisPt = -1f;
    private bool _loggedLogPush;
    private VanillaFontReport _lastLogFont;

    /// <summary>
    /// What the last vanilla log-text push actually put on the ImGui font stack.
    /// </summary>
    public readonly record struct VanillaFontReport(
        float RequestedPt,
        float RequestedPx,
        bool Ready,
        string? LoadError,
        bool Pushed,
        float DrawnPx,
        float GlobalScale,
        string Source);

    public IFontHandle RegularFont = null!;
    public IFontHandle? ItalicFont;

    public IFontHandle FontAwesome = null!;

    public readonly byte[] GameSymFont;

    private ushort[] Ranges = [];
    private ushort[] JpRange = [];

    public static readonly HashSet<float> AxisFontSizeList =
    [
        9.6f, 10f, 12f, 14f, 16f,
        18f, 18.4f, 20f, 23f, 34f,
        36f, 40f, 45f, 46f, 68f, 90f,
    ];

    public FontManager()
    {
        var filePath = Path.Combine(Plugin.Interface.ConfigDirectory.FullName, "FFXIV_Lodestone_SSF.ttf");
        if (File.Exists(filePath))
        {
            GameSymFont = File.ReadAllBytes(filePath);
        }
        else
        {
            GameSymFont = ServerCore.HttpClient.GetAsync("https://img.finalfantasyxiv.com/lds/pc/global/fonts/FFXIV_Lodestone_SSF.ttf")
                .Result
                .Content
                .ReadAsByteArrayAsync()
                .Result;

            Dalamud.Utility.FilesystemUtil.WriteAllBytesSafe(filePath, GameSymFont);
        }
    }

    private unsafe void SetUpRanges()
    {
        ushort[] BuildRange(IReadOnlyList<ushort>? chars, params nint[] ranges)
        {
            var builder = new ImFontGlyphRangesBuilderPtr(ImGuiNative.ImFontGlyphRangesBuilder());
            // text
            foreach (var range in ranges)
                builder.AddRanges((ushort*)range);

            // chars
            if (chars != null)
            {
                for (var i = 0; i < chars.Count; i += 2)
                {
                    if (chars[i] == 0)
                        break;

                    for (var j = (uint) chars[i]; j <= chars[i + 1]; j++)
                        builder.AddChar((ushort) j);
                }
            }

            // Ingame supported ranges
            var reader = new FdtReader(Plugin.DataManager.GetFile("common/font/axis_12.fdt")!.Data);
            foreach (var c in reader.Glyphs)
                builder.AddChar(c.Char);

            // various symbols
            // French
            // Romanian
            // builder.AddText("←→↑↓《》■※☀★★☆♥♡ヅツッシ☀☁☂℃℉°♀♂♠♣♦♣♧®©™€$£♯♭♪✓√◎◆◇♦■□〇●△▽▼▲‹›≤≥<«“”─＼～");
            builder.AddText("Œœ");
            builder.AddText("ĂăÂâÎîȘșȚț");

            // "Enclosed Alphanumerics" (partial) https://www.compart.com/en/unicode/block/U+2460
            for (var i = 0x2460; i <= 0x24B5; i++)
                builder.AddChar((char) i);

            builder.AddChar('⓪');
            return builder.BuildRangesToArray();
        }

        var ranges = new List<nint> { (nint)ImGui.GetIO().Fonts.GetGlyphRangesDefault() };
        foreach (var extraRange in Enum.GetValues<ExtraGlyphRanges>())
            if (Plugin.Config.ExtraGlyphRanges.HasFlag(extraRange))
                ranges.Add(extraRange.Range());

        Ranges = BuildRange(null, ranges.ToArray());
        JpRange = BuildRange(GlyphRangesJapanese.GlyphRanges);
    }

    public void BuildFonts()
    {
        SetUpRanges();

        Axis = Plugin.Interface.UiBuilder.FontAtlas.NewGameFontHandle(new GameFontStyle(GameFontFamily.Axis, SizeInPx(Plugin.Config.FontSizeV2)));
        AxisItalic = Plugin.Interface.UiBuilder.FontAtlas.NewGameFontHandle(new GameFontStyle(GameFontFamily.Axis, SizeInPx(Plugin.Config.FontSizeV2))
        {
            SkewStrength = SizeInPx(Plugin.Config.FontSizeV2) / 6
        });

        FontAwesome = Plugin.Interface.UiBuilder.FontAtlas.NewDelegateFontHandle(e =>
        {
            e.OnPreBuild(tk => tk.AddFontAwesomeIconFont(new SafeFontConfig { SizePx = GetFontSize() }));
            e.OnPostBuild(tk => tk.FitRatio(tk.Font));
        });

        RegularFont = Plugin.Interface.UiBuilder.FontAtlas.NewDelegateFontHandle(
            e => e.OnPreBuild(
                tk =>
                {
                    var config = new SafeFontConfig {SizePt = Plugin.Config.GlobalFontV2.SizePt, GlyphRanges = Ranges};
                    config.MergeFont = Plugin.Config.GlobalFontV2.FontId.AddToBuildToolkit(tk, config);

                    config.SizePt = Plugin.Config.JapaneseFontV2.SizePt;
                    config.GlyphRanges = JpRange;
                    Plugin.Config.JapaneseFontV2.FontId.AddToBuildToolkit(tk, config);

                    config.SizePt = Plugin.Config.SymbolsFontSizeV2;
                    tk.AddGameSymbol(config);

                    tk.Font = config.MergeFont;
                }
            ));

        if (Plugin.Config.ItalicEnabled)
        {
            ItalicFont = Plugin.Interface.UiBuilder.FontAtlas.NewDelegateFontHandle(
                e => e.OnPreBuild(
                    tk =>
                    {
                        var config = new SafeFontConfig {SizePt = Plugin.Config.ItalicFontV2.SizePt, GlyphRanges = Ranges};
                        config.MergeFont = Plugin.Config.ItalicFontV2.FontId.AddToBuildToolkit(tk, config);

                        config.SizePt = Plugin.Config.JapaneseFontV2.SizePt;
                        config.GlyphRanges = JpRange;
                        Plugin.Config.JapaneseFontV2.FontId.AddToBuildToolkit(tk, config);

                        config.SizePt = Plugin.Config.SymbolsFontSizeV2;
                        tk.AddGameSymbol(config);

                        tk.Font = config.MergeFont;
                    }
                ));
        }
        else
        {
            ItalicFont = null;
        }
    }

    public static float SizeInPt(float px) => (float) (px * 3.0 / 4.0);
    public static float SizeInPx(float pt) => (float) (pt * 4.0 / 3.0);
    public static float GetFontSize() => Plugin.Config.VanillaStyle
        ? SizeInPx(Ui.Style.VanillaGameLook.LogFontSizePt())
        : Plugin.Config.FontsEnabled ? Plugin.Config.GlobalFontV2.SizePx : SizeInPx(Plugin.Config.FontSizeV2);

    /// <summary>
    /// Axis at the game's chat-log size. Rebuilt only when that size changes.
    /// The handle is created with an explicit pixel size. <see cref="IFontHandle.Push"/> is skipped
    /// until the atlas has built the glyphs: pushing early puts a null font on the stack and the
    /// user's Dalamud font stays. The next frame, once <see cref="IFontHandle.Available"/> is set, switches to Axis.
    /// </summary>
    public IDisposable PushVanillaAxis()
    {
        EnsureVanillaAxis();
        return PushIfReady(_vanillaAxis!, _vanillaAxisPt, recordLog: true, "game Axis");
    }

    /// <summary>
    /// Same Axis handle, without replacing the chat window's last-push readout.
    /// </summary>
    public IDisposable PushVanillaAxisPreview()
    {
        EnsureVanillaAxis();
        return PushIfReady(_vanillaAxis!, _vanillaAxisPt, recordLog: false, "game Axis");
    }

    /// <summary>
    /// Italic Axis at the same log size. Returns a no-op until that handle is built, so the
    /// regular Axis pushed around the line stays up for that frame.
    /// </summary>
    public IDisposable PushVanillaAxisItalic()
    {
        EnsureVanillaAxis();
        return PushIfReady(_vanillaAxisItalic!, _vanillaAxisPt, recordLog: false, "game Axis italic");
    }

    /// <summary>
    /// Axis at a fixed point size. The chat-log ULD uses 14 for the channel name and 12 for tab text,
    /// independent of the log font size. Until the handle is ready, the caller keeps the font already pushed.
    /// </summary>
    public IDisposable PushFixedAxis(float pt)
    {
        var key = (int)Math.Round(pt * 100f);
        if (!_fixedAxis.TryGetValue(key, out var handle))
        {
            handle = NewAxis(SizeInPx(pt));
            _fixedAxis[key] = handle;
        }

        return PushIfReady(handle, pt, recordLog: false, "game Axis");
    }

    /// <summary>
    /// Readout for <c>/chat2vanillaDebugger</c>. Includes the handle the chat last pushed, not only
    /// the font the debugger window itself is using.
    /// </summary>
    public string DescribeVanillaFont()
    {
        EnsureVanillaAxis();
        var log = _vanillaAxis!;
        var italic = _vanillaAxisItalic!;
        var report = new StringBuilder();
        report.AppendLine($"requested {_vanillaAxisPt:0.##} pt ({SizeInPx(_vanillaAxisPt):0.##} px) via IFontAtlas.NewGameFontHandle(Axis)");
        report.AppendLine($"handle ready={log.Available} italic ready={italic.Available} globalScale={ImGuiHelpers.GlobalScale:0.##}");
        if (log.LoadException != null)
            report.AppendLine($"load error: {log.LoadException.Message}");
        if (_loggedLogPush)
            report.AppendLine($"last log push: {_lastLogFont.Source}, drawn {_lastLogFont.DrawnPx:0.##} px, ready={_lastLogFont.Ready}, pushed={_lastLogFont.Pushed}");
        else
            report.AppendLine("last log push: the chat window has not drawn yet");
        foreach (var (key, handle) in _fixedAxis.OrderBy(pair => pair.Key))
            report.AppendLine($"fixed Axis {key / 100f:0.##} pt ready={handle.Available} error={handle.LoadException?.Message ?? "none"}");
        return report.ToString().TrimEnd();
    }

    public IFontHandle VanillaAxisItalic
    {
        get
        {
            EnsureVanillaAxis();
            return _vanillaAxisItalic!;
        }
    }

    private void EnsureVanillaAxis()
    {
        var pt = Ui.Style.VanillaGameLook.LogFontSizePt();
        if (_vanillaAxis != null && _vanillaAxisItalic != null && Math.Abs(_vanillaAxisPt - pt) < 0.05f)
            return;

        _vanillaAxis?.Dispose();
        _vanillaAxisItalic?.Dispose();
        _vanillaAxisPt = pt;

        var px = SizeInPx(pt);
        _vanillaAxis = NewAxis(px);
        _vanillaAxisItalic = NewAxis(px, italicSkew: px / 6f);
        _loggedLogPush = false;
    }

    /// <summary>
    /// Explicit pixel size. Dalamud's atlas then scales the glyphs for <see cref="ImGuiHelpers.GlobalScale"/>
    /// so the drawn size matches the rest of the vanilla chrome. The point size is the game's log font size,
    /// not the Fonts-tab face and not the user's Dalamud UI font.
    /// </summary>
    private static IFontHandle NewAxis(float sizePx, float italicSkew = 0f)
    {
        var style = new GameFontStyle(GameFontFamily.Axis, sizePx);
        if (italicSkew > 0f)
            style.SkewStrength = italicSkew;
        return Plugin.Interface.UiBuilder.FontAtlas.NewGameFontHandle(style);
    }

    private IDisposable PushIfReady(IFontHandle handle, float pt, bool recordLog, string source)
    {
        var px = SizeInPx(pt);
        var scale = ImGuiHelpers.GlobalScale;
        if (!handle.Available)
        {
            if (recordLog)
                Remember(pt, px, false, handle.LoadException?.Message, false, ImGui.GetFontSize(), scale, "fallback (atlas not ready, keeping the current font)");
            return NopDispose.Instance;
        }

        var pushed = handle.Push();
        var drawn = ImGui.GetFontSize();
        var match = FontMatches(handle) ? source : "pushed, but the active font pointer differs";
        if (recordLog)
            Remember(pt, px, true, handle.LoadException?.Message, true, drawn, scale, match);
        return pushed;
    }

    private void Remember(float pt, float px, bool ready, string? error, bool pushed, float drawn, float scale, string source)
    {
        _loggedLogPush = true;
        _lastLogFont = new VanillaFontReport(pt, px, ready, error, pushed, drawn, scale, source);
    }

    private static unsafe bool FontMatches(IFontHandle handle)
    {
        using var locked = handle.Lock();
        return ImGui.GetFont().Handle == locked.ImFont.Handle;
    }

    private sealed class NopDispose : IDisposable
    {
        public static readonly NopDispose Instance = new();
        public void Dispose()
        {
        }
    }
}
