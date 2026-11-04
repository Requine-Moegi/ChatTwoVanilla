using System.Numerics;

namespace ChatTwo.Ui.Style;

public readonly record struct VanillaRect(float X, float Y, float W, float H)
{
    public Vector2 Min => new(X, Y);
    public Vector2 Max => new(X + W, Y + H);
    public Vector2 Size => new(W, H);
}

/// <summary>
/// Screen-space copy of ChatLog.uld's 324x200 widget. Bottom chrome keeps its design
/// distance from the window bottom. The log nine-grid grows to fill what is above it.
/// Round buttons stay a 28px step of 24px off the right edge, which is where nodes
/// 1001, 1004 and 1003 sit in the file.
/// </summary>
public readonly struct VanillaFrame
{
    public VanillaRect Background { get; init; }
    public VanillaRect Messages { get; init; }
    public VanillaRect Scroll { get; init; }
    public VanillaRect Label { get; init; }
    public VanillaRect Input { get; init; }
    public VanillaRect Bubble { get; init; }
    public VanillaRect TabStrip { get; init; }
    public VanillaRect Plus { get; init; }
    public VanillaRect Gear { get; init; }
    public VanillaRect Close { get; init; }
    public VanillaRect Grip { get; init; }
    public float Scale { get; init; }

    private static float Snap(float value) => MathF.Round(value);

    private static VanillaRect R(float x, float y, float w, float h)
        => new(Snap(x), Snap(y), MathF.Max(1f, Snap(w)), MathF.Max(1f, Snap(h)));

    public static VanillaFrame From(Vector2 origin, Vector2 size, float scale)
    {
        float S(float design) => design * scale;
        var width = size.X;
        var height = size.Y;
        var button = S(28f);
        var step = S(24f);

        var backgroundHeight = Math.Max(S(48f), height - S(52f));
        var background = R(origin.X, origin.Y, width, backgroundHeight);

        var label = R(
            origin.X + S(26f),
            origin.Y + height - S(67f),
            Math.Max(S(40f), width - S(30f)),
            S(21f));
        var input = R(
            origin.X + S(23f),
            origin.Y + height - S(53f),
            Math.Max(S(40f), width - S(27f)),
            button);
        var bubble = R(origin.X, input.Y, button, button);

        var rowY = origin.Y + height - button;
        var closeX = origin.X + width - button;
        var gearX = closeX - step;
        var plusX = gearX - step;
        var tabX = origin.X + S(23f);
        var tabRight = plusX - S(21f);
        var tabStrip = R(tabX, rowY, Math.Max(S(36f), tabRight - tabX), button);

        var grip = R(background.X + background.W - S(36f), background.Y + S(10f), button, button);
        var messageX = background.X + S(24f);
        var messageY = background.Y + S(16f);
        var messageBottom = label.Y - S(2f);
        // Stop before the grip so a wrapped line cannot run under the arrow.
        var messageRight = grip.X - S(4f);
        var messages = R(
            messageX,
            messageY,
            Math.Max(S(40f), messageRight - messageX),
            Math.Max(S(20f), messageBottom - messageY));
        var scroll = R(
            background.X + S(12f),
            background.Y + S(12f),
            S(12f),
            Math.Max(S(24f), background.H - S(24f)));

        return new VanillaFrame
        {
            Background = background,
            Messages = messages,
            Scroll = scroll,
            Label = label,
            Input = input,
            Bubble = bubble,
            TabStrip = tabStrip,
            Plus = R(plusX, rowY, button, button),
            Gear = R(gearX, rowY, button, button),
            Close = R(closeX, rowY, button, button),
            Grip = grip,
            Scale = scale,
        };
    }
}
