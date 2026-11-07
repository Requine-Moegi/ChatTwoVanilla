using ChatTwo.Code;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;

namespace ChatTwo.Game;

/// <summary>
/// One game chat tab, already translated into Chat 2 channels. Built only after the live
/// log module and the log-filter memory both read cleanly.
/// </summary>
internal sealed class ImportedGameTab
{
    public required string Name { get; init; }
    public required bool DisplayTimestamp { get; init; }
    public required Dictionary<ChatType, (ChatSource Source, ChatSource Target)> Channels { get; init; }
}

internal sealed class GameTabImport
{
    public required IReadOnlyList<ImportedGameTab> Tabs { get; init; }
    public required IReadOnlyList<string> Notes { get; init; }
    public required string How { get; init; }
}

/// <summary>
/// Reads the four vanilla chat tabs from the running client. Names come from
/// <see cref="RaptureLogModule"/>; which messages each tab shows come from the
/// loaded <see cref="LogFilterConfig"/> (the LogFilter checkbox list), not from a file.
/// </summary>
internal static class GameChatTabImporter
{
    public static bool TryRead(out GameTabImport? import, out string? error)
    {
        import = null;
        error = null;
        try
        {
            return TryReadUnsafe(out import, out error);
        }
        catch (Exception ex)
        {
            error = "The game chat tabs could not be read: " + ex.Message;
            Plugin.Log.Warning(ex, "Import tabs from game chat failed");
            return false;
        }
    }

    private static unsafe bool TryReadUnsafe(out GameTabImport? import, out string? error)
    {
        import = null;
        error = null;

        var log = RaptureLogModule.Instance();
        if (log == null)
        {
            error = "RaptureLogModule is not available. Log into the game and open this again; the chat log has not been created yet.";
            Plugin.Log.Warning("Import tabs from game chat: {Reason}", error);
            return false;
        }

        var tabs = log->ChatTabs;
        if (tabs.Length < 4)
        {
            error = $"RaptureLogModule has {tabs.Length} chat tabs, not the 4 the game log uses. Nothing was changed.";
            Plugin.Log.Warning("Import tabs from game chat: {Reason}", error);
            return false;
        }

        var names = new string[4];
        var timestamps = new bool[4];
        var timeFlags = log->ChatTabShouldDisplayTime;
        var named = 0;
        for (var i = 0; i < 4; i++)
        {
            names[i] = CleanName(tabs[i].Name.ToString(), i);
            if (!names[i].StartsWith("Tab ", StringComparison.Ordinal))
                named++;
            timestamps[i] = timeFlags.Length > i ? timeFlags[i] : true;
        }

        if (named == 0)
        {
            error = "The four live chat tabs have no names. The log module is allocated, but its tab names are empty, so this is not a reliable read. Nothing was changed.";
            Plugin.Log.Warning("Import tabs from game chat: {Reason}", error);
            return false;
        }

        var filter = LogFilterConfig.Instance();
        if (filter == null)
        {
            error = "LogFilterConfig is not available. The character log filters are not loaded yet. Nothing was changed.";
            Plugin.Log.Warning("Import tabs from game chat: {Reason}", error);
            return false;
        }

        var header = sizeof(UserFileManager.UserFileEvent);
        var payloadSize = sizeof(LogFilterConfig) - header;
        if (payloadSize < 4 * (LogFilterRows.MaxRowId + 1))
        {
            error = $"LogFilterConfig is {sizeof(LogFilterConfig)} bytes. After the {header}-byte user-file header, {payloadSize} bytes are left, which is shorter than 4 tabs of LogFilter checkboxes. The struct no longer matches this client. Nothing was changed.";
            Plugin.Log.Warning("Import tabs from game chat: {Reason}", error);
            return false;
        }

        var payload = new byte[payloadSize];
        new ReadOnlySpan<byte>((byte*)filter + header, payloadSize).CopyTo(payload);

        if (!TryFindFlags(payload, out var flags, out var how, out var layoutError))
        {
            error = layoutError;
            Plugin.Log.Warning("Import tabs from game chat: {Reason}", error);
            return false;
        }

        var notes = new List<string>();
        var imported = new List<ImportedGameTab>(4);
        for (var i = 0; i < 4; i++)
        {
            var channels = ChannelsFor(flags[i], notes);
            imported.Add(new ImportedGameTab
            {
                Name = names[i],
                DisplayTimestamp = timestamps[i],
                Channels = channels,
            });
        }

        foreach (var note in notes.Distinct())
            Plugin.Log.Information("Import tabs from game chat: {Note}", note);

        Plugin.Log.Information(
            "Import tabs from game chat read {Tabs} via {How}",
            string.Join(", ", imported.Select(tab => $"{tab.Name} ({tab.Channels.Count})") ),
            how);

        import = new GameTabImport { Tabs = imported, Notes = notes.Distinct().ToArray(), How = how };
        return true;
    }

    public static Tab ToPluginTab(ImportedGameTab source)
    {
        return new Tab
        {
            Name = source.Name,
            DisplayTimestamp = source.DisplayTimestamp,
            SelectedChannels = source.Channels.ToDictionary(pair => pair.Key, pair => pair.Value),
        };
    }

    private static string CleanName(string raw, int index)
    {
        var trimmed = raw.Replace("\0", "").Trim();
        if (trimmed.Length > 64)
            trimmed = trimmed[..64];
        return trimmed.Length == 0 ? $"Tab {index + 1}" : trimmed;
    }

    private static Dictionary<ChatType, (ChatSource, ChatSource)> ChannelsFor(bool[] enabled, List<string> notes)
    {
        var channels = new Dictionary<ChatType, (ChatSource Source, ChatSource Target)>();
        foreach (var row in LogFilterRows.All)
        {
            if (row.Row >= enabled.Length || !enabled[row.Row])
                continue;

            if (!Enum.IsDefined(typeof(ChatType), row.LogKind))
            {
                notes.Add($"Unmapped log kind {row.LogKind} on filter row {row.Row}.");
                continue;
            }

            Add(channels, (ChatType)row.LogKind, MapMask(row.Caster), MapMask(row.Target));
        }

        // The game's Tell checkbox is one row (log kind 12) and shows both directions.
        if (channels.TryGetValue(ChatType.TellOutgoing, out var tell) && !channels.ContainsKey(ChatType.TellIncoming))
        {
            channels[ChatType.TellIncoming] = tell;
            notes.Add("Tell is one game checkbox. It was imported as incoming and outgoing tells.");
        }

        // Cross-world party uses a separate log kind. The Party checkbox is the only party row.
        if (channels.TryGetValue(ChatType.Party, out var party) && !channels.ContainsKey(ChatType.CrossParty))
        {
            channels[ChatType.CrossParty] = party;
            notes.Add("Party is one game checkbox. Cross-world party was included with it.");
        }

        return channels;
    }

    private static void Add(Dictionary<ChatType, (ChatSource Source, ChatSource Target)> channels, ChatType type, ChatSource source, ChatSource target)
    {
        if (channels.TryGetValue(type, out var existing))
            channels[type] = (existing.Source | source, existing.Target | target);
        else
            channels[type] = (source, target);
    }

    /// <summary>
    /// LogFilter caster/target masks use the same bit positions as the client's relation kinds.
    /// 65535 is every relation. Bit 0 is an extra "you" flag beside the local player. Bits 12–15
    /// are unused high bits of a 16-bit mask (set on "everyone except you").
    /// </summary>
    private static ChatSource MapMask(ushort mask)
    {
        if (mask == 0xFFFF)
            return ChatSourceExt.All;

        var source = (ChatSource)0;
        if ((mask & 1) != 0)
            source |= ChatSource.LocalPlayer;
        for (var bit = 1; bit <= 11; bit++)
        {
            if ((mask & (1 << bit)) != 0)
                source |= (ChatSource)(1 << bit);
        }

        return source;
    }

    private static bool TryFindFlags(byte[] payload, out bool[][] flags, out string how, out string error)
    {
        flags = [];
        how = "";
        error = "";

        bool[][]? best = null;
        var bestScore = int.MinValue;
        var ambiguous = false;
        string bestHow = "";

        void Consider(bool[][] candidate, string description)
        {
            var score = Score(candidate);
            if (score < 8)
                return;
            if (score > bestScore)
            {
                bestScore = score;
                best = candidate;
                bestHow = description;
                ambiguous = false;
            }
            else if (score == bestScore && best != null && !Same(best, candidate))
            {
                ambiguous = true;
            }
        }

        var rows = LogFilterRows.MaxRowId + 1;
        var span = payload.AsSpan();
        if (payload.Length >= 4 * rows)
        {
            for (var offset = 0; offset + 4 * rows <= payload.Length; offset += 4)
            {
                if (!IsCheckboxBytes(span.Slice(offset, 4 * rows)))
                    continue;
                Consider(ReadByteFlags(span.Slice(offset), rows), $"one byte per LogFilter row ({rows} rows) at LogFilterConfig+0x{0x48 + offset:X}");
            }
        }

        foreach (var width in new[] { 40, 48, 64 })
        {
            for (var gap = 0; gap <= 0x80; gap += 4)
            {
                var stride = width + gap;
                for (var offset = 0; offset + 3 * stride + width <= payload.Length; offset += 4)
                {
                    var candidate = ReadBitFlags(span, offset, stride, width, rows);
                    if (BlankHits(candidate) != 0)
                        continue;
                    Consider(candidate, $"bitset of {width} bytes per tab, stride 0x{stride:X}, at LogFilterConfig+0x{0x48 + offset:X}");
                }
            }
        }

        if (best == null)
        {
            error = "The log filters could not be read reliably. Tab names are on RaptureLogModule, and the checkboxes live in LogFilterConfig after its 0x48-byte user-file header, but that payload is not a documented field. It was not a 0-or-1 checkbox for each LogFilter row, and it was not a bitset of those rows. Nothing was changed.";
            return false;
        }

        if (ambiguous)
        {
            error = "The log filters could not be read reliably. More than one layout in LogFilterConfig matched the LogFilter checkbox list, so the channels would be a guess. Nothing was changed.";
            return false;
        }

        flags = best;
        how = bestHow;
        return true;
    }

    private static int Score(bool[][] flags)
    {
        var real = 0;
        foreach (var tab in flags)
        {
            foreach (var row in LogFilterRows.All)
            {
                if (row.Row < tab.Length && tab[row.Row])
                    real++;
            }
        }

        return BlankHits(flags) == 0 ? real : -1;
    }

    private static int BlankHits(bool[][] flags)
    {
        var real = new bool[LogFilterRows.MaxRowId + 1];
        foreach (var row in LogFilterRows.All)
        {
            if (row.Row < real.Length)
                real[row.Row] = true;
        }

        var blank = 0;
        foreach (var tab in flags)
        {
            for (var i = 0; i < tab.Length && i < real.Length; i++)
            {
                if (tab[i] && !real[i])
                    blank++;
            }
        }

        return blank;
    }

    private static bool Same(bool[][] left, bool[][] right)
    {
        if (left.Length != right.Length)
            return false;
        for (var i = 0; i < left.Length; i++)
        {
            if (left[i].Length != right[i].Length)
                return false;
            for (var j = 0; j < left[i].Length; j++)
            {
                if (left[i][j] != right[i][j])
                    return false;
            }
        }

        return true;
    }

    private static bool IsCheckboxBytes(ReadOnlySpan<byte> bytes)
    {
        foreach (var value in bytes)
        {
            if (value > 1)
                return false;
        }

        return true;
    }

    private static bool[][] ReadByteFlags(ReadOnlySpan<byte> payload, int rows)
    {
        var flags = new bool[4][];
        for (var tab = 0; tab < 4; tab++)
        {
            flags[tab] = new bool[rows];
            var slice = payload.Slice(tab * rows, rows);
            for (var row = 0; row < rows; row++)
                flags[tab][row] = slice[row] != 0;
        }

        return flags;
    }

    private static bool[][] ReadBitFlags(ReadOnlySpan<byte> payload, int offset, int stride, int width, int rows)
    {
        var flags = new bool[4][];
        for (var tab = 0; tab < 4; tab++)
        {
            flags[tab] = new bool[rows];
            var bits = payload.Slice(offset + tab * stride, width);
            for (var row = 0; row < rows; row++)
            {
                var index = row >> 3;
                if (index >= bits.Length)
                    break;
                flags[tab][row] = (bits[index] & (1 << (row & 7))) != 0;
            }
        }

        return flags;
    }
}
