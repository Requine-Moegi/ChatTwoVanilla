using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;

namespace ChatTwo.Ui.Style;

/// <summary>
/// The chat-window style layer. When vanilla style is off, every method is a no-op and Chat 2
/// keeps drawing with ImGui's own window chrome. When it is on, the window background, tabs,
/// input bar, and scrollbar are painted from game UI textures.
/// </summary>
public static class ChatChrome
{
    private static readonly VanillaUldCatalog Catalog = new();
    private static readonly VanillaDrawer Drawer = new();
    private static bool _stylePushed;
    private static int _colorCount;
    private static int _varCount;

    public static bool Enabled => Plugin.Config.VanillaStyle;

    public static VanillaUldCatalog Uld => Catalog;

    public static void Invalidate()
    {
        Drawer.InvalidatePaths();
        Catalog.ForceReload();
    }

    public static IDisposable PushContentFont()
    {
        if (!Enabled)
            return EmptyDispose.Instance;

        return Plugin.Instance.FontManager.PushVanillaAxis();
    }

    public static Vector2 WindowPadding
    {
        get
        {
            var scale = ImGuiHelpers.GlobalScale;
            Catalog.EnsureLoaded();
            var background = Catalog.Background;
            var x = background == null ? 10f : Math.Clamp((int)background.Left, 8, 28);
            return new Vector2(x, 4f) * scale;
        }
    }

    public static float TabHeight
    {
        get
        {
            var scale = ImGuiHelpers.GlobalScale;
            Catalog.EnsureLoaded();
            var part = Catalog.TabActive ?? Catalog.TabInactive;
            if (part == null)
                return 24f * scale;
            return MathF.Max(part.H * scale, 20f * scale);
        }
    }

    public static float ScrollbarSize
    {
        get
        {
            var scale = ImGuiHelpers.GlobalScale;
            Catalog.EnsureLoaded();
            var part = Catalog.ScrollTrack;
            if (part == null)
                return 14f * scale;
            return Math.Clamp(part.W * scale, 10f * scale, 22f * scale);
        }
    }

    public static void PushWindowStyle()
    {
        if (_stylePushed)
            PopWindowStyle();

        Catalog.EnsureLoaded();
        var scale = ImGuiHelpers.GlobalScale;
        var transparent = Vector4.Zero;
        var quietButton = new Vector4(0f, 0f, 0f, 0.18f);
        (ImGuiCol Id, Vector4 Color)[] colors =
        [
            (ImGuiCol.ChildBg, transparent),
            (ImGuiCol.ScrollbarBg, transparent),
            (ImGuiCol.ScrollbarGrab, transparent),
            (ImGuiCol.ScrollbarGrabHovered, transparent),
            (ImGuiCol.ScrollbarGrabActive, transparent),
            (ImGuiCol.FrameBg, transparent),
            (ImGuiCol.FrameBgHovered, transparent),
            (ImGuiCol.FrameBgActive, new Vector4(1f, 1f, 1f, 0.04f)),
            (ImGuiCol.Border, transparent),
            (ImGuiCol.ResizeGrip, transparent),
            (ImGuiCol.ResizeGripHovered, new Vector4(1f, 1f, 1f, 0.15f)),
            (ImGuiCol.ResizeGripActive, new Vector4(1f, 1f, 1f, 0.28f)),
            (ImGuiCol.Button, quietButton),
            (ImGuiCol.ButtonHovered, new Vector4(1f, 1f, 1f, 0.14f)),
            (ImGuiCol.ButtonActive, new Vector4(1f, 1f, 1f, 0.22f)),
        ];

        foreach (var (id, color) in colors)
            ImGui.PushStyleColor(id, color);
        _colorCount = colors.Length;

        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0f);
        ImGui.PushStyleVar(ImGuiStyleVar.ChildBorderSize, 0f);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 0f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 0f);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 0f);
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 0f);
        ImGui.PushStyleVar(ImGuiStyleVar.ScrollbarRounding, 0f);
        ImGui.PushStyleVar(ImGuiStyleVar.ScrollbarSize, ScrollbarSize);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, WindowPadding);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(6f, 3f) * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(6f, 4f) * scale);
        _varCount = 11;
        _stylePushed = true;
    }

    public static void PopWindowStyle()
    {
        if (!_stylePushed)
            return;

        ImGui.PopStyleVar(_varCount);
        ImGui.PopStyleColor(_colorCount);
        _varCount = 0;
        _colorCount = 0;
        _stylePushed = false;
    }

    public static void DrawWindowBackground(float alpha)
    {
        Catalog.EnsureLoaded();
        var min = ImGui.GetWindowPos();
        var max = min + ImGui.GetWindowSize();
        var drawList = ImGui.GetWindowDrawList();
        if (Drawer.Draw(drawList, Catalog.Background, min, max, alpha))
            return;

        drawList.AddRectFilled(min, max, ImGui.GetColorU32(new Vector4(0.04f, 0.05f, 0.07f, alpha * 0.92f)));
        drawList.AddRect(min, max, ImGui.GetColorU32(new Vector4(0.75f, 0.72f, 0.62f, alpha * 0.55f)));
    }

    public static void DrawTab(Vector2 pos, Vector2 size, bool active, bool hovered)
    {
        Catalog.EnsureLoaded();
        var part = active ? Catalog.TabActive ?? Catalog.TabInactive : Catalog.TabInactive ?? Catalog.TabActive;
        var tint = active
            ? Vector4.One
            : hovered
                ? new Vector4(0.9f, 0.9f, 0.9f, 1f)
                : new Vector4(0.68f, 0.68f, 0.68f, 1f);
        var drawList = ImGui.GetWindowDrawList();
        if (!Drawer.Draw(drawList, part, pos, pos + size, 1f, tint))
        {
            var fill = active
                ? new Vector4(0.16f, 0.16f, 0.18f, 0.95f)
                : new Vector4(0.08f, 0.08f, 0.09f, 0.8f);
            drawList.AddRectFilled(pos, pos + size, ImGui.GetColorU32(fill));
        }
    }

    public static void DrawTabCaps(Vector2 rowPos, float rowWidth, float rowHeight)
    {
        Catalog.EnsureLoaded();
        var scale = ImGuiHelpers.GlobalScale;
        var drawList = ImGui.GetWindowDrawList();
        if (Catalog.TabBarStart != null)
        {
            var size = new Vector2(Catalog.TabBarStart.W, Catalog.TabBarStart.H) * scale;
            Drawer.Draw(drawList, Catalog.TabBarStart, rowPos - new Vector2(size.X * 0.35f, 0f), rowPos + new Vector2(size.X * 0.65f, Math.Max(size.Y, rowHeight)), 1f);
        }

        if (Catalog.TabBarEnd != null)
        {
            var size = new Vector2(Catalog.TabBarEnd.W, Catalog.TabBarEnd.H) * scale;
            var end = rowPos + new Vector2(rowWidth, 0f);
            Drawer.Draw(drawList, Catalog.TabBarEnd, end - new Vector2(size.X * 0.65f, 0f), end + new Vector2(size.X * 0.35f, Math.Max(size.Y, rowHeight)), 1f);
        }
    }

    public static void DrawInputBar(Vector2 pos, Vector2 size, float alpha)
    {
        Catalog.EnsureLoaded();
        var drawList = ImGui.GetWindowDrawList();
        if (Drawer.Draw(drawList, Catalog.InputBackground ?? Catalog.Background, pos, pos + size, alpha))
            return;

        drawList.AddRectFilled(pos, pos + size, ImGui.GetColorU32(new Vector4(0f, 0f, 0f, alpha * 0.35f)));
    }

    public static void DrawScrollbar()
    {
        if (!Enabled)
            return;

        var max = ImGui.GetScrollMaxY();
        if (max <= 1f)
            return;

        Catalog.EnsureLoaded();
        var bar = ImGui.GetStyle().ScrollbarSize;
        var winPos = ImGui.GetWindowPos();
        var winSize = ImGui.GetWindowSize();
        var trackPos = new Vector2(winPos.X + winSize.X - bar, winPos.Y);
        var trackSize = new Vector2(bar, winSize.Y);
        var drawList = ImGui.GetWindowDrawList();
        Drawer.Draw(drawList, Catalog.ScrollTrack, trackPos, trackPos + trackSize, 1f);

        var visible = winSize.Y;
        var thumbH = MathF.Max(bar * 2f, visible * (visible / (visible + max)));
        thumbH = MathF.Min(thumbH, visible);
        var fraction = ImGui.GetScrollY() / max;
        var thumbPos = new Vector2(trackPos.X, trackPos.Y + (visible - thumbH) * fraction);
        var thumbSize = new Vector2(bar, thumbH);
        if (!Drawer.Draw(drawList, Catalog.ScrollThumb ?? Catalog.ScrollTrack, thumbPos, thumbPos + thumbSize, 1f, new Vector4(1.15f, 1.15f, 1.15f, 1f)))
            drawList.AddRectFilled(thumbPos, thumbPos + thumbSize, ImGui.GetColorU32(new Vector4(0.75f, 0.75f, 0.75f, 0.45f)));
    }

    public static void DrawResizeGrip(Vector2 windowPos, Vector2 windowSize)
    {
        Catalog.EnsureLoaded();
        if (Catalog.ResizeGrip == null)
            return;

        var scale = ImGuiHelpers.GlobalScale;
        var size = new Vector2(Catalog.ResizeGrip.W, Catalog.ResizeGrip.H) * scale;
        var max = windowPos + windowSize;
        Drawer.Draw(ImGui.GetWindowDrawList(), Catalog.ResizeGrip, max - size, max, 1f);
    }

    private sealed class EmptyDispose : IDisposable
    {
        public static readonly EmptyDispose Instance = new();
        public void Dispose()
        {
        }
    }
}
