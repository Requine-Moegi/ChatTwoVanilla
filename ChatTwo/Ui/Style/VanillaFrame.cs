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

    public static VanillaFrame From(Vector2 origin, Vector2 size, float scale)
    {
        float S(float design) => design * scale;
        var width = size.X;
        var height = size.Y;
        var button = S(28f);
        var step = S(24f);

        var backgroundHeight = Math.Max(S(48f), height - S(52f));
        var background = new VanillaRect(origin.X, origin.Y, width, backgroundHeight);

        var label = new VanillaRect(
            origin.X + S(26f),
            origin.Y + height - S(67f),
            Math.Max(S(40f), width - S(30f)),
            S(21f));
        var input = new VanillaRect(
            origin.X + S(23f),
            origin.Y + height - S(53f),
            Math.Max(S(40f), width - S(27f)),
            button);
        var bubble = new VanillaRect(origin.X, input.Y, button, button);

        var rowY = origin.Y + height - button;
        var closeX = origin.X + width - button;
        var gearX = closeX - step;
        var plusX = gearX - step;
        var tabX = origin.X + S(23f);
        var tabRight = plusX - S(21f);
        var tabStrip = new VanillaRect(tabX, rowY, Math.Max(S(36f), tabRight - tabX), button);

        var messageX = background.X + S(24f);
        var messageY = background.Y + S(16f);
        var messageBottom = label.Y - S(2f);
        var messages = new VanillaRect(
            messageX,
            messageY,
            Math.Max(S(40f), background.X + background.W - S(12f) - messageX),
            Math.Max(S(20f), messageBottom - messageY));
        var scroll = new VanillaRect(
            background.X + S(12f),
            background.Y + S(12f),
            S(12f),
            Math.Max(S(24f), background.H - S(24f)));
        var grip = new VanillaRect(background.X + background.W - S(36f), background.Y + S(10f), button, button);

        return new VanillaFrame
        {
            Background = background,
            Messages = messages,
            Scroll = scroll,
            Label = label,
            Input = input,
            Bubble = bubble,
            TabStrip = tabStrip,
            Plus = new VanillaRect(plusX, rowY, button, button),
            Gear = new VanillaRect(gearX, rowY, button, button),
            Close = new VanillaRect(closeX, rowY, button, button),
            Grip = grip,
            Scale = scale,
        };
    }
}
