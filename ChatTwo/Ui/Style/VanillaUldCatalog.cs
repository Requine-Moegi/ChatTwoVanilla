using System.Numerics;
using System.Text;
using Lumina.Data.Files;
using Lumina.Data.Parsing.Uld;
using UldComponent = Lumina.Data.Parsing.Uld.ComponentData;
using UldImage = Lumina.Data.Parsing.Uld.NodeData.ImageNode;
using UldNine = Lumina.Data.Parsing.Uld.NodeData.NineGridNode;
using UldNode = Lumina.Data.Parsing.Uld.UldRoot.NodeData;

namespace ChatTwo.Ui.Style;

/// <summary>
/// Loads <c>ui/uld/ChatLog.uld</c> and <c>ui/uld/ChatLogPanel.uld</c> and picks the parts that make up
/// the chat window. Part rectangles come from the ULD, so a patch that moves pixels is picked up
/// without a hardcoded UV table. Which node is the background versus a tab is a layout heuristic
/// and is logged for in-game confirmation.
/// </summary>
public sealed class VanillaUldCatalog
{
    private const int ImageNodeType = 2;
    private const int NineGridNodeType = 4;

    private static readonly string[] UldPaths =
    [
        "ui/uld/ChatLog.uld",
        "ui/uld/ChatLogPanel.uld",
    ];

    private int _loadedTheme = int.MinValue;
    private bool _loadedHd;
    private bool _loaded;

    public VanillaPart? Background { get; private set; }
    public VanillaPart? TabInactive { get; private set; }
    public VanillaPart? TabActive { get; private set; }
    public VanillaPart? TabBarStart { get; private set; }
    public VanillaPart? TabBarEnd { get; private set; }
    public VanillaPart? InputBackground { get; private set; }
    public VanillaPart? ScrollTrack { get; private set; }
    public VanillaPart? ScrollThumb { get; private set; }
    public VanillaPart? ResizeGrip { get; private set; }
    public string Summary { get; private set; } = "Vanilla ULD catalog has not been loaded.";
    public IReadOnlyList<string> TexturePaths { get; private set; } = [];

    public void EnsureLoaded()
    {
        var theme = VanillaGameLook.TextureThemeIndex();
        var hd = VanillaGameLook.PreferHighResolutionTextures();
        if (_loaded && _loadedTheme == theme && _loadedHd == hd)
            return;

        _loadedTheme = theme;
        _loadedHd = hd;
        _loaded = true;
        Load();
    }

    public void ForceReload()
    {
        _loaded = false;
        EnsureLoaded();
    }

    private void Load()
    {
        Background = TabInactive = TabActive = TabBarStart = TabBarEnd = null;
        InputBackground = ScrollTrack = ScrollThumb = ResizeGrip = null;

        var widgetNines = new List<Found>();
        var widgetImages = new List<Found>();
        var tabParts = new List<Found>();
        var inputParts = new List<Found>();
        var scrollParts = new List<Found>();
        var buttonInstances = new List<(UldNode Node, UldFile File, UldComponent.ComponentType Type)>();
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var notes = new StringBuilder();

        foreach (var path in UldPaths)
        {
            if (!Plugin.DataManager.FileExists(path))
            {
                notes.AppendLine($"missing {path}");
                continue;
            }

            UldFile file;
            try
            {
                file = Plugin.DataManager.GetFile<UldFile>(path)!;
            }
            catch (Exception ex)
            {
                notes.AppendLine($"failed {path}: {ex.Message}");
                Plugin.Log.Warning(ex, "Vanilla style failed to parse {Path}", path);
                continue;
            }

            if (file == null)
            {
                notes.AppendLine($"empty {path}");
                continue;
            }

            notes.AppendLine($"loaded {path} assets={file.AssetData?.Length ?? 0} parts={file.Parts?.Length ?? 0} components={file.Components?.Length ?? 0} nodes={file.WidgetData.Nodes?.Length ?? 0} themeSupport={file.WidgetData.AssetsHaveThemeSupport}");

            if (file.AssetData != null)
            {
                foreach (var asset in file.AssetData)
                {
                    var assetPath = Clean(asset.Path);
                    if (assetPath.Length > 0)
                        paths.Add(assetPath);
                }
            }

            var components = file.Components ?? [];
            if (file.WidgetData.Nodes != null)
            {
                foreach (var node in file.WidgetData.Nodes)
                {
                    CollectGraphic(file, node, "widget", widgetNines, widgetImages, tabParts, inputParts, scrollParts);
                    if (node.NodeType > 1000)
                    {
                        var component = FindComponent(components, (uint)node.NodeType);
                        if (component != null)
                            buttonInstances.Add((node, file, component.Value.Type));
                    }
                }
            }

            foreach (var component in components)
            {
                if (component.Nodes == null)
                    continue;

                var role = RoleFor(component.Type);
                if (role.Length == 0)
                    continue;

                foreach (var node in component.Nodes)
                    CollectGraphic(file, node, role, widgetNines, widgetImages, tabParts, inputParts, scrollParts);
            }
        }

        Background = Largest(widgetNines) ?? Largest(widgetNines.Concat(inputParts).Concat(tabParts));
        (TabInactive, TabActive) = PickTabPair(tabParts);
        (TabBarStart, TabBarEnd) = PickCaps(widgetImages);
        InputBackground = Largest(inputParts);
        (ScrollTrack, ScrollThumb) = PickScroll(scrollParts);
        ResizeGrip = PickResize(buttonInstances);

        TexturePaths = paths.Order(StringComparer.OrdinalIgnoreCase).ToArray();
        Summary = BuildSummary(notes);
        Plugin.Log.Information("Vanilla style ULD catalog:\n{Summary}", Summary);
    }

    private string BuildSummary(StringBuilder notes)
    {
        var report = new StringBuilder();
        report.AppendLine($"theme={VanillaGameLook.ThemeName(VanillaGameLook.TextureThemeIndex())} ({VanillaGameLook.TextureThemeIndex()}) colorTheme={Fmt(VanillaGameLook.ColorThemeType)} uiAsset={Fmt(VanillaGameLook.UiAssetType)} textureRezo={Fmt(VanillaGameLook.TextureRezoType)}");
        report.AppendLine($"logAlpha={VanillaGameLook.LogBackgroundAlpha():0.00} inputAlpha={VanillaGameLook.LogInputAlpha():0.00} fontPt={VanillaGameLook.LogFontSizePt():0.##}");
        report.Append(notes);
        report.AppendLine("background: " + (Background?.Describe() ?? "(none, flat fallback)"));
        report.AppendLine("tab inactive: " + (TabInactive?.Describe() ?? "(none)"));
        report.AppendLine("tab active: " + (TabActive?.Describe() ?? "(none)"));
        report.AppendLine("tab cap start: " + (TabBarStart?.Describe() ?? "(none)"));
        report.AppendLine("tab cap end: " + (TabBarEnd?.Describe() ?? "(none)"));
        report.AppendLine("input: " + (InputBackground?.Describe() ?? "(none)"));
        report.AppendLine("scroll track: " + (ScrollTrack?.Describe() ?? "(none)"));
        report.AppendLine("scroll thumb: " + (ScrollThumb?.Describe() ?? "(none)"));
        report.AppendLine("resize: " + (ResizeGrip?.Describe() ?? "(none)"));
        report.AppendLine("textures:");
        foreach (var path in TexturePaths)
            report.AppendLine("  " + path);
        return report.ToString();
    }

    private static void CollectGraphic(
        UldFile file,
        UldNode node,
        string role,
        List<Found> widgetNines,
        List<Found> widgetImages,
        List<Found> tabParts,
        List<Found> inputParts,
        List<Found> scrollParts)
    {
        if (!TryMakePart(file, node, role, out var part))
            return;

        var found = new Found(node, part);
        switch (role)
        {
            case "widget" when node.NodeType == NineGridNodeType:
                widgetNines.Add(found);
                break;
            case "widget" when node.NodeType == ImageNodeType:
                widgetImages.Add(found);
                break;
            case "tab":
                tabParts.Add(found);
                break;
            case "input":
                inputParts.Add(found);
                break;
            case "scroll":
                scrollParts.Add(found);
                break;
        }
    }

    private static bool TryMakePart(UldFile file, UldNode node, string role, out VanillaPart part)
    {
        part = null!;
        uint listId;
        uint partId;
        var nine = false;
        short left = 0, top = 0, right = 0, bottom = 0;

        switch (node.Node)
        {
            case UldImage image:
                listId = image.PartListId;
                partId = image.PartId;
                break;
            case UldNine grid:
                listId = grid.PartListId;
                partId = grid.PartId;
                nine = true;
                left = grid.LeftOffset;
                top = grid.TopOffset;
                right = grid.RightOffset;
                bottom = grid.BottomOffset;
                break;
            default:
                return false;
        }

        if (file.Parts == null)
            return false;

        Lumina.Data.Parsing.Uld.UldRoot.PartsData? list = null;
        foreach (var candidate in file.Parts)
        {
            if (candidate.Id != listId)
                continue;
            list = candidate;
            break;
        }

        if (list == null || list.Value.Parts == null || partId >= list.Value.Parts.Length)
            return false;

        var rect = list.Value.Parts[partId];
        if (file.AssetData == null)
            return false;

        string? texture = null;
        byte mask = 0;
        foreach (var asset in file.AssetData)
        {
            if (asset.Id != rect.TextureId)
                continue;
            texture = Clean(asset.Path);
            mask = asset.ThemeSupportBitmask;
            break;
        }

        if (string.IsNullOrWhiteSpace(texture))
            return false;

        part = new VanillaPart
        {
            BasePath = texture,
            ThemeMask = mask,
            AssetsHaveThemeSupport = file.WidgetData.AssetsHaveThemeSupport,
            U = rect.U,
            V = rect.V,
            W = rect.W,
            H = rect.H,
            Left = left,
            Top = top,
            Right = right,
            Bottom = bottom,
            IsNineGrid = nine,
            NodeAlpha = node.Alpha == 0 ? 1f : node.Alpha / 255f,
            Multiply = new Vector3(Norm(node.MultiplyRed), Norm(node.MultiplyGreen), Norm(node.MultiplyBlue)),
            Source = $"{role}#{node.NodeId} {node.W}x{node.H}@{node.X},{node.Y}",
        };
        return part.W > 0 && part.H > 0;
    }

    private static (VanillaPart? Inactive, VanillaPart? Active) PickTabPair(List<Found> tabs)
    {
        var distinct = DistinctParts(tabs);
        if (distinct.Count == 0)
            return (null, null);
        if (distinct.Count == 1)
            return (distinct[0], distinct[0]);

        // The selected face is usually the next part of a similar size. Prefer the larger
        // of the two closest-sized parts as the active face; the other is inactive.
        VanillaPart? inactive = null;
        VanillaPart? active = null;
        var bestDelta = int.MaxValue;
        for (var i = 0; i < distinct.Count; i++)
        {
            for (var j = i + 1; j < distinct.Count; j++)
            {
                var a = distinct[i];
                var b = distinct[j];
                var delta = Math.Abs(a.W - b.W) + Math.Abs(a.H - b.H);
                if (delta >= bestDelta)
                    continue;
                bestDelta = delta;
                if (a.W * a.H >= b.W * b.H)
                {
                    active = a;
                    inactive = b;
                }
                else
                {
                    active = b;
                    inactive = a;
                }
            }
        }

        return (inactive ?? distinct[0], active ?? distinct[0]);
    }

    private static (VanillaPart? Start, VanillaPart? End) PickCaps(List<Found> images)
    {
        var caps = images
            .Where(found => found.Node.H is > 0 and <= 48 && found.Node.W is > 0 and <= 80 && found.Node.Y <= 24)
            .OrderBy(found => found.Node.X)
            .ToList();
        if (caps.Count == 0)
            return (null, null);
        if (caps.Count == 1)
            return (caps[0].Part, null);
        return (caps[0].Part, caps[^1].Part);
    }

    private static (VanillaPart? Track, VanillaPart? Thumb) PickScroll(List<Found> parts)
    {
        if (parts.Count == 0)
            return (null, null);

        var ordered = parts
            .Select(found => found.Part)
            .OrderByDescending(part => (long)part.W * part.H)
            .ToList();
        var track = ordered[0];
        VanillaPart? thumb = null;
        foreach (var part in ordered.Skip(1))
        {
            if (part.H <= track.H && part.W <= track.W + 4)
            {
                thumb = part;
                break;
            }
        }

        return (track, thumb);
    }

    private static VanillaPart? PickResize(List<(UldNode Node, UldFile File, UldComponent.ComponentType Type)> instances)
    {
        var buttons = instances
            .Where(entry => entry.Type == UldComponent.ComponentType.Button && entry.Node.W is > 0 and <= 64 && entry.Node.H is > 0 and <= 64)
            .OrderByDescending(entry => entry.Node.X + entry.Node.Y)
            .ToList();
        if (buttons.Count == 0)
            return null;

        var chosen = buttons[0];
        var component = FindComponent(chosen.File.Components ?? [], (uint)chosen.Node.NodeType);
        if (component?.Nodes == null)
            return null;

        VanillaPart? best = null;
        var bestArea = 0;
        foreach (var node in component.Value.Nodes)
        {
            if (!TryMakePart(chosen.File, node, "resize", out var part))
                continue;
            var area = part.W * part.H;
            if (area <= bestArea)
                continue;
            bestArea = area;
            best = part;
        }

        return best;
    }

    private static UldRoot.ComponentData? FindComponent(UldRoot.ComponentData[] components, uint id)
    {
        foreach (var component in components)
        {
            if (component.Id == id)
                return component;
        }

        return null;
    }

    private static List<VanillaPart> DistinctParts(List<Found> found)
    {
        var list = new List<VanillaPart>();
        foreach (var item in found)
        {
            if (list.Any(existing => existing.BasePath == item.Part.BasePath && existing.U == item.Part.U && existing.V == item.Part.V && existing.W == item.Part.W && existing.H == item.Part.H))
                continue;
            list.Add(item.Part);
        }

        return list;
    }

    private static VanillaPart? Largest(IEnumerable<Found> parts)
    {
        VanillaPart? best = null;
        var bestArea = 0L;
        foreach (var found in parts)
        {
            var area = (long)found.Part.W * found.Part.H;
            if (area <= bestArea)
                continue;
            bestArea = area;
            best = found.Part;
        }

        return best;
    }

    private static string RoleFor(UldComponent.ComponentType type) => type switch
    {
        UldComponent.ComponentType.Tabbed => "tab",
        UldComponent.ComponentType.TextInput => "input",
        UldComponent.ComponentType.ScrollBar => "scroll",
        _ => "",
    };

    private static float Norm(short value)
    {
        if (value <= 0)
            return 1f;
        if (value <= 100)
            return value / 100f;
        return Math.Clamp(value / 255f, 0f, 1f);
    }

    private static string Clean(char[] chars)
    {
        var end = Array.IndexOf(chars, '\0');
        var text = end >= 0 ? new string(chars, 0, end) : new string(chars);
        return text.Replace('\\', '/').Trim();
    }

    private static string Fmt(uint? value) => value?.ToString() ?? "unread";

    private readonly record struct Found(UldNode Node, VanillaPart Part);
}
