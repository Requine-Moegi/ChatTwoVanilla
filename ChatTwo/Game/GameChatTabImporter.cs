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
/// <see cref="RaptureLogModule"/>. Which messages each tab shows come from the
/// LOGFLTR layout: one byte per LogFilter row, inside LogFilterConfig when that
/// image is present, otherwise from the character's LOGFLTR.DAT.
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

        if (!TryReadFilters(out var flags, out var how, out var layoutError))
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

    private static unsafe bool TryReadFilters(out bool[][] flags, out string how, out string error)
    {
        flags = [];
        how = "";
        error = "";

        var filter = LogFilterConfig.Instance();
        if (filter != null)
        {
            var memory = new byte[sizeof(LogFilterConfig)];
            new ReadOnlySpan<byte>(filter, memory.Length).CopyTo(memory);
            if (LogFilterDat.TryFind(memory, out flags))
            {
                how = "LogFilterConfig, one byte per LogFilter row";
                return true;
            }

            var written = filter->TempDataBytesWritten;
            if (filter->TempDataPtr != 0 && written >= LogFilterDat.MinimumSize && written <= 1024 * 1024)
            {
                var temp = new byte[written];
                new ReadOnlySpan<byte>((byte*)filter->TempDataPtr, temp.Length).CopyTo(temp);
                if (LogFilterDat.TryFind(temp, out flags))
                {
                    how = "LogFilterConfig file buffer, one byte per LogFilter row";
                    return true;
                }
            }
        }

        var contentId = filter != null ? filter->CharacterContentId : 0;
        if (contentId == 0)
            contentId = Plugin.PlayerState.ContentId;

        if (!TryReadLogFilterFile(contentId, out flags, out error))
            return false;

        how = "LOGFLTR.DAT, one byte per LogFilter row";
        error = "";
        return true;
    }

    private static bool TryReadLogFilterFile(ulong contentId, out bool[][] flags, out string error)
    {
        flags = [];
        error = "";
        if (contentId == 0)
        {
            error = "The log filters could not be read. Log into the game and try again. Nothing was changed.";
            return false;
        }

        var path = FindLogFilterPath(contentId);
        if (path == null)
        {
            error = "The log filters could not be read from the game, and this character's LOGFLTR.DAT was not found. Nothing was changed.";
            return false;
        }

        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            if (stream.Length < LogFilterDat.MinimumSize || stream.Length > 1024 * 1024)
            {
                error = "LOGFLTR.DAT is not the expected log-filter file. Nothing was changed.";
                return false;
            }

            var data = new byte[stream.Length];
            stream.ReadExactly(data);
            if (!LogFilterDat.TryParse(data, out flags))
            {
                error = "LOGFLTR.DAT does not match the 4-tab filter layout. Nothing was changed.";
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "Could not read {Path}", path);
            error = "LOGFLTR.DAT could not be read. Nothing was changed.";
            return false;
        }
    }

    private static string? FindLogFilterPath(ulong contentId)
    {
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "My Games",
            "FINAL FANTASY XIV - A Realm Reborn");
        if (!Directory.Exists(root))
            return null;

        var exact = Path.Combine(root, "FFXIV_CHR" + contentId.ToString("X16"), "LOGFLTR.DAT");
        if (File.Exists(exact))
            return exact;

        var bare = contentId.ToString("X");
        foreach (var dir in Directory.EnumerateDirectories(root, "FFXIV_CHR*"))
        {
            var folder = Path.GetFileName(dir);
            var id = folder.Length > "FFXIV_CHR".Length ? folder["FFXIV_CHR".Length..] : "";
            if (!id.Equals(bare, StringComparison.OrdinalIgnoreCase) && !id.Equals(contentId.ToString("X16"), StringComparison.OrdinalIgnoreCase))
                continue;

            var candidate = Path.Combine(dir, "LOGFLTR.DAT");
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }
}
