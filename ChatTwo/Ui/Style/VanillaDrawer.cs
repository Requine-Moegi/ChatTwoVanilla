using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Interface.Utility;
using Dalamud.Utility;
using Lumina.Data.Files;

namespace ChatTwo.Ui.Style;

/// <summary>
/// Draws ULD parts through <see cref="Dalamud.Plugin.Services.ITextureProvider"/>. Textures are the
/// themed game files (<c>ui/uld/imgXX/...</c> and <c>_hr1</c> when that sheet exists).
/// </summary>
public sealed class VanillaDrawer
{
    private readonly Dictionary<string, string?> _paths = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<int, IDalamudTextureWrap> _inputTints = new();
    private string? _inputTintKey;
    private byte[]? _inputTintSource;
    private int _inputTintWidth;
    private int _inputTintHeight;
    private bool _inputTintHighResolution;

    public void InvalidatePaths()
    {
        _paths.Clear();
        ClearInputTints();
    }

    public bool Draw(ImDrawListPtr drawList, VanillaPart? part, Vector2 min, Vector2 max, float alpha, Vector4? extraTint = null, bool? flipH = null, bool skipCenter = false)
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
        DrawPart(drawList, wrap, highResolution, part, min, max, color, flipH ?? part.FlipH, skipCenter);
        return true;
    }

    /// <summary>
    /// Destination edges of a nine-grid, in the same pixel space <see cref="Draw"/> uses.
    /// Index 0 is the outer min, 1 and 2 bracket the center slice, 3 is the outer max.
    /// Offsets are the ULD nine-grid margins in design pixels times <paramref name="uiScale"/>,
    /// then rounded. A high-resolution sheet does not change these edges. It only doubles UVs.
    /// </summary>
    public static bool TrySlice(VanillaPart? part, Vector2 min, Vector2 max, float uiScale, out float[] dstX, out float[] dstY)
    {
        dstX = [];
        dstY = [];
        if (part is not { IsNineGrid: true })
            return false;

        var destW = max.X - min.X;
        var destH = max.Y - min.Y;
        if (destW < 1f || destH < 1f)
            return false;

        var left = Math.Clamp((int)part.Left, 0, part.W / 2);
        var right = Math.Clamp((int)part.Right, 0, part.W / 2);
        var top = Math.Clamp((int)part.Top, 0, part.H / 2);
        var bottom = Math.Clamp((int)part.Bottom, 0, part.H / 2);
        return InputTextureTint.Slice(left, right, top, bottom, min.X, min.Y, max.X, max.Y, uiScale, out dstX, out dstY);
    }

    /// <summary>
    /// Draws the input part with its dark pixels replaced by <paramref name="tint"/>. The light rim
    /// is the original texture. The result is cached per color. A miss falls through so the caller
    /// can draw the untinted part.
    /// </summary>
    public bool DrawInteriorTinted(ImDrawListPtr drawList, VanillaPart? part, Vector2 min, Vector2 max, float alpha, Vector4 tint)
    {
        if (part == null || max.X - min.X < 1f || max.Y - min.Y < 1f)
            return false;

        if (!TryGetInteriorWrap(part, tint, out var wrap, out var highResolution) || wrap == null)
            return false;

        var multiply = part.Multiply;
        var color = ImGui.GetColorU32(new Vector4(multiply.X, multiply.Y, multiply.Z, part.NodeAlpha * Math.Clamp(alpha, 0f, 1f)));
        DrawPart(drawList, wrap, highResolution, part, min, max, color, part.FlipH, false, 0, 0);
        return true;
    }

    private bool TryGetInteriorWrap(VanillaPart part, Vector4 tint, out IDalamudTextureWrap? wrap, out bool highResolution)
    {
        wrap = null;
        highResolution = false;
        var path = ResolvePath(part);
        if (string.IsNullOrEmpty(path) || !EnsureInputTintSource(part, path))
            return false;

        highResolution = _inputTintHighResolution;
        var key = PackTint(tint);
        if (_inputTints.TryGetValue(key, out wrap) && wrap != null)
            return true;

        var pixels = (byte[])_inputTintSource!.Clone();
        InputTextureTint.Apply(pixels, new Vector3(tint.X, tint.Y, tint.Z));
        try
        {
            wrap = Plugin.TextureProvider.CreateFromRaw(
                RawImageSpecification.Rgba32(_inputTintWidth, _inputTintHeight),
                pixels,
                "ChatTwoVanilla input tint");
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "Could not upload the tinted input texture");
            wrap = null;
            return false;
        }

        while (_inputTints.Count >= 8)
        {
            var drop = _inputTints.Keys.First();
            _inputTints[drop].Dispose();
            _inputTints.Remove(drop);
        }

        _inputTints[key] = wrap;
        return true;
    }

    private bool EnsureInputTintSource(VanillaPart part, string path)
    {
        var key = $"{path}|{part.U}|{part.V}|{part.W}|{part.H}";
        if (_inputTintSource != null && _inputTintKey == key)
            return true;

        TexFile? file;
        try
        {
            file = Plugin.DataManager.GetFile<TexFile>(path);
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "Could not read the input texture");
            return false;
        }

        if (file == null)
            return false;

        byte[] rgba;
        try
        {
            rgba = file.GetRgbaImageData();
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "Could not decode the input texture");
            return false;
        }

        var width = file.Header.Width;
        var height = file.Header.Height;
        if (width <= 0 || height <= 0 || rgba.Length < width * height * 4)
            return false;

        var scale = 1;
        var highResolution = path.Contains("_hr1", StringComparison.OrdinalIgnoreCase);
        if (highResolution && (part.U + part.W) * 2 <= width && (part.V + part.H) * 2 <= height)
            scale = 2;

        var cropW = part.W * scale;
        var cropH = part.H * scale;
        var x0 = part.U * scale;
        var y0 = part.V * scale;
        if (x0 < 0 || y0 < 0 || x0 + cropW > width || y0 + cropH > height || cropW <= 0 || cropH <= 0)
            return false;

        var crop = new byte[cropW * cropH * 4];
        for (var y = 0; y < cropH; y++)
            Buffer.BlockCopy(rgba, ((y0 + y) * width + x0) * 4, crop, y * cropW * 4, cropW * 4);

        ClearInputTints();
        _inputTintSource = crop;
        _inputTintWidth = cropW;
        _inputTintHeight = cropH;
        _inputTintHighResolution = scale == 2;
        _inputTintKey = key;
        return true;
    }

    private void ClearInputTints()
    {
        foreach (var wrap in _inputTints.Values)
            wrap.Dispose();
        _inputTints.Clear();
        _inputTintSource = null;
        _inputTintKey = null;
    }

    private static int PackTint(Vector4 tint)
    {
        var r = (int)Math.Clamp((int)MathF.Round(tint.X * 255f), 0, 255);
        var g = (int)Math.Clamp((int)MathF.Round(tint.Y * 255f), 0, 255);
        var b = (int)Math.Clamp((int)MathF.Round(tint.Z * 255f), 0, 255);
        return (r << 16) | (g << 8) | b;
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

    private static void DrawPart(ImDrawListPtr drawList, IDalamudTextureWrap tex, bool highResolution, VanillaPart part, Vector2 min, Vector2 max, uint color, bool flipH, bool skipCenter, int uvU = -1, int uvV = -1)
    {
        var originU = uvU >= 0 ? uvU : part.U;
        var originV = uvV >= 0 ? uvV : part.V;
        var uvScale = 1f;
        if (highResolution)
        {
            var uvRight = (originU + part.W) * 2f;
            var uvBottom = (originV + part.H) * 2f;
            if (uvRight <= tex.Width + 0.5f && uvBottom <= tex.Height + 0.5f)
                uvScale = 2f;
        }

        Vector2 Uv(float x, float y) => new(x * uvScale / tex.Width, y * uvScale / tex.Height);

        if (!part.IsNineGrid || part.Left <= 0 && part.Right <= 0 && part.Top <= 0 && part.Bottom <= 0)
        {
            float u0 = originU;
            float u1 = originU + part.W;
            if (flipH)
                (u0, u1) = (u1, u0);
            drawList.AddImage(tex.Handle, min, max, Uv(u0, originV), Uv(u1, originV + part.H), color);
            return;
        }

        if (!TrySlice(part, min, max, ImGuiHelpers.GlobalScale, out var dstX, out var dstY))
            return;

        var left = Math.Clamp((int)part.Left, 0, part.W / 2);
        var right = Math.Clamp((int)part.Right, 0, part.W / 2);
        var top = Math.Clamp((int)part.Top, 0, part.H / 2);
        var bottom = Math.Clamp((int)part.Bottom, 0, part.H / 2);
        float[] srcX = [originU, originU + left, originU + part.W - right, originU + part.W];
        float[] srcY = [originV, originV + top, originV + part.H - bottom, originV + part.H];

        for (var y = 0; y < 3; y++)
        {
            for (var x = 0; x < 3; x++)
            {
                if (skipCenter && x == 1 && y == 1)
                    continue;

                var p0 = new Vector2(dstX[x], dstY[y]);
                var p1 = new Vector2(dstX[x + 1], dstY[y + 1]);
                if (p1.X - p0.X < 0.5f || p1.Y - p0.Y < 0.5f)
                    continue;

                drawList.AddImage(tex.Handle, p0, p1, Uv(srcX[x], srcY[y]), Uv(srcX[x + 1], srcY[y + 1]), color);
            }
        }
    }
}
