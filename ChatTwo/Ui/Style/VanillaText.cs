using System.Numerics;
using System.Text;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;

namespace ChatTwo.Ui.Style;

/// <summary>
/// Vanilla log text is drawn with an edge (a dark outline) rather than ImGui's flat glyphs.
/// The outline is drawn first so the coloured glyph stays the ImGui item used for links and clicks.
/// </summary>
public static class VanillaText
{
    private static readonly Vector2[] Offsets =
    [
        new(-1, 0), new(1, 0), new(0, -1), new(0, 1),
        new(-1, -1), new(1, -1), new(-1, 1), new(1, 1),
    ];

    public static void DrawEdge(Vector2 pos, string text)
    {
        if (!Plugin.Config.VanillaStyle || text.Length == 0)
            return;

        var font = ImGui.GetFont();
        var size = ImGui.GetFontSize();
        var drawList = ImGui.GetWindowDrawList();
        var distance = MathF.Max(1f, ImGuiHelpers.GlobalScale * 0.75f);
        var color = ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.95f));
        foreach (var offset in Offsets)
            drawList.AddText(font, size, pos + offset * distance, color, text);
    }

    public static unsafe void DrawEdge(Vector2 pos, byte* text, byte* textEnd)
    {
        if (!Plugin.Config.VanillaStyle || text == null || textEnd <= text)
            return;

        var length = (int)(textEnd - text);
        DrawEdge(pos, Encoding.UTF8.GetString(text, length));
    }

    public static void DrawAt(Vector2 pos, string text, Vector4 color)
    {
        DrawEdge(pos, text);
        var font = ImGui.GetFont();
        var size = ImGui.GetFontSize();
        ImGui.GetWindowDrawList().AddText(font, size, pos, ImGui.GetColorU32(color), text);
    }
}
