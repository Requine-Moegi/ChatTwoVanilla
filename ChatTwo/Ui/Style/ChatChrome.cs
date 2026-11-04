using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;

namespace ChatTwo.Ui.Style;

/// <summary>
/// The chat-window style layer. When vanilla style is off, every method is a no-op and Chat 2
/// keeps drawing with ImGui's own window chrome. When it is on, the window background, bottom
/// tab strip, input bar, and scrollbar are painted from game UI textures.
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
                return 26f * scale;
            var node = part.NodeH > 0 ? part.NodeH : part.H;
            return Math.Clamp(node * scale, 22f * scale, 34f * scale);
        }
    }

    public static float InputHeight
    {
        get
        {
            var scale = ImGuiHelpers.GlobalScale;
            Catalog.EnsureLoaded();
            var part = Catalog.InputBackground;
            if (part == null)
                return 28f * scale;
            var node = part.NodeH > 0 ? part.NodeH : part.H;
            return Math.Clamp(node * scale, 22f * scale, 36f * scale);
        }
    }

    public static float ButtonSize => TabHeight;

    public static float ChromeGap => 2f * ImGuiHelpers.GlobalScale;

    public static float ChannelLabelHeight => ImGui.GetTextLineHeight();

    /// <summary>Channel name plus the framed input, not including the gap under the input.</summary>
    public static float InputBlockHeight => ChannelLabelHeight + ChromeGap + InputHeight;

    public static float TabRowHeight => Math.Max(TabHeight, ButtonSize);

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

        var fill = Math.Clamp(Math.Max(alpha, 0.45f), 0f, 0.92f);
        drawList.AddRectFilled(min, max, ImGui.GetColorU32(new Vector4(0.05f, 0.05f, 0.06f, fill)));
        drawList.AddRect(min, max, ImGui.GetColorU32(new Vector4(0.62f, 0.58f, 0.46f, 0.85f)), 0f, ImDrawFlags.None, 2f);
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
                ? new Vector4(0.28f, 0.26f, 0.22f, 0.95f)
                : new Vector4(0.12f, 0.11f, 0.1f, 0.9f);
            var rounding = size.Y * 0.45f;
            drawList.AddRectFilled(pos, pos + size, ImGui.GetColorU32(fill), rounding);
            drawList.AddRect(pos, pos + size, ImGui.GetColorU32(new Vector4(0.72f, 0.66f, 0.5f, active ? 0.95f : 0.45f)), rounding);
        }
    }

    public static void DrawActiveDot(Vector2 tabPos, Vector2 tabSize, bool unread)
    {
        Catalog.EnsureLoaded();
        var scale = ImGuiHelpers.GlobalScale;
        var center = new Vector2(tabPos.X + tabSize.X * 0.5f, tabPos.Y + 3f * scale);
        var tint = unread ? new Vector4(1f, 0.78f, 0.28f, 1f) : Vector4.One;
        if (Catalog.TabDot != null)
        {
            var d = Math.Max(6f, Catalog.TabDot.W) * scale;
            if (Drawer.Draw(ImGui.GetWindowDrawList(), Catalog.TabDot, center - new Vector2(d * 0.5f, d * 0.5f), center + new Vector2(d * 0.5f, d * 0.5f), 1f, tint))
                return;
        }

        var color = unread
            ? new Vector4(0.95f, 0.78f, 0.28f, 1f)
            : new Vector4(0.93f, 0.9f, 0.78f, 1f);
        ImGui.GetWindowDrawList().AddCircleFilled(center, 2.4f * scale, ImGui.GetColorU32(color));
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
        if (Drawer.Draw(drawList, Catalog.InputBackground, pos, pos + size, alpha))
            return;

        var rounding = size.Y * 0.35f;
        drawList.AddRectFilled(pos, pos + size, ImGui.GetColorU32(new Vector4(0.02f, 0.02f, 0.025f, Math.Max(alpha, 0.8f))), rounding);
        drawList.AddRect(pos, pos + size, ImGui.GetColorU32(new Vector4(0.55f, 0.5f, 0.38f, 0.9f)), rounding);
    }

    public static void DrawScrollbar()
    {
        if (!Enabled)
            return;

        Catalog.EnsureLoaded();
        var bar = ImGui.GetStyle().ScrollbarSize;
        var winPos = ImGui.GetWindowPos();
        var winSize = ImGui.GetWindowSize();
        var trackPos = new Vector2(winPos.X + winSize.X - bar, winPos.Y);
        var trackSize = new Vector2(bar, winSize.Y);
        var drawList = ImGui.GetWindowDrawList();
        if (!Drawer.Draw(drawList, Catalog.ScrollTrack, trackPos, trackPos + trackSize, 1f))
            drawList.AddRectFilled(trackPos, trackPos + trackSize, ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.35f)), bar * 0.4f);

        var max = ImGui.GetScrollMaxY();
        if (max <= 1f)
            return;

        var visible = winSize.Y;
        var thumbH = MathF.Max(bar * 2f, visible * (visible / (visible + max)));
        thumbH = MathF.Min(thumbH, visible);
        var fraction = ImGui.GetScrollY() / max;
        var thumbPos = new Vector2(trackPos.X, trackPos.Y + (visible - thumbH) * fraction);
        var thumbSize = new Vector2(bar, thumbH);
        if (!Drawer.Draw(drawList, Catalog.ScrollThumb ?? Catalog.ScrollTrack, thumbPos, thumbPos + thumbSize, 1f, new Vector4(1.15f, 1.15f, 1.15f, 1f)))
            drawList.AddRectFilled(thumbPos, thumbPos + thumbSize, ImGui.GetColorU32(new Vector4(0.75f, 0.72f, 0.6f, 0.7f)), bar * 0.4f);
    }

    public static void DrawResizeGrip(Vector2 windowPos, Vector2 windowSize)
    {
        Catalog.EnsureLoaded();
        var scale = ImGuiHelpers.GlobalScale;
        var max = windowPos + windowSize;
        if (Catalog.ResizeGrip != null)
        {
            var native = new Vector2(Math.Max(Catalog.ResizeGrip.NodeW, Catalog.ResizeGrip.W), Math.Max(Catalog.ResizeGrip.NodeH, Catalog.ResizeGrip.H)) * scale;
            var size = new Vector2(
                Math.Clamp(native.X, 12f * scale, 22f * scale),
                Math.Clamp(native.Y, 12f * scale, 22f * scale));
            if (Drawer.Draw(ImGui.GetWindowDrawList(), Catalog.ResizeGrip, max - size, max, 1f))
                return;
        }

        var grip = ImGui.GetWindowDrawList();
        var color = ImGui.GetColorU32(new Vector4(0.85f, 0.8f, 0.65f, 0.8f));
        for (var i = 0; i < 3; i++)
        {
            var offset = (i + 1) * 4f * scale;
            grip.AddLine(new Vector2(max.X - offset, max.Y - 2f * scale), new Vector2(max.X - 2f * scale, max.Y - offset), color, 1.4f);
        }
    }

    public enum VanillaGlyph
    {
        Bubble,
        Plus,
        Gear,
        Cursor,
        Close,
        Left,
        Right,
    }

    public static bool RoundButton(string id, VanillaGlyph glyph, float size, bool enabled = true)
    {
        var clicked = ImGui.InvisibleButton(id, new Vector2(size, size));
        var min = ImGui.GetItemRectMin();
        var alpha = enabled ? 1f : 0.35f;
        DrawRoundFace(min, size, enabled && ImGui.IsItemHovered(), alpha);
        DrawGlyph(min, size, glyph, alpha);
        return enabled && clicked;
    }

    public static void DrawChannelIcon(Vector2 pos, float size)
    {
        Catalog.EnsureLoaded();
        var drawList = ImGui.GetWindowDrawList();
        if (Catalog.ChannelIcon != null && Drawer.Draw(drawList, Catalog.ChannelIcon, pos, pos + new Vector2(size, size), 1f))
            return;

        DrawGlyph(pos, size, VanillaGlyph.Bubble);
    }

    private static void DrawRoundFace(Vector2 pos, float size, bool hovered, float alpha)
    {
        Catalog.EnsureLoaded();
        var tint = hovered ? new Vector4(1.15f, 1.15f, 1.15f, 1f) : Vector4.One;
        if (Drawer.Draw(ImGui.GetWindowDrawList(), Catalog.CircleButton, pos, pos + new Vector2(size, size), alpha, tint))
            return;

        var center = pos + new Vector2(size * 0.5f, size * 0.5f);
        var radius = size * 0.42f;
        var drawList = ImGui.GetWindowDrawList();
        drawList.AddCircleFilled(center, radius, ImGui.GetColorU32(new Vector4(0.1f, 0.09f, 0.08f, 0.92f * alpha)));
        drawList.AddCircle(center, radius, ImGui.GetColorU32(new Vector4(0.75f, 0.68f, 0.48f, (hovered ? 1f : 0.75f) * alpha)), 0, 1.5f);
    }

    private static void DrawGlyph(Vector2 pos, float size, VanillaGlyph glyph, float alpha = 1f)
    {
        var drawList = ImGui.GetWindowDrawList();
        var color = ImGui.GetColorU32(new Vector4(0.93f, 0.9f, 0.78f, alpha));
        var center = pos + new Vector2(size * 0.5f, size * 0.5f);
        var u = Math.Max(1f, size * 0.08f);
        switch (glyph)
        {
            case VanillaGlyph.Plus:
                drawList.AddLine(center - new Vector2(u * 2.2f, 0), center + new Vector2(u * 2.2f, 0), color, 1.6f);
                drawList.AddLine(center - new Vector2(0, u * 2.2f), center + new Vector2(0, u * 2.2f), color, 1.6f);
                break;
            case VanillaGlyph.Close:
                drawList.AddLine(center - new Vector2(u * 1.8f, u * 1.8f), center + new Vector2(u * 1.8f, u * 1.8f), color, 1.6f);
                drawList.AddLine(center + new Vector2(-u * 1.8f, u * 1.8f), center + new Vector2(u * 1.8f, -u * 1.8f), color, 1.6f);
                break;
            case VanillaGlyph.Left:
                drawList.AddLine(center + new Vector2(u, -u * 1.6f), center + new Vector2(-u * 0.8f, 0), color, 1.6f);
                drawList.AddLine(center + new Vector2(-u * 0.8f, 0), center + new Vector2(u, u * 1.6f), color, 1.6f);
                break;
            case VanillaGlyph.Right:
                drawList.AddLine(center + new Vector2(-u, -u * 1.6f), center + new Vector2(u * 0.8f, 0), color, 1.6f);
                drawList.AddLine(center + new Vector2(u * 0.8f, 0), center + new Vector2(-u, u * 1.6f), color, 1.6f);
                break;
            case VanillaGlyph.Gear:
                drawList.AddCircle(center, u * 1.3f, color, 0, 1.4f);
                for (var i = 0; i < 6; i++)
                {
                    var angle = i * MathF.PI / 3f;
                    var dir = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
                    drawList.AddLine(center + dir * u * 1.5f, center + dir * u * 2.4f, color, 1.5f);
                }
                break;
            case VanillaGlyph.Cursor:
                var tip = center + new Vector2(-u * 1.6f, -u * 1.8f);
                var a = center + new Vector2(-u * 0.2f, u * 1.6f);
                var b = center + new Vector2(u * 0.5f, u * 0.2f);
                drawList.AddTriangleFilled(tip, a, b, color);
                break;
            case VanillaGlyph.Bubble:
                drawList.AddRectFilled(center - new Vector2(u * 2.2f, u * 1.5f), center + new Vector2(u * 2.2f, u * 1.1f), color, u);
                drawList.AddTriangleFilled(center + new Vector2(-u * 0.4f, u * 0.8f), center + new Vector2(-u * 1.6f, u * 2.2f), center + new Vector2(u * 0.6f, u * 0.8f), color);
                break;
        }
    }

    private sealed class EmptyDispose : IDisposable
    {
        public static readonly EmptyDispose Instance = new();
        public void Dispose()
        {
        }
    }
}
