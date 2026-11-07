namespace ChatTwo.Game;

/// <summary>
/// On-disk LOGFLTR.DAT, measured from a 2080-byte character file.
/// A 16-byte header, then four 0x150-byte tabs. Each tab is a 16-byte record header
/// and 0x140 state bytes, one per LogFilter row id. 1 is checked. 0 is unchecked.
/// Other values (2 in this file) sit only on row ids that are not checkboxes.
/// </summary>
internal static class LogFilterDat
{
    public const int FileHeaderSize = 0x10;
    public const int TabCount = 4;
    public const int TabStride = 0x150;
    public const int TabHeaderSize = 0x10;
    public const int BodySize = 0x140;
    public const int ContentSize = TabCount * TabStride;
    public const int MinimumSize = FileHeaderSize + ContentSize;

    public static bool TryParse(ReadOnlySpan<byte> data, out bool[][] flags)
    {
        flags = [];
        if (data.Length >= MinimumSize && IsFileHeader(data) && TryParseTabs(data.Slice(FileHeaderSize, ContentSize), out flags))
            return true;

        if (data.Length >= ContentSize && TryParseTabs(data[..ContentSize], out flags))
            return true;

        flags = [];
        return false;
    }

    public static bool TryFind(ReadOnlySpan<byte> data, out bool[][] flags)
    {
        if (TryParse(data, out flags))
            return true;

        for (var offset = 2; offset + ContentSize <= data.Length; offset += 2)
        {
            if (TryParse(data[offset..], out flags))
                return true;
        }

        flags = [];
        return false;
    }

    private static bool IsFileHeader(ReadOnlySpan<byte> data)
    {
        // File type 4 is LOGFLTR. Version 3. The next u32 is the four tab blocks.
        return ReadU16(data, 0) == 4
            && ReadU16(data, 2) == 3
            && ReadU32(data, 8) == ContentSize;
    }

    private static bool TryParseTabs(ReadOnlySpan<byte> tabs, out bool[][] flags)
    {
        flags = [];
        if (tabs.Length < ContentSize)
            return false;

        var parsed = new bool[TabCount][];
        for (var tab = 0; tab < TabCount; tab++)
        {
            var at = tab * TabStride;
            if (ReadU16(tabs, at) != tab)
                return false;
            if (ReadU16(tabs, at + 2) != 3)
                return false;
            if (ReadU32(tabs, at + 4) != BodySize)
                return false;

            var body = tabs.Slice(at + TabHeaderSize, BodySize);
            var enabled = new bool[LogFilterRows.MaxRowId + 1];
            foreach (var row in LogFilterRows.All)
            {
                if (row.Row >= body.Length)
                    return false;

                // A real checkbox is only off or on. Anything else is a different layout.
                if (body[row.Row] > 1)
                    return false;

                enabled[row.Row] = body[row.Row] == 1;
            }

            parsed[tab] = enabled;
        }

        flags = parsed;
        return true;
    }

    private static ushort ReadU16(ReadOnlySpan<byte> data, int offset)
        => (ushort)(data[offset] | (data[offset + 1] << 8));

    private static uint ReadU32(ReadOnlySpan<byte> data, int offset)
        => (uint)(data[offset] | (data[offset + 1] << 8) | (data[offset + 2] << 16) | (data[offset + 3] << 24));
}
