using System.Numerics;

namespace ChatTwo.Ui.Style;

/// <summary>
/// One rectangle from a game UI texture, plus the nine-grid margins from the ULD node that uses it.
/// Coordinates are in the standard-resolution texture space stored by the ULD. High-resolution
/// sheets (<c>_hr1</c>) use the same rectangle doubled at draw time.
/// </summary>
public sealed class VanillaPart
{
    public string BasePath { get; init; } = "";
    public byte ThemeMask { get; init; }
    public bool AssetsHaveThemeSupport { get; init; }
    public int PartListId { get; init; }
    public int PartId { get; init; }
    public ushort U { get; init; }
    public ushort V { get; init; }
    public ushort W { get; init; }
    public ushort H { get; init; }
    public int NodeW { get; init; }
    public int NodeH { get; init; }
    public short Left { get; init; }
    public short Top { get; init; }
    public short Right { get; init; }
    public short Bottom { get; init; }
    public bool IsNineGrid { get; init; }
    public bool FlipH { get; init; }
    public float NodeAlpha { get; init; } = 1f;
    public Vector3 Multiply { get; init; } = Vector3.One;
    public string Source { get; init; } = "";

    public string Describe()
        => $"{Source} {BasePath} partList={PartListId} part={PartId} uv=({U},{V},{W},{H}) node={NodeW}x{NodeH} slice=({Left},{Top},{Right},{Bottom}) themeMask={ThemeMask} themed={AssetsHaveThemeSupport}";
}
