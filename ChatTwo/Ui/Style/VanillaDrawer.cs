using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Interface.Utility;

namespace ChatTwo.Ui.Style;

/// <summary>
/// Draws ULD parts through <see cref="Dalamud.Plugin.Services.ITextureProvider"/>. Textures are the
/// themed game files (<c>ui/uld/imgXX/...</c> and <c>_hr1</c> when that sheet exists).
/// </summary>
public sealed class VanillaDrawer
{
    private readonly Dictionary<string, string?> _paths = new(StringComparer.OrdinalIgnoreCase);

    public void InvalidatePaths() => _paths.Clear();

    public bool Draw(ImDrawListPtr drawList, VanillaPart? part, Vector2 min, Vector2 max, float alpha, Vector4? extraTint = null, bool? flipH = null)
    {
        if (part == null || max.X - min.X < 1f || max.Y - min.Y < 1f)
            return false;

        if (!TryGetWrap(part, out var wrap, out var highResolution) || wrap == null)
            return false;

        var multiply = part.Multiply;
        var tint = new Vector4(multiply.X, multiply.Y, multiply.Z, part.NodeAlpha * Math.Clamp(alpha, 0f, 1f));
        if (extraTint != null)
            tint *= extraTint.Value;

        var color = ImGui.GetColorU32(tint);
        DrawPart(drawList, wrap, highResolution, part, min, max, color, flipH ?? part.FlipH);
        return true;
    }

    public string? ResolvePath(VanillaPart part)
    {
        var theme = VanillaGameLook.TextureThemeIndex();
        var preferHd = VanillaGameLook.PreferHighResolutionTextures();
        var key = $"{theme}|{preferHd}|{part.AssetsHaveThemeSupport}|{part.ThemeMask}|{part.BasePath}";
        if (_paths.TryGetValue(key, out var cached))
            return cached;

        string? found = null;
        foreach (var candidate in VanillaGameLook.CandidatePaths(part, theme, preferHd))
        {
            if (!Plugin.DataManager.FileExists(candidate))
                continue;
            found = candidate;
            break;
        }

        _paths[key] = found;
        return found;
    }

    private bool TryGetWrap(VanillaPart part, out IDalamudTextureWrap? wrap, out bool highResolution)
    {
        wrap = null;
        highResolution = false;
        var path = ResolvePath(part);
        if (string.IsNullOrEmpty(path))
            return false;

        wrap = Plugin.TextureProvider.GetFromGame(path).GetWrapOrDefault();
        if (wrap == null)
            return false;

        highResolution = path.Contains("_hr1", StringComparison.OrdinalIgnoreCase);
        return true;
    }

    private static void DrawPart(ImDrawListPtr drawList, IDalamudTextureWrap tex, bool highResolution, VanillaPart part, Vector2 min, Vector2 max, uint color, bool flipH)
    {
        var uvScale = 1f;
        if (highResolution)
        {
            var uvRight = (part.U + part.W) * 2f;
            var uvBottom = (part.V + part.H) * 2f;
            if (uvRight <= tex.Width + 0.5f && uvBottom <= tex.Height + 0.5f)
                uvScale = 2f;
        }

        Vector2 Uv(float x, float y) => new(x * uvScale / tex.Width, y * uvScale / tex.Height);

        var destW = max.X - min.X;
        var destH = max.Y - min.Y;
        if (!part.IsNineGrid || part.Left <= 0 && part.Right <= 0 && part.Top <= 0 && part.Bottom <= 0)
        {
            float u0 = part.U;
            float u1 = part.U + part.W;
            if (flipH)
                (u0, u1) = (u1, u0);
            drawList.AddImage(tex.Handle, min, max, Uv(u0, part.V), Uv(u1, part.V + part.H), color);
            return;
        }

        var left = Math.Clamp((int)part.Left, 0, part.W / 2);
        var right = Math.Clamp((int)part.Right, 0, part.W / 2);
        var top = Math.Clamp((int)part.Top, 0, part.H / 2);
        var bottom = Math.Clamp((int)part.Bottom, 0, part.H / 2);
        var ui = ImGuiHelpers.GlobalScale;
        var leftPx = left * ui;
        var rightPx = right * ui;
        var topPx = top * ui;
        var bottomPx = bottom * ui;

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

        float[] srcX = [part.U, part.U + left, part.U + part.W - right, part.U + part.W];
        float[] srcY = [part.V, part.V + top, part.V + part.H - bottom, part.V + part.H];
        float[] dstX = [min.X, min.X + leftPx, max.X - rightPx, max.X];
        float[] dstY = [min.Y, min.Y + topPx, max.Y - bottomPx, max.Y];

        for (var y = 0; y < 3; y++)
        {
            for (var x = 0; x < 3; x++)
            {
                var p0 = new Vector2(dstX[x], dstY[y]);
                var p1 = new Vector2(dstX[x + 1], dstY[y + 1]);
                if (p1.X - p0.X < 0.5f || p1.Y - p0.Y < 0.5f)
                    continue;

                drawList.AddImage(tex.Handle, p0, p1, Uv(srcX[x], srcY[y]), Uv(srcX[x + 1], srcY[y + 1]), color);
            }
        }
    }
}
