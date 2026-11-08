using System.Numerics;
using ChatTwo.Code;
using ChatTwo.Ui.Style;
using ChatTwo.Util;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;

namespace ChatTwo.Ui.Handler;

public class ChunkHandler
{
    private readonly Plugin Plugin;

    public ChunkHandler(Plugin plugin)
    {
        Plugin = plugin;
    }

    public void DrawChunks(IReadOnlyList<Chunk> chunks, bool wrap = true, PayloadHandler? handler = null, float lineWidth = 0f)
    {
        using var style = ImRaii.PushStyle(ImGuiStyleVar.ItemSpacing, Vector2.Zero);

        for (var i = 0; i < chunks.Count; i++)
        {
            if (chunks[i] is TextChunk text && string.IsNullOrEmpty(text.Content))
                continue;

            DrawChunk(chunks[i], wrap, handler, lineWidth);

            if (i < chunks.Count - 1)
            {
                ImGui.SameLine();
            }
            else if (chunks[i].Link is EmotePayload && Plugin.Config.ShowEmotes)
            {
                // Emote payloads seem to not automatically put newlines, which
                // is an issue when modern mode is disabled.
                ImGui.SameLine();
                // Use default ImGui behavior for newlines.
                ImGui.TextUnformatted("");
            }
        }
    }

    public void DrawIcon(Chunk chunk, IconChunk icon, PayloadHandler? handler)
    {
        if (!IconUtil.GfdFileView.TryGetEntry((uint) icon.Icon, out var entry))
            return;

        var iconTexture = Plugin.TextureProvider.GetFromGame("common/font/fonticon_ps5.tex").GetWrapOrDefault();
        if (iconTexture == null)
            return;

        var texSize = new Vector2(iconTexture.Width, iconTexture.Height);

        // GetFontSize() is the line height actually drawn. Do not multiply by Dalamud's
        // global scale: that made icon lines taller than the text at scale 1.5.
        var line = ImGui.GetFontSize();
        var sizeRatio = entry.Height > 0f ? line / entry.Height : 1f;
        var size = new Vector2(entry.Width, entry.Height) * sizeRatio;

        var uv0 = new Vector2(entry.Left, entry.Top + 170) * 2 / texSize;
        var uv1 = new Vector2(entry.Left + entry.Width, entry.Top + entry.Height + 170) * 2 / texSize;

        ImGui.Image(iconTexture.Handle, size, uv0, uv1);
        ImGuiUtil.PostPayload(chunk, handler);
    }

    public void DrawChunk(Chunk chunk, bool wrap = true, PayloadHandler? handler = null, float lineWidth = 0f)
    {
        if (chunk is IconChunk icon)
        {
            DrawIcon(chunk, icon, handler);
            return;
        }

        if (chunk is not TextChunk text)
            return;

        if (chunk.Link is EmotePayload emotePayload && Plugin.Config.ShowEmotes)
        {
            var emoteSize = ImGui.CalcTextSize("W");
            emoteSize = emoteSize with { Y = emoteSize.X } * 1.5f;

            // TextWrap doesn't work for emotes, so we have to wrap them manually
            if (ImGui.GetContentRegionAvail().X < emoteSize.X)
                ImGui.NewLine();

            // We only draw a dummy if it is still loading, in the case it failed we draw the actual name
            var image = EmoteCache.GetEmote(emotePayload.Code);
            if (image is { Failed: false })
            {
                if (image.IsLoaded)
                    image.Draw(emoteSize);
                else
                    ImGui.Dummy(emoteSize);

                if (ImGui.IsItemHovered())
                    ImGuiUtil.Tooltip(emotePayload.Code);

                return;
            }
        }

        var type = text.FallbackColor is ChatType fallback ? ChatTypeExt.Normalize((ushort)fallback) : (ChatType?)null;
        var obtain = IsObtainChunk(chunk, text, type);
        var color = text.Color;
        // A white item-rarity payload, or a failed swatch read, was covering the yellow
        // the game uses for "You obtain". Keep real rarity colors (green, blue, purple).
        // The push below is unconditional for these lines: a null Vector4 does not push,
        // and the style color left in place is white.
        if (obtain && (color == null || ColourUtil.IsNearWhite(color.Value)))
            color = LootYellow();
        else if (color == null && type != null)
            color = VanillaGameLook.ResolveChannelColor(type.Value);

        if (obtain && chunk.Message is { LoggedLootColor: false } message)
        {
            message.LoggedLootColor = true;
            var code = message.Code;
            var resolved = color == null ? "none" : ColourUtil.Vector4ToRgbaHex(color.Value);
            var fg = text.Foreground?.ToString("X8") ?? "none";
            var snippet = (text.Content ?? "").Replace("\n", " ");
            if (snippet.Length > 80)
                snippet = snippet[..80];
            var scale = ImGuiHelpers.GlobalScale;
            Plugin.Log.Information($"Loot color: type={code.Type} ({(ushort)code.Type}) raw={code.RawLogKind} source={code.Source} target={code.Target} fg={fg} resolved={resolved} globalScale={scale:0.##} text=\"{snippet}\"");
        }

        using var pushedColor = ImRaii.PushColor(ImGuiCol.Text, color);

        // Vanilla italic is the same Axis handle at the log size. If that atlas entry is still
        // building, leave the regular Axis (or the current font) in place for this frame.
        IDisposable? italicFont = null;
        if (text.Italic)
        {
            italicFont = Plugin.Config.VanillaStyle
                ? Plugin.FontManager.PushVanillaAxisItalic()
                : (Plugin.Config.FontsEnabled && Plugin.FontManager.ItalicFont != null
                    ? Plugin.FontManager.ItalicFont
                    : Plugin.FontManager.AxisItalic).Push();
        }

        // Check for contains here as sometimes there are multiple
        // TextChunks with the same PlayerPayload but only one has the name.
        // E.g. party chat with cross world players adds extra chunks.
        //
        // Note: This has been null before, I'm guessing due to some issues with
        // other plugins. New TextChunks will now enforce empty string in ctor,
        // but old ones may still be null.
        // ReSharper disable once NullCoalescingConditionIsAlwaysNotNullAccordingToAPIContract
        var content = text.Content ?? "";
        if (PlayerUtil.ScreenshotMode)
        {
            if (chunk.Link is PlayerPayload playerPayload)
                content = PlayerUtil.HidePlayerInString(content, playerPayload.PlayerName, playerPayload.World.RowId);
            else if (Plugin.PlayerState.IsLoaded)
                content = PlayerUtil.HidePlayerInString(content, Plugin.PlayerState.CharacterName, Plugin.PlayerState.HomeWorld.RowId);
        }

        if (wrap)
        {
            ImGuiUtil.WrapText(content, chunk, handler, Plugin.DefaultText, lineWidth);
        }
        else
        {
            VanillaText.DrawEdge(ImGui.GetCursorScreenPos(), content);
            ImGui.TextUnformatted(content);
            ImGuiUtil.PostPayload(chunk, handler);
        }

        italicFont?.Dispose();
    }

    /// <summary>
    /// Vanilla yellow for "You obtain". Used when the game swatch is missing or white,
    /// including the item-name chunk, which carries its own foreground and does not
    /// contain the word itself.
    /// </summary>
    private static Vector4 LootYellow()
    {
        var resolved = VanillaGameLook.ResolveChannelColor(ChatType.LootNotice);
        if (resolved == null || ColourUtil.IsNearWhite(resolved.Value))
            return new Vector4(1f, 1f, 176f / 255f, 1f);
        return resolved.Value;
    }

    /// <summary>
    /// The loot type, or a non-player line whose text says "obtain". The item name is
    /// a later chunk of the same message, so the check looks at the message, not only
    /// the chunk being drawn. Global scale does not choose a different color path;
    /// the scale is logged with the resolved color when one of these lines draws.
    /// </summary>
    private static bool IsObtainChunk(Chunk chunk, TextChunk text, ChatType? type)
    {
        if (type is ChatType kind && (kind.UsesItemNoticeColor() || IsObtainLine(kind, text.Content)))
            return true;

        var message = chunk.Message;
        if (message == null)
            return false;

        if (message.Code.Type.UsesItemNoticeColor())
            return true;

        if (message.Code.Type.IsPlayerChannel())
            return false;

        foreach (var part in message.Content)
        {
            if (part is TextChunk other && IsObtainLine(message.Code.Type, other.Content))
                return true;
        }

        return false;
    }

    /// <summary>
    /// "You obtain" lines that arrived under a system-style type instead of LootNotice.
    /// Spoken channels are left alone so a player saying the words is not recolored.
    /// </summary>
    private static bool IsObtainLine(ChatType type, string? text)
    {
        if (type.IsPlayerChannel() || string.IsNullOrEmpty(text))
            return false;

        return text.Contains("obtain", StringComparison.OrdinalIgnoreCase);
    }
}