using System.Numerics;

namespace ChatTwo.Ui.Style;

/// <summary>
/// Recolors the dark interior of the input part. The light rim and the transparent margin stay
/// as they are in the texture, so the frame keeps that texture's shape.
/// </summary>
public static class InputTextureTint
{
    /// <summary>
    /// Max channel of an interior pixel. Part 17's fill is black, with a fringe at 8 and 16 and
    /// a few inner-corner texels at 32. The grey bevel starts at 41, so 36 keeps that bevel.
    /// </summary>
    public const int InteriorMaxChannel = 36;

    /// <summary>Below this alpha the texel is the outer margin, not the box.</summary>
    public const int InteriorMinAlpha = 200;

    public static bool IsInterior(byte r, byte g, byte b, byte a)
    {
        if (a < InteriorMinAlpha)
            return false;

        var luma = r > g ? r : g;
        if (b > luma)
            luma = b;
        return luma <= InteriorMaxChannel;
    }

    /// <summary>
    /// Writes <paramref name="color"/> over every interior pixel. Alpha is left alone, and every
    /// other pixel is left alone. <paramref name="rgba"/> is tightly packed R, G, B, A.
    /// </summary>
    public static void Apply(Span<byte> rgba, Vector3 color)
    {
        var tr = ToByte(color.X);
        var tg = ToByte(color.Y);
        var tb = ToByte(color.Z);
        for (var i = 0; i + 3 < rgba.Length; i += 4)
        {
            if (!IsInterior(rgba[i], rgba[i + 1], rgba[i + 2], rgba[i + 3]))
                continue;

            rgba[i] = tr;
            rgba[i + 1] = tg;
            rgba[i + 2] = tb;
        }
    }

    /// <summary>
    /// Destination edges of a nine-grid. Index 0 is the outer min, 1 and 2 bracket the center
    /// slice, 3 is the outer max. Margins are design pixels times <paramref name="uiScale"/>,
    /// then rounded. A high-resolution sheet does not change these edges.
    /// </summary>
    public static bool Slice(
        int left,
        int right,
        int top,
        int bottom,
        float minX,
        float minY,
        float maxX,
        float maxY,
        float uiScale,
        out float[] dstX,
        out float[] dstY)
    {
        dstX = [];
        dstY = [];
        var destW = maxX - minX;
        var destH = maxY - minY;
        if (destW < 1f || destH < 1f)
            return false;

        var leftPx = left * uiScale;
        var rightPx = right * uiScale;
        var topPx = top * uiScale;
        var bottomPx = bottom * uiScale;

        if (leftPx + rightPx > destW && leftPx + rightPx > 0f)
        {
            var fit = destW / (leftPx + rightPx);
            leftPx *= fit;
            rightPx *= fit;
        }

        if (topPx + bottomPx > destH && topPx + bottomPx > 0f)
        {
            var fit = destH / (topPx + bottomPx);
            topPx *= fit;
            bottomPx *= fit;
        }

        float Px(float value) => MathF.Round(value);
        dstX = [Px(minX), Px(minX + leftPx), Px(maxX - rightPx), Px(maxX)];
        dstY = [Px(minY), Px(minY + topPx), Px(maxY - bottomPx), Px(maxY)];
        if (dstX[1] > dstX[2])
            dstX[1] = dstX[2] = (dstX[1] + dstX[2]) * 0.5f;
        if (dstY[1] > dstY[2])
            dstY[1] = dstY[2] = (dstY[1] + dstY[2]) * 0.5f;
        return true;
    }

    private static byte ToByte(float channel)
        => (byte)Math.Clamp((int)MathF.Round(channel * 255f), 0, 255);
}
