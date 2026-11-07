using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;

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

    public static Vector2 WindowPadding => Vector2.Zero;

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

    /// <summary>
    /// The visible scrollbar is ScrollBarB, drawn by hand on the left of the log.
    /// ImGui's own bar stays a pixel wide and fully transparent so it does not sit in the text.
    /// </summary>
    public static float ScrollbarSize => 1f;

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
            (ImGuiCol.WindowBg, transparent),
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

    private sealed class ScrollState
    {
        public float Y;
        public float Max;
        public float View = 1f;
        public float? DragFraction;
    }

    // The message child and the parent share one entry. BeginScrollDrag records the parent
    // window, and NoteScroll (inside the child) writes that same entry.
    private static readonly Dictionary<uint, ScrollState> Scrolls = new();
    private static uint _scrollWindow;

    private static ScrollState CurrentScroll()
    {
        if (!Scrolls.TryGetValue(_scrollWindow, out var state))
            Scrolls[_scrollWindow] = state = new ScrollState();
        return state;
    }

    public static VanillaFrame Frame()
        => VanillaFrame.From(ImGui.GetWindowPos(), ImGui.GetWindowSize(), ImGuiHelpers.GlobalScale);

    public static bool DrawPart(VanillaPart? part, Vector2 min, Vector2 max, float alpha = 1f, Vector4? tint = null)
    {
        Catalog.EnsureLoaded();
        return Drawer.Draw(ImGui.GetWindowDrawList(), part, min, max, alpha, tint);
    }

    public static void DrawLogBackground(VanillaFrame frame, float? permeation = null, Vector4? tint = null)
    {
        Catalog.EnsureLoaded();
        var rect = frame.Background;
        var drawList = ImGui.GetWindowDrawList();
        var alphaArg = permeation ?? VanillaGameLook.LogBackgroundOpacityFactor();
        // The log texture stays its own color. A multiply cannot tint it: the texels are nearly black.
        var drew = Drawer.Draw(drawList, Catalog.Background, rect.Min, rect.Max, alphaArg, null);
        if (!drew)
        {
            // Soft edge, no hard border. The real part is a 20px fade into black.
            var fade = 18f * frame.Scale;
            var alpha = 0.5f * alphaArg;
            drawList.AddRectFilled(rect.Min, rect.Max, ImGui.GetColorU32(new Vector4(0f, 0f, 0f, alpha * 0.35f)));
            var inner = new Vector2(rect.X + fade, rect.Y + fade);
            var innerMax = new Vector2(rect.X + rect.W - fade, rect.Y + rect.H - fade);
            if (innerMax.X > inner.X && innerMax.Y > inner.Y)
                drawList.AddRectFilled(inner, innerMax, ImGui.GetColorU32(new Vector4(0f, 0f, 0f, alpha)));
        }

        if (tint != null)
            DrawLogOverlay(drawList, rect, Catalog.Background, frame.Scale, alphaArg, tint.Value);
    }

    /// <summary>
    /// Visible rim of ChatLog part 17, in design pixels, measured from the part PNG.
    /// The texture is 28×28. Outside the light rim is a transparent margin. The dark
    /// interior starts 3px in from the sides, 4px from the top, and 5px from the bottom.
    /// The nine-grid offsets are 13 and are the corner slices, not this rim.
    /// A 6px corner radius stays inside that inner edge at the top and the bottom.
    /// </summary>
    private const float InputRimLeft = 3f;
    private const float InputRimRight = 3f;
    private const float InputRimTop = 4f;
    private const float InputRimBottom = 5f;
    private const float InputRimRadius = 6f;

    public static void DrawInputFrame(VanillaRect rect, float? permeation = null, Vector4? tint = null)
    {
        Catalog.EnsureLoaded();
        var drawList = ImGui.GetWindowDrawList();
        var alpha = permeation ?? VanillaGameLook.LogInputAlpha();
        var part = Catalog.InputBackground;
        var scale = ImGuiHelpers.GlobalScale;
        // Ceiling keeps the fill on the interior side of the measured rim when a
        // half-pixel would otherwise round outward onto the frame.
        float Snap(float design) => MathF.Ceiling(design * scale - 0.001f);

        var drewFrame = Drawer.Draw(drawList, part, rect.Min, rect.Max, alpha);
        if (tint == null)
        {
            if (drewFrame)
                return;
            var rounding = rect.H * 0.45f;
            drawList.AddRectFilled(rect.Min, rect.Max, ImGui.GetColorU32(new Vector4(0.02f, 0.02f, 0.02f, 0.92f * alpha)), rounding);
            drawList.AddRect(rect.Min, rect.Max, ImGui.GetColorU32(new Vector4(0.75f, 0.75f, 0.72f, 0.9f * alpha)), rounding, ImDrawFlags.None, 1.25f);
            return;
        }

        var left = Snap(InputRimLeft);
        var right = Snap(InputRimRight);
        var top = Snap(InputRimTop);
        var bottom = Snap(InputRimBottom);
        var fillMin = new Vector2(rect.Min.X + left, rect.Min.Y + top);
        var fillMax = new Vector2(rect.Max.X - right, rect.Max.Y - bottom);
        if (fillMax.X - fillMin.X >= 1f && fillMax.Y - fillMin.Y >= 1f)
        {
            var radius = MathF.Min(Snap(InputRimRadius), MathF.Min(fillMax.X - fillMin.X, fillMax.Y - fillMin.Y) * 0.5f);
            var fill = new Vector4(
                tint.Value.X,
                tint.Value.Y,
                tint.Value.Z,
                Math.Clamp(alpha, 0f, 1f) * Math.Clamp(tint.Value.W, 0f, 1f));
            // The part's interior texels are opaque black, so this fill is what colors
            // the box. It is inset to the inner edge of the light rim.
            drawList.AddRectFilled(fillMin, fillMax, ImGui.GetColorU32(fill), radius);
        }

        // Paint the real rim again, on top of the fill, so antialiasing at the rounded
        // edge cannot color the frame. The clip is only the measured rim band. The
        // black interior is left as the fill.
        if (drewFrame)
        {
            DrawInputRimBand(drawList, part, rect, alpha, left, right, top, bottom);
            return;
        }

        var fallbackRadius = MathF.Min(rect.H * 0.45f, rect.H * 0.5f);
        drawList.AddRect(rect.Min, rect.Max, ImGui.GetColorU32(new Vector4(0.75f, 0.75f, 0.72f, 0.9f * alpha)), fallbackRadius, ImDrawFlags.None, 1.25f);
    }

    private static void DrawInputRimBand(ImDrawListPtr drawList, VanillaPart? part, VanillaRect rect, float alpha, float left, float right, float top, float bottom)
    {
        void Band(Vector2 min, Vector2 max)
        {
            if (max.X - min.X < 0.5f || max.Y - min.Y < 0.5f)
                return;
            drawList.PushClipRect(min, max, true);
            Drawer.Draw(drawList, part, rect.Min, rect.Max, alpha);
            drawList.PopClipRect();
        }

        var min = rect.Min;
        var max = rect.Max;
        Band(min, new Vector2(max.X, min.Y + top));
        Band(new Vector2(min.X, max.Y - bottom), max);
        Band(new Vector2(min.X, min.Y + top), new Vector2(min.X + left, max.Y - bottom));
        Band(new Vector2(max.X - right, min.Y + top), new Vector2(max.X, max.Y - bottom));
    }

    /// <summary>
    /// Color laid over the log panel. Alpha follows the same permeation and texture alpha as the
    /// nine-grid, so the opacity slider fades the tint with the panel. No tint leaves this undrawn.
    /// </summary>
    private static void DrawLogOverlay(ImDrawListPtr drawList, VanillaRect rect, VanillaPart? part, float scale, float permeation, Vector4 color)
    {
        var corner = part is { IsNineGrid: true } ? Math.Max(part.Left, Math.Max(part.Top, Math.Max(part.Right, part.Bottom))) : 20;
        if (corner <= 0)
            corner = 20;
        var inset = MathF.Max(4f, corner * 0.35f) * scale;
        var min = new Vector2(rect.X + inset, rect.Y + inset);
        var max = new Vector2(rect.X + rect.W - inset, rect.Y + rect.H - inset);
        if (max.X - min.X < 2f || max.Y - min.Y < 2f)
            return;

        var nodeAlpha = part?.NodeAlpha ?? 127f / 255f;
        var coverage = Math.Clamp(permeation, 0f, 1f) * nodeAlpha;
        if (coverage <= 0.001f)
            return;

        var rounding = Math.Clamp(corner * scale - inset, 0f, Math.Min(max.X - min.X, max.Y - min.Y) * 0.5f);
        var fill = new Vector4(color.X, color.Y, color.Z, coverage);
        drawList.AddRectFilled(min, max, ImGui.GetColorU32(fill), rounding);
    }

    public static void DrawTabFace(VanillaRect rect, bool hovered)
    {
        Catalog.EnsureLoaded();
        var tint = hovered ? new Vector4(1.12f, 1.12f, 1.12f, 1f) : Vector4.One;
        var drawList = ImGui.GetWindowDrawList();
        if (Drawer.Draw(drawList, Catalog.TabActive ?? Catalog.TabInactive, rect.Min, rect.Max, 1f, tint))
            return;

        var rounding = rect.H * 0.35f;
        drawList.AddRectFilled(rect.Min, rect.Max, ImGui.GetColorU32(new Vector4(0.22f, 0.22f, 0.22f, 0.95f)), rounding);
    }

    public static void DrawTabMarker(VanillaRect tab)
    {
        Catalog.EnsureLoaded();
        var scale = ImGuiHelpers.GlobalScale;
        // Component 1011 node 2: the 28px marker is at (-6, -4). The diamond in that
        // sheet then sits at the top-left of the tab, not in the middle of the label.
        var size = 28f * scale;
        var pos = new Vector2(MathF.Round(tab.X - 6f * scale), MathF.Round(tab.Y - 4f * scale));
        if (Drawer.Draw(ImGui.GetWindowDrawList(), Catalog.TabDot, pos, pos + new Vector2(size, size), 1f))
            return;

        ImGui.GetWindowDrawList().AddCircleFilled(
            pos + new Vector2(13.5f * scale, 14f * scale),
            3f * scale,
            ImGui.GetColorU32(new Vector4(0.95f, 0.9f, 0.75f, 1f)));
    }

    /// <summary>
    /// One rounded cap at each end of the whole strip. The right cap is the v6 placement:
    /// flipped part 15, 28px, starting at the strip's right edge. The left cap is that
    /// same sprite mirrored, hanging off the strip's left edge.
    /// </summary>
    public static void DrawTabCaps(VanillaRect strip)
    {
        Catalog.EnsureLoaded();
        var scale = ImGuiHelpers.GlobalScale;
        var size = new Vector2(28f * scale, 28f * scale);
        var drawList = ImGui.GetWindowDrawList();
        var right = new Vector2(strip.X + strip.W, strip.Y);
        var left = new Vector2(strip.X - size.X, strip.Y);
        drawList.PushClipRect(left - new Vector2(2f, 2f), right + size + new Vector2(2f, 2f), false);
        Drawer.Draw(drawList, Catalog.TabBarEnd, left, left + size, 1f, flipH: false);
        Drawer.Draw(drawList, Catalog.TabBarEnd, right, right + size, 1f);
        drawList.PopClipRect();
    }

    /// <summary>
    /// Hit target on a strip end cap. The glyph is drawn only when that side has hidden tabs.
    /// </summary>
    public static bool TabCapArrow(string id, Vector2 pos, float size, bool left)
    {
        ImGui.SetCursorScreenPos(pos);
        var clicked = ImGui.InvisibleButton(id, new Vector2(size, size));
        // Part 15's opaque rounded cap inside the 28px sheet is x 7..28, y 3..26.
        // The right cap is that sheet mirrored, so its visible center is mirrored too.
        // The glyph is centered on that, not on the sprite's transparent padding.
        var unit = size / 28f;
        var center = (left ? new Vector2(17.5f, 14.5f) : new Vector2(10.5f, 14.5f)) * unit;
        var glyph = 16f * unit;
        var glyphPos = pos + center - new Vector2(glyph * 0.5f, glyph * 0.5f);
        DrawGlyph(new Vector2(MathF.Round(glyphPos.X), MathF.Round(glyphPos.Y)), glyph, left ? VanillaGlyph.Left : VanillaGlyph.Right, 0.95f);
        return clicked;
    }

    public static bool ArrowButton(string id, VanillaGlyph glyph, Vector2 pos, float size, bool enabled)
    {
        ImGui.SetCursorScreenPos(pos);
        var clicked = ImGui.InvisibleButton(id, new Vector2(size, size));
        DrawGlyph(pos, size, glyph, enabled ? 0.95f : 0.3f);
        return enabled && clicked;
    }

    public static bool DrawCircleIcon(string id, VanillaPart? part, VanillaRect rect, VanillaGlyph fallback)
    {
        ImGui.SetCursorScreenPos(rect.Min);
        var clicked = ImGui.InvisibleButton(id, rect.Size);
        var hovered = ImGui.IsItemHovered();
        var tint = hovered ? new Vector4(1.2f, 1.2f, 1.2f, 1f) : Vector4.One;
        Catalog.EnsureLoaded();
        if (!Drawer.Draw(ImGui.GetWindowDrawList(), part, rect.Min, rect.Max, 1f, tint))
        {
            DrawRoundFace(rect.Min, rect.W, hovered, 1f);
            DrawGlyph(rect.Min, rect.W, fallback);
        }

        return clicked;
    }

    private sealed class GripDrag
    {
        public bool Dragging;
        public Vector2 StartMouse;
        public Vector2 StartSize;
        public Vector2 StartPos;
    }

    // One drag per ImGui window. A single static drag made every open log apply the same
    // SetWindowPos/SetWindowSize, so resizing the main window moved each pop-out onto it.
    private static readonly Dictionary<uint, GripDrag> GripDrags = new();

    public static void DrawGrip(VanillaFrame frame)
    {
        Catalog.EnsureLoaded();
        var windowKey = ImGui.GetID("vanilla-grip");
        if (!GripDrags.TryGetValue(windowKey, out var drag))
            GripDrags[windowKey] = drag = new GripDrag();

        var sprite = frame.Grip;
        // The message child covers most of this sprite and would otherwise take the hover.
        // A later child sits above it. Hover, glow, and drag all use this same padded rect.
        var pad = 4f * frame.Scale;
        var hit = new VanillaRect(sprite.X - pad, sprite.Y - pad, sprite.W + pad * 2f, sprite.H + pad * 2f);
        var parentPos = ImGui.GetWindowPos();
        var parentSize = ImGui.GetWindowSize();

        var hovered = false;
        var activated = false;
        ImGui.SetCursorScreenPos(hit.Min);
        using (var hitWindow = ImRaii.Child(
                   "##vanilla-grip-hit",
                   hit.Size,
                   false,
                   ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoNav))
        {
            if (hitWindow)
            {
                ImGui.SetCursorPos(Vector2.Zero);
                ImGui.InvisibleButton("##vanilla-resize-grip", ImGui.GetContentRegionAvail());
                hovered = ImGui.IsItemHovered();
                activated = ImGui.IsItemActivated();
            }

            var hotNow = hovered || drag.Dragging;
            if (hotNow)
                ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeNesw);

            // Drawn in this child so the arrow stays above log text. The child is only the hit rect.
            var drawList = ImGui.GetWindowDrawList();
            var idle = new Vector4(0.55f, 0.55f, 0.55f, 1f);
            var under = Drawer.Draw(drawList, Catalog.ResizeUnder, sprite.Min, sprite.Max, 1f, hotNow ? Vector4.One : idle);
            var over = hotNow && Drawer.Draw(drawList, Catalog.ResizeGrip, sprite.Min, sprite.Max, 1f);
            if (!under && !over)
            {
                var color = ImGui.GetColorU32(hotNow
                    ? new Vector4(0.95f, 0.78f, 0.28f, 0.95f)
                    : new Vector4(0.45f, 0.45f, 0.45f, 0.7f));
                var max = sprite.Max;
                var scale = frame.Scale;
                for (var i = 0; i < 3; i++)
                {
                    var offset = (i + 1) * 4f * scale;
                    drawList.AddLine(new Vector2(max.X - offset, sprite.Y + 2f * scale), new Vector2(max.X - 2f * scale, sprite.Y + offset), color, 1.4f);
                }
            }
        }

        // Top-right of the log. Dragging grows to the right and up, so the bottom-left corner stays put.
        // ImGui's own grip is disabled with NoResize while vanilla style is on.
        var io = ImGui.GetIO();
        if (activated)
        {
            drag.Dragging = true;
            drag.StartMouse = io.MousePos;
            drag.StartSize = parentSize;
            drag.StartPos = parentPos;
        }

        if (!drag.Dragging)
            return;

        if (!io.MouseDown[0])
        {
            drag.Dragging = false;
            return;
        }

        if (ImGui.IsWindowDocked())
            return;

        var delta = io.MousePos - drag.StartMouse;
        var minimum = new Vector2(280f, 168f) * frame.Scale;
        var newW = Math.Max(minimum.X, drag.StartSize.X + delta.X);
        var newH = Math.Max(minimum.Y, drag.StartSize.Y - delta.Y);
        ImGui.SetWindowPos(new Vector2(drag.StartPos.X, drag.StartPos.Y + drag.StartSize.Y - newH));
        ImGui.SetWindowSize(new Vector2(newW, newH));
    }

    public static void BeginScrollDrag(VanillaRect track)
    {
        // The click is taken by the button in PaintScrollbar, after this frame's child has closed.
        // NoteScroll applies that fraction on the next frame. Record which window that belongs to.
        _scrollWindow = ImGui.GetID("vanilla-scroll");
        if (!Scrolls.ContainsKey(_scrollWindow))
            Scrolls[_scrollWindow] = new ScrollState();
    }

    public static void NoteScroll()
    {
        var scroll = CurrentScroll();
        scroll.View = Math.Max(1f, ImGui.GetWindowHeight());
        scroll.Max = ImGui.GetScrollMaxY();
        if (scroll.DragFraction is float fraction && scroll.Max > 1f)
            ImGui.SetScrollY(fraction * scroll.Max);
        scroll.Y = ImGui.GetScrollY();
        if (!ImGui.GetIO().MouseDown[0])
            scroll.DragFraction = null;
    }

    public static void PaintScrollbar(VanillaRect track)
    {
        var scroll = CurrentScroll();
        // Hidden until the log actually overflows. The bright ScrollBarB piece is the thumb, not a full-height track.
        if (scroll.Max <= 1f)
            return;

        Catalog.EnsureLoaded();
        var thumbH = MathF.Round(ThumbHeight(track.H));
        var travel = Math.Max(1f, track.H - thumbH);
        var thumbY = MathF.Round(track.Y + travel * Math.Clamp(scroll.Y / Math.Max(1f, scroll.Max), 0f, 1f));
        var thumbMin = new Vector2(track.X, thumbY);
        var thumbMax = thumbMin + new Vector2(track.W, thumbH);
        var drawList = ImGui.GetWindowDrawList();
        if (!Drawer.Draw(drawList, Catalog.ScrollTrack ?? Catalog.ScrollThumb, thumbMin, thumbMax, 1f))
            drawList.AddRectFilled(thumbMin, thumbMax, ImGui.GetColorU32(new Vector4(0.95f, 0.86f, 0.55f, 0.9f)), track.W * 0.3f);

        ImGui.SetCursorScreenPos(track.Min);
        ImGui.InvisibleButton("##vanilla-scroll", track.Size);
        if (!ImGui.IsItemActive())
            return;

        var io = ImGui.GetIO();
        scroll.DragFraction = Math.Clamp((io.MousePos.Y - track.Y - thumbH * 0.5f) / travel, 0f, 1f);
    }

    private static float ThumbHeight(float trackH)
    {
        var scroll = CurrentScroll();
        var minThumb = Math.Min(trackH, 20f * ImGuiHelpers.GlobalScale);
        if (scroll.Max <= 1f)
            return minThumb;

        var ratio = scroll.View / (scroll.View + scroll.Max);
        return Math.Clamp(trackH * ratio, minThumb, trackH);
    }

    private sealed class EmptyDispose : IDisposable
    {
        public static readonly EmptyDispose Instance = new();
        public void Dispose()
        {
        }
    }
}
