using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using ChatTwo.Code;
using ChatTwo.GameFunctions;
using ChatTwo.GameFunctions.Types;
using ChatTwo.Resources;
using ChatTwo.Ui.Handler;
using ChatTwo.Ui.Style;
using ChatTwo.Util;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Interface;
using Dalamud.Interface.Style;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using FFXIVClientStructs.FFXIV.Client.UI;
using Dalamud.Bindings.ImGui;
using Lumina.Extensions;

namespace ChatTwo.Ui.ChatLog;

public partial class ChatLog : Window, IChatWindow
{
    private const string ChatChannelPicker = "chat-channel-picker";

    private readonly Plugin Plugin;
    public readonly InputHandler InputHandler;

    public bool TellSpecial;
    private readonly Stopwatch LastResize = new();

    // Used to detect channel changes for the webinterface
    public Chunk[] PreviousChannel = [];

    private unsafe ImGuiViewport* LastViewport;
    private bool WasDocked;

    private bool DrewThisFrame;

    public bool IsHidden;
    public HideState CurrentHideState { get; set; } = HideState.None;

    public Vector2 LastWindowPos { get; set; } = Vector2.Zero;
    public Vector2 LastWindowSize { get; set; } = Vector2.Zero;

    public readonly List<bool> PopOutDocked = [];
    public readonly HashSet<Guid> PopOutWindows = [];
    private readonly Dictionary<Guid, Popout> PopOutById = [];

    /// <summary>First visible index in the bottom tab strip when there are more tabs than fit.</summary>
    private int _vanillaTabFirst;
    private float _vanillaTabBudget = -1f;

    // Per tab: last content height, whether the reader is parked on the bottom,
    // and a one-frame correction after a new line (SetScrollHere's target can
    // land short of the max that is only known on the next frame).
    private readonly Dictionary<Guid, float> _logContentY = new();
    private readonly Dictionary<Guid, bool> _logFollowBottom = new();
    private readonly Dictionary<Guid, bool> _logStickPending = new();

    public ChatLog(Plugin plugin) : base($"{Plugin.PluginName}###chat2vanilla")
    {
        Plugin = plugin;

        Size = new Vector2(500, 250);
        SizeCondition = ImGuiCond.FirstUseEver;

        PositionCondition = ImGuiCond.Always;

        IsOpen = true;
        RespectCloseHotkey = false;
        DisableWindowSounds = true;

        InputHandler = new InputHandler(this, plugin, "MainChatLog");

        Plugin.Commands.Register(Plugin.ClearCommand, "Clear the Chat 2 Vanilla chat log").Execute += ClearLog;
        Plugin.Commands.Register(Plugin.MainCommand).Execute += ToggleChat;

        Plugin.ClientState.Login += Login;
        Plugin.ClientState.Logout += Logout;

        Plugin.AddonLifecycle.RegisterListener(AddonEvent.PostUpdate, "ItemDetail", MoveTooltip);
        Plugin.AddonLifecycle.RegisterListener(AddonEvent.PostUpdate, "ActionDetail", MoveTooltip);
    }

    public void Dispose()
    {
        Plugin.AddonLifecycle.UnregisterListener(AddonEvent.PostUpdate, "ItemDetail", MoveTooltip);
        Plugin.AddonLifecycle.UnregisterListener(AddonEvent.PostUpdate, "ActionDetail", MoveTooltip);

        Plugin.ClientState.Logout -= Logout;
        Plugin.ClientState.Login -= Login;
        Plugin.Commands.Register(Plugin.MainCommand).Execute -= ToggleChat;
        Plugin.Commands.Register(Plugin.ClearCommand).Execute -= ClearLog;
    }

    private void Logout(int _, int __)
    {
        Plugin.MessageManager.ClearAllTabs();
    }

    private void Login()
    {
        Plugin.MessageManager.FilterAllTabsAsync();
    }

    public unsafe void Activated(ChatActivatedArgs args)
    {
        TellSpecial = args.TellSpecial;

        InputHandler.Activate = true;
        InputHandler.PlayedClosingSound = false;
        if (Plugin.Config.PlaySounds)
            UIGlobals.PlaySoundEffect(InputHandler.ChatOpenSfx);

        // Don't set the channel or text content when activating a disabled tab.
        if (Plugin.CurrentTab.InputDisabled)
        {
            // The closing sound would've been immediately played in this case.
            InputHandler.PlayedClosingSound = true;
            return;
        }

        if (args.AddIfNotPresent != null && !InputHandler.ChatInput.Contains(args.AddIfNotPresent))
        {
            // Replace the full chat input if it's a command
            if (args.AddIfNotPresent.StartsWith('/'))
                InputHandler.ChatInput = args.AddIfNotPresent;
            else
                InputHandler.ChatInput += args.AddIfNotPresent;
        }

        if (args.Input != null)
        {
            // Replace the full chat input if it's a command
            if (args.Input.StartsWith('/'))
                InputHandler.ChatInput = args.Input;
            else
                InputHandler.ChatInput += args.Input;
        }

        var (info, reason, target) = (args.ChannelSwitchInfo, args.TellReason, args.TellTarget);
        if (info.Channel != null)
        {
            var targetChannel = info.Channel;
            if (info.Channel is InputChannel.Tell)
            {
                if (info.Rotate != RotateMode.None)
                {
                    var idx = Plugin.CurrentTab.CurrentChannel.TempChannel != InputChannel.Tell
                        ? 0 : info.Rotate == RotateMode.Reverse
                            ? -1 : 1;

                    var tellInfo = Plugin.Functions.Chat.GetTellHistoryInfo(idx);
                    if (tellInfo != null && reason != null)
                        Plugin.CurrentTab.CurrentChannel.TempTellTarget = new TellTarget(tellInfo.Name, (ushort) tellInfo.World, tellInfo.ContentId, reason.Value);
                }
                else
                {
                    Plugin.CurrentTab.CurrentChannel.TellTarget = null;
                    if (target != null)
                    {
                        if (info.Permanent)
                        {
                            Plugin.CurrentTab.CurrentChannel.TellTarget = target;
                        }
                        else
                        {
                            Plugin.CurrentTab.CurrentChannel.UseTempChannel = true;
                            Plugin.CurrentTab.CurrentChannel.TempTellTarget = target;
                        }
                    }
                }
            }
            else
            {
                Plugin.CurrentTab.CurrentChannel.TellTarget = null;
            }

            if (info.Channel is InputChannel.Linkshell1 or InputChannel.CrossLinkshell1 && info.Rotate != RotateMode.None)
            {
                var module = UIModule.Instance();

                // If any of these operations fail, do nothing.
                if (info.Permanent)
                {
                    // Rotate using the game's code.
                    if (info.Channel == InputChannel.Linkshell1)
                    {
                        Chat.RotateLinkshellHistory(info.Rotate);
                        targetChannel = info.Channel + (uint)module->LinkshellCycle;
                    }
                    else
                    {
                        Chat.RotateCrossLinkshellHistory(info.Rotate);
                        targetChannel = info.Channel + (uint)module->CrossWorldLinkshellCycle;
                    }
                }
                else
                {
                    targetChannel = Chat.ResolveTempInputChannel(Plugin.CurrentTab.CurrentChannel.TempChannel, info.Channel.Value, info.Rotate);
                }
            }

            if (targetChannel == null || !Chat.IsChannelOrExistingLinkshell(targetChannel.Value))
            {
                Plugin.Log.Warning($"Channel was set to an invalid value '{targetChannel}', ignoring");
                return;
            }

            if (info.Permanent)
            {
                Plugin.Functions.Chat.SetChannelWithExtraChat(targetChannel);
            }
            else
            {
                Plugin.CurrentTab.CurrentChannel.UseTempChannel = true;
                Plugin.CurrentTab.CurrentChannel.TempChannel = targetChannel.Value;
            }
        }

        if (info.Text != null && InputHandler.ChatInput.Length == 0)
            InputHandler.ChatInput = info.Text;
    }

    public float GetRemainingHeightForMessageLog(bool supportsInputPreview)
    {
        var height = ImGui.GetContentRegionAvail().Y - ImGui.CalcTextSize("A").Y * 2 - ImGui.GetStyle().ItemSpacing.Y - ImGui.GetStyle().FramePadding.Y * 2;

        if (supportsInputPreview && Plugin.Config.PreviewPosition is PreviewPosition.Inside)
            height -= Plugin.InputPreview.PreviewHeight;

        return height;
    }

    public void ChangeTab(int index) {
        Plugin.WantedTab = index;
        InputHandler.LastActivityTime = InputHandler.FrameTime;
    }

    public void ChangeTabDelta(int offset)
    {
        var newIndex = (Plugin.LastTab + offset) % Plugin.Config.Tabs.Count;
        while (newIndex < 0)
            newIndex += Plugin.Config.Tabs.Count;
        ChangeTab(newIndex);
    }

    private void TabSwitched(Tab newTab, Tab previousTab)
    {
        // Use the fixed channel if set by the user, or set it to the current tabs channel if this tab wasn't accessed before
        if (newTab.Channel is not null)
            newTab.CurrentChannel.Channel = newTab.Channel.Value;
        else if (newTab.CurrentChannel.Channel is InputChannel.Invalid)
            newTab.CurrentChannel = previousTab.CurrentChannel;

        Plugin.Functions.Chat.SetChannelWithExtraChat(newTab.CurrentChannel.Channel);

        // Inform the webinterface about tab switch
        // TODO implement tabs in the webinterface
        Plugin.ServerCore.SendNewLogin();
    }

    public void BeginFrame()
    {
        DrewThisFrame = false;
    }

    public void FinalizeFrame()
    {
        if (!DrewThisFrame)
            InputHandler.InputFocused = false;
    }

    public override unsafe void PreOpenCheck()
    {
        Flags = ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoFocusOnAppearing;
        if (!Plugin.Config.CanMove)
            Flags |= ImGuiWindowFlags.NoMove;

        if (!Plugin.Config.CanResize)
            Flags |= ImGuiWindowFlags.NoResize;

        if (Plugin.Config.VanillaStyle)
        {
            // The top-right game grip is the resize handle. ImGui's corner grip and border resize stay off.
            Flags |= ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoResize;
            BgAlpha = 0f;
        }
        else
        {
            if (!Plugin.Config.ShowTitleBar)
                Flags |= ImGuiWindowFlags.NoTitleBar;

            if (LastViewport == ImGuiHelpers.MainViewport.Handle && !WasDocked)
                BgAlpha = Plugin.Config.WindowAlpha / 100f;
        }

        LastViewport = ImGui.GetWindowViewport().Handle;
        WasDocked = ImGui.IsWindowDocked();
    }

    public override bool DrawConditions()
    {
        InputHandler.FrameTime = Environment.TickCount64;
        if (IsHidden)
            return false;

        if (!Plugin.Config.HideWhenInactive || (!Plugin.Config.InactivityHideActiveDuringBattle && Plugin.InBattle) ||  InputHandler.Activate)
        {
            InputHandler.LastActivityTime =  InputHandler.FrameTime;
            return true;
        }

        var currentTab = Plugin.CurrentTab; // local to avoid calling the getter repeatedly
        var lastActivityTime = Plugin.Config.Tabs
            .Where(tab => !tab.PopOut && (tab.UnhideOnActivity || tab == currentTab))
            .Select(tab => tab.LastActivity)
            .Append( InputHandler.LastActivityTime)
            .Max();
        return  InputHandler.FrameTime - lastActivityTime <= 1000 * Plugin.Config.InactivityHideTimeout;
    }

    public override void PreDraw()
    {
        if (Plugin.Config.KeepInputFocus &&  InputHandler.Activate)
            ImGui.SetWindowFocus(WindowName);

        if (Plugin.Config.VanillaStyle)
            ChatChrome.PushWindowStyle();
        else if (Plugin.Config is { OverrideStyle: true, ChosenStyle: not null })
            StyleModel.GetConfiguredStyles()?.FirstOrDefault(style => style.Name == Plugin.Config.ChosenStyle)?.Push();
    }

    public override void PostDraw()
    {
        // Set Activate to false after draw to avoid repeatedly trying to focus
        // the text input in a tab with input disabled. The usual way that
        // Activate gets disabled is via the text input callback, but that
        // doesn't get called if the input is disabled.
        if (Plugin.CurrentTab.InputDisabled)
            InputHandler.Activate = false;

        if (Plugin.Config.VanillaStyle)
            ChatChrome.PopWindowStyle();
        else if (Plugin.Config is { OverrideStyle: true, ChosenStyle: not null })
            StyleModel.GetConfiguredStyles()?.FirstOrDefault(style => style.Name == Plugin.Config.ChosenStyle)?.Pop();
    }

    public override void OnClose()
    {
        // We force the main log to be always open
        IsOpen = true;
    }

    public override void Draw()
    {
        DrewThisFrame = true;
        using var font = ChatChrome.PushContentFont();
        try
        {
            DrawChatLog();
            AddPopOutsToDraw();
            InputHandler.AutoCompleteHandler.DrawAutoComplete();
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "Error drawing Chat Log window");
            // Prevent recurring draw failures from constantly trying to grab
            // input focus, which breaks every other ImGui window.
            InputHandler.Activate = false;
        }
    }

    private static bool IsChatMode => Plugin.Config.PreviewPosition is PreviewPosition.Inside or PreviewPosition.Tooltip;
    private unsafe void DrawChatLog()
    {
        // Position change has applied, so we set it to null again
        Position = null;

        var currentSize = ImGui.GetWindowSize();
        var resized = LastWindowSize != currentSize;
        LastWindowSize = currentSize;
        LastWindowPos = ImGui.GetWindowPos();

        if (resized)
            LastResize.Restart();

        LastViewport = ImGui.GetWindowViewport().Handle;
        WasDocked = ImGui.IsWindowDocked();

        if (IsChatMode && Plugin.InputPreview.IsDrawable)
            Plugin.InputPreview.CalculatePreview();

        if (ChatChrome.Enabled)
        {
            DrawVanillaBody();
            return;
        }

        if (Plugin.Config.SidebarTabView)
            DrawTabSidebar();
        else
            DrawTabBar();

        var activeTab = Plugin.CurrentTab;

        // This tab has a fixed channel, so we force this channel to be always set as current
        if (activeTab.Channel is not null)
            activeTab.CurrentChannel.SetChannel(activeTab.Channel.Value);

        if (Plugin.Config.PreviewPosition is PreviewPosition.Inside && Plugin.InputPreview.IsDrawable)
            Plugin.InputPreview.DrawPreview();

        using (ImRaii.PushStyle(ImGuiStyleVar.ItemSpacing, Vector2.Zero))
            DrawChannelName(activeTab, true);

        if (ImGuiUtil.IconButton(FontAwesomeIcon.Comment) && activeTab.Channel is null)
            ImGui.OpenPopup(ChatChannelPicker);

        if (activeTab.Channel is not null && ImGui.IsItemHovered())
            ImGuiUtil.Tooltip(Language.ChatLog_SwitcherDisabled);

        using (var popup = ImRaii.Popup(ChatChannelPicker))
        {
            if (popup)
            {
                foreach (var (name, channel) in GetValidChannels())
                    if (ImGui.Selectable(name))
                        Plugin.Functions.Chat.SetChannelWithExtraChat(channel);
            }
        }

        ImGui.SameLine();

        var buttonWidth = ImGuiUtil.CalcIconButtonSize().X;
        var showNovice = Plugin.Config.ShowNoviceNetwork && GameFunctions.GameFunctions.IsMentor();
        var buttonsRight = 1 + (showNovice ? 1 : 0) + (Plugin.Config.ShowHideButton ? 1 : 0);
        var inputWidth = ImGui.GetContentRegionAvail().X - buttonWidth * buttonsRight - ImGui.GetStyle().ItemSpacing.X * buttonsRight;
        InputHandler.DrawInputArea(activeTab, inputWidth, ref TellSpecial);

        ImGui.SameLine();

        if (ImGuiUtil.IconButton(FontAwesomeIcon.Cog))
            Plugin.SettingsWindow.Toggle();

        if (Plugin.Config.ShowHideButton)
        {
            ImGui.SameLine();
            if (ImGuiUtil.IconButton(FontAwesomeIcon.EyeSlash))
                UserHide();
        }

        if (ImGui.IsWindowHovered(ImGuiHoveredFlags.ChildWindows))
            InputHandler.LastActivityTime = InputHandler.FrameTime;

        if (showNovice)
        {
            ImGui.SameLine();

            if (ImGuiUtil.IconButton(FontAwesomeIcon.Leaf))
                GameFunctions.GameFunctions.ClickNoviceNetworkButton();
        }
    }

    public Dictionary<string, InputChannel> GetValidChannels()
    {
        var channels = new Dictionary<string, InputChannel>();
        foreach (var channel in Enum.GetValues<InputChannel>())
        {
            if (!channel.IsValid())
                continue;

            var name = Sheets.LogFilterSheet.FirstOrNull(row => row.LogKind == (byte) channel.ToChatType())?.Name.ToString() ?? channel.ToChatType().Name();
            if (channel.IsLinkshell())
            {
                var lsName = Chat.GetLinkshellName(channel.LinkshellIndex());
                if (string.IsNullOrWhiteSpace(lsName))
                    continue;

                name += $": {lsName}";
            }

            if (channel.IsCrossLinkshell())
            {
                var lsName = Chat.GetCrossLinkshellName(channel.LinkshellIndex());
                if (string.IsNullOrWhiteSpace(lsName))
                    continue;

                name += $": {lsName}";
            }

            // Check if the linkshell with this index is registered in
            // the ExtraChat plugin by seeing if the command is
            // registered. The command gets registered only if a
            // linkshell is assigned (and even gets unassigned if the
            // index changes!).
            if (channel.IsExtraChatLinkshell() && !Plugin.CommandManager.Commands.ContainsKey(channel.Prefix()))
                continue;

            channels.Add(name, channel);
        }

        return channels;
    }

    private Chunk[] ReadChannelName(Tab activeTab)
    {
        Chunk[] channelNameChunks;
        // Check the temp channel before others
        if (activeTab.CurrentChannel.UseTempChannel)
        {
            if (activeTab.CurrentChannel.TempTellTarget != null && activeTab.CurrentChannel.TempTellTarget.IsSet())
            {
                channelNameChunks = GenerateTellTargetName(activeTab.CurrentChannel.TempTellTarget);
            }
            else
            {
                string name;
                if (activeTab.CurrentChannel.TempChannel.IsLinkshell())
                {
                    var idx = (uint) activeTab.CurrentChannel.TempChannel - (uint) InputChannel.Linkshell1;
                    var lsName = Chat.GetLinkshellName(idx);
                    name = $"LS #{idx + 1}: {lsName}";
                }
                else if (activeTab.CurrentChannel.TempChannel.IsCrossLinkshell())
                {
                    var idx = (uint) activeTab.CurrentChannel.TempChannel - (uint) InputChannel.CrossLinkshell1;
                    var cwlsName = Chat.GetCrossLinkshellName(idx);
                    name = $"CWLS [{idx + 1}]: {cwlsName}";
                }
                else
                {
                    name = activeTab.CurrentChannel.TempChannel.ToChatType().Name();
                }

                channelNameChunks = [new TextChunk(ChunkSource.None, null, name)];
            }
        }
        else if (activeTab.CurrentChannel.TellTarget?.IsSet() == true)
        {
            channelNameChunks = GenerateTellTargetName(activeTab.CurrentChannel.TellTarget);
        }
        else if (activeTab is { Channel: { } channel })
        {
            if (channel == InputChannel.Tell && activeTab.TellTarget.IsSet())
            {
                channelNameChunks = GenerateTellTargetName(activeTab.TellTarget);
            }
            else
            {
                // We cannot lookup ExtraChat channel names from index over
                // IPC so we just don't show the name if it's the tabs channel.
                //
                // We don't call channel.ToChatType().Name() as it has the
                // long name as used in the settings window.
                channelNameChunks = [new TextChunk(ChunkSource.None, null, channel.IsExtraChatLinkshell() ? $"ECLS [{channel.LinkshellIndex() + 1}]" : channel.ToChatType().Name())];
            }
        }
        else if (Plugin.ExtraChat.ChannelOverride is var (overrideName, _))
        {
            // If the current channel is not an ExtraChat Linkshell add a warning for the user
            var warning = activeTab.CurrentChannel.Channel.IsExtraChatLinkshell()
                ? ""
                : $" (Warning: {activeTab.CurrentChannel.Channel.ToChatType().Name()})";

            channelNameChunks = [new TextChunk(ChunkSource.None, null, $"{overrideName}{warning}")];
        }
        else if (PlayerUtil.ScreenshotMode && activeTab.CurrentChannel.Channel is InputChannel.Tell && activeTab.CurrentChannel.TellTarget != null)
        {
            if (!string.IsNullOrWhiteSpace(activeTab.CurrentChannel.TellTarget.Name) && activeTab.CurrentChannel.TellTarget.World != 0)
            {
                // Note: don't use HidePlayerInString here because abbreviation settings do not affect this.
                var playerName = PlayerUtil.HashPlayer(activeTab.CurrentChannel.TellTarget.Name, activeTab.CurrentChannel.TellTarget.World);
                var world = Sheets.WorldSheet.TryGetRow(activeTab.CurrentChannel.TellTarget.World, out var worldRow)
                    ? worldRow.Name.ToString()
                    : "???";

                channelNameChunks =
                [
                    new TextChunk(ChunkSource.None, null, "Tell "),
                    new TextChunk(ChunkSource.None, null, playerName),
                    new IconChunk(ChunkSource.None, null, BitmapFontIcon.CrossWorld),
                    new TextChunk(ChunkSource.None, null, world),
                ];
            }
            else
            {
                // We still need to censor the name if we couldn't read valid data.
                channelNameChunks = [new TextChunk(ChunkSource.None, null, "Tell")];
            }
        }
        else
        {
            channelNameChunks = activeTab.CurrentChannel.Name.Count > 0
                ? activeTab.CurrentChannel.Name.ToArray()
                : [new TextChunk(ChunkSource.None, null, activeTab.CurrentChannel.Channel.ToChatType().Name())];
        }

        return channelNameChunks;
    }

    private Chunk[] GenerateTellTargetName(TellTarget tellTarget)
    {
        var playerName = tellTarget.Name;
        if (PlayerUtil.ScreenshotMode)
            // Note: don't use HidePlayerInString here because
            // abbreviation settings do not affect this.
            playerName = PlayerUtil.HashPlayer(tellTarget.Name, tellTarget.World);

        var world = Sheets.WorldSheet.TryGetRow(tellTarget.World, out var worldRow)
            ? worldRow.Name.ToString()
            : "???";

        return
        [
            new TextChunk(ChunkSource.None, null, "Tell "),
            new TextChunk(ChunkSource.None, null, playerName),
            new IconChunk(ChunkSource.None, null, BitmapFontIcon.CrossWorld),
            new TextChunk(ChunkSource.None, null, world)
        ];
    }

    public void UserHide()
    {
        CurrentHideState = HideState.User;
    }

    public void DrawMessageLog(Tab tab, PayloadHandler handler, float childHeight, bool switchedTab, float childWidth = -1f, VanillaRect? clip = null)
    {
        var childFlags = ChatChrome.Enabled ? ImGuiWindowFlags.NoScrollbar : ImGuiWindowFlags.None;
        using var child = ImRaii.Child("##chat2-messages", new Vector2(childWidth, childHeight), false, childFlags);
        if (!child.Success)
            return;

        // A child window is composited after the parent, so a clip on the parent does not
        // cover these glyphs. Intersect with the log panel while the text is submitted.
        if (clip is { } clipRect)
            ImGui.PushClipRect(clipRect.Min, clipRect.Max, true);
        try
        {
            // Push again inside the child. A child window does not always keep the parent's font,
            // and a pop-out is a separate window, so the game Axis has to be pushed on this draw.
            using var axis = ChatChrome.PushContentFont();

            if (tab.DisplayTimestamp && Plugin.Config.PrettierTimestamps)
                DrawLogTableStyle(tab, handler, switchedTab);
            else
                DrawLogNormalStyle(tab, handler, switchedTab);

            ChatChrome.NoteScroll();
        }
        finally
        {
            if (clip is not null)
                ImGui.PopClipRect();
        }
    }

    private void DrawLogNormalStyle(Tab tab, PayloadHandler handler, bool switchedTab)
    {
        using (ImRaii.PushStyle(ImGuiStyleVar.ItemSpacing, Vector2.Zero))
            DrawMessages(tab, handler, false);

        StickToBottom(tab, switchedTab);
        handler.Draw();
    }

    private void DrawLogTableStyle(Tab tab, PayloadHandler handler, bool switchedTab)
    {
        var compact = Plugin.Config.MoreCompactPretty;
        var oldItemSpacing = ImGui.GetStyle().ItemSpacing;
        var oldCellPadding = ImGui.GetStyle().CellPadding;
        // Dalamud stores cell padding already multiplied by its global scale. Divide that
        // back out so the gap between messages stays the same at scale 1 and scale 1.5.
        var global = ImGuiHelpers.GlobalScale;
        if (!float.IsFinite(global) || global < 0.05f)
            global = 1f;
        var cellPadY = compact ? 0f : oldCellPadding.Y / global;

        using (ImRaii.PushStyle(ImGuiStyleVar.ItemSpacing, Vector2.Zero))
        using (ImRaii.PushStyle(ImGuiStyleVar.CellPadding, oldCellPadding with { Y = cellPadY }))
        {
            using var table = ImRaii.Table("timestamp-table", 2, ImGuiTableFlags.PreciseWidths);
            if (!table.Success)
                return;

            ImGui.TableSetupColumn("timestamps", ImGuiTableColumnFlags.WidthFixed);
            ImGui.TableSetupColumn("messages", ImGuiTableColumnFlags.WidthStretch);

            DrawMessages(tab, handler, true, compact, cellPadY);

            using (ImRaii.PushStyle(ImGuiStyleVar.ItemSpacing, oldItemSpacing))
            using (ImRaii.PushStyle(ImGuiStyleVar.CellPadding, oldCellPadding))
            {
                StickToBottom(tab, switchedTab);
                handler.Draw();
            }
        }
    }

    /// <summary>
    /// Keep a log that is already at the bottom pinned there when a line arrives.
    /// SetScrollHereY every frame rounded onto a different pixel and the view shook.
    /// Scroll only when the content actually grew, then once more to the real max
    /// on the following frame (that max is not known until the new line has been submitted).
    /// </summary>
    private void StickToBottom(Tab tab, bool switchedTab)
    {
        var id = tab.Identifier;
        var maxY = ImGui.GetScrollMaxY();
        var y = ImGui.GetScrollY();
        var contentY = ImGui.GetCursorPosY();
        _logContentY.TryGetValue(id, out var previousY);
        _logContentY[id] = contentY;
        _logFollowBottom.TryGetValue(id, out var follow);
        if (!_logFollowBottom.ContainsKey(id))
            follow = true;
        _logStickPending.TryGetValue(id, out var pending);

        var grew = contentY > previousY + 0.5f;
        var scrollingUp = ImGui.IsWindowHovered() && ImGui.GetIO().MouseWheel > 0f;
        if (scrollingUp)
            follow = false;
        else if (!switchedTab && !grew && maxY > 1f && y < maxY - 4f)
            follow = false;
        else if (switchedTab || maxY <= 1f || y >= maxY - 2f)
            follow = true;

        if (switchedTab || (follow && grew))
        {
            ImGui.SetScrollHereY(1f);
            pending = true;
        }
        else if (pending && follow && maxY > 1f)
        {
            ImGui.SetScrollY(maxY);
            pending = false;
        }
        else
        {
            pending = false;
        }

        _logFollowBottom[id] = follow;
        _logStickPending[id] = pending;
    }

    private void DrawMessages(Tab tab, PayloadHandler handler, bool isTable, bool moreCompact = false, float oldCellPaddingY = 0)
    {
        try
        {
            // This may produce ApplicationException which is catched below.
            using var messages = tab.Messages.GetReadOnly(3);

            var reset = false;
            if (LastResize is { IsRunning: true, Elapsed.TotalSeconds: > 0.25 })
            {
                LastResize.Stop();
                LastResize.Reset();
                reset = true;
            }

            var lastPosY = ImGui.GetCursorPosY();
            var lastTimestamp = string.Empty;
            int? lastMessageHash = null;
            var sameCount = 0;

            var maxLines = Plugin.Config.MaxLinesToRender;
            var startLine = messages.Count > maxLines ? messages.Count - maxLines : 0;
            for (var i = startLine; i < messages.Count; i++)
            {
                var message = messages[i];
                if (reset)
                {
                    message.Height[tab.Identifier] = null;
                    message.IsVisible[tab.Identifier] = false;
                }

                if (Plugin.Config.CollapseDuplicateMessages)
                {
                    var messageHash = message.Hash;
                    var same = lastMessageHash == messageHash;
                    if (same)
                    {
                        sameCount += 1;
                        message.IsVisible[tab.Identifier] = false;
                        if (i != messages.Count - 1)
                            continue;
                    }

                    if (sameCount > 0)
                    {
                        ImGui.SameLine();
                        InputHandler.ChunkHandler.DrawChunks(
                            [new TextChunk(ChunkSource.None, null, $" ({sameCount + 1}x)") { FallbackColor = ChatType.System, Italic = true }],
                            true,
                            handler,
                            ImGui.GetContentRegionAvail().X
                        );
                        sameCount = 0;
                    }

                    lastMessageHash = messageHash;
                    if (same && i == messages.Count - 1)
                        continue;
                }

                // go to next row
                if (isTable)
                    ImGui.TableNextColumn();

                // Set the height of the previous message. `lastPosY` is set to
                // the top of the previous message, and the current cursor is at
                // the top of the current message.
                if (i > 0)
                {
                    var prevMessage = messages[i - 1];
                    prevMessage.Height.TryGetValue(tab.Identifier, out var prevHeight);
                    if (prevHeight == null || (prevMessage.IsVisible.TryGetValue(tab.Identifier, out var prevVisible) && prevVisible))
                    {
                        var newHeight = ImGui.GetCursorPosY() - lastPosY;

                        // Remove the padding from the bottom of the previous row and the top of the current row.
                        if (isTable && !moreCompact)
                            newHeight -= oldCellPaddingY * 2;

                        if (newHeight != 0)
                            prevMessage.Height[tab.Identifier] = newHeight;
                    }
                }
                lastPosY = ImGui.GetCursorPosY();

                // A row that sits fully outside the viewport keeps the height it was
                // drawn at. Testing that with a dummy and then rewinding left the
                // dummy's extent in the content size for one frame, so the next
                // frame shrank and the stuck-to-bottom view jumped.
                message.Height.TryGetValue(tab.Identifier, out var height);
                if (height is float known && known > 0f)
                {
                    var top = ImGui.GetCursorPosY();
                    var bandTop = ImGui.GetScrollY();
                    var bandBottom = bandTop + ImGui.GetWindowHeight();
                    const float margin = 8f;
                    var onScreen = top + known >= bandTop - margin && top <= bandBottom + margin;
                    if (!onScreen)
                    {
                        if (isTable)
                            ImGui.TableNextColumn();

                        ImGui.Dummy(new Vector2(10f, known));
                        message.IsVisible[tab.Identifier] = false;
                        continue;
                    }
                }

                if (tab.DisplayTimestamp)
                {
                    var localTime = message.Date.ToLocalTime();
                    var timestamp = localTime.ToString("t", !Plugin.Config.Use24HourClock ? null : CultureInfo.CreateSpecificCulture("de-DE"));
                    if (isTable)
                    {
                        if (!Plugin.Config.HideSameTimestamps || timestamp != lastTimestamp)
                        {
                            lastTimestamp = timestamp;
                            VanillaText.DrawEdge(ImGui.GetCursorScreenPos(), timestamp);
                            ImGui.TextUnformatted(timestamp);

                            // We use an IsItemHovered() check here instead of
                            // just calling Tooltip() to avoid computing the
                            // tooltip string for all visible items on every
                            // frame.
                            if (ImGui.IsItemHovered())
                                ImGuiUtil.Tooltip(localTime.ToString("F"));
                        }
                        else
                        {
                            // Avoids rendering issues caused by emojis in
                            // message content.
                            ImGui.TextUnformatted("");
                        }
                    }
                    else
                    {
                        InputHandler.ChunkHandler.DrawChunk(new TextChunk(ChunkSource.None, null, $"[{timestamp}] ") { Foreground = 0xFFFFFFFF, Color = ColourUtil.RgbaToVector4(0xFFFFFFFF)});
                        ImGui.SameLine();
                    }
                }

                if (isTable)
                    ImGui.TableNextColumn();

                var lineWidth = ImGui.GetContentRegionAvail().X;
                if (message.Sender.Count > 0)
                {
                    InputHandler.ChunkHandler.DrawChunks(message.Sender, true, handler, lineWidth);
                    ImGui.SameLine();
                }

                // We need to draw something otherwise the item visibility check below won't work.
                if (message.Content.Count == 0)
                    InputHandler.ChunkHandler.DrawChunks([new TextChunk(ChunkSource.Content, null, " ")], true, handler, lineWidth);
                else
                    InputHandler.ChunkHandler.DrawChunks(message.Content, true, handler, lineWidth);

                message.IsVisible[tab.Identifier] = ImGui.IsItemVisible();
            }
        }
        catch (ApplicationException)
        {
            // We couldn't get a reader lock on messages within 3ms, so
            // don't draw anything (and don't log a warning either).
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "Error drawing chat log");
        }
    }

    private void DrawVanillaBody()
    {
        var frame = ChatChrome.Frame();
        var previewWrap = Math.Max(40f, frame.Messages.W);
        if (Plugin.Config.PreviewPosition is PreviewPosition.Inside && Plugin.InputPreview.IsDrawable)
            Plugin.InputPreview.CalculatePreview(previewWrap);

        var previewH = 0f;
        if (Plugin.Config.PreviewPosition is PreviewPosition.Inside && Plugin.InputPreview.IsDrawable)
        {
            var room = frame.Label.Y - frame.Messages.Y - 28f * frame.Scale;
            previewH = Math.Clamp(Plugin.InputPreview.PreviewHeight, 0f, Math.Max(0f, room));
        }

        var content = ChatChrome.LogContentRect(frame);
        var messages = frame.Messages;
        if (previewH > 0f)
        {
            var previewTop = frame.Label.Y - 4f * frame.Scale - previewH;
            messages = messages with { H = Math.Max(frame.Scale * 20f, previewTop - 4f * frame.Scale - messages.Y) };
        }

        // Same inner edge as a pop-out. The list must not draw into part 12's border fade.
        var messageTop = Math.Max(messages.Y, content.Y);
        var messageBottom = Math.Min(messages.Y + messages.H, content.Y + content.H);
        messages = new VanillaRect(messages.X, messageTop, messages.W, Math.Max(frame.Scale * 20f, messageBottom - messageTop));

        var (tabIndex, switched) = ResolveVanillaTab();
        var shown = tabIndex >= 0 ? Plugin.Config.Tabs[tabIndex] : null;

        ChatChrome.BeginScrollDrag(frame.Scroll);
        ChatChrome.DrawLogBackground(frame, VanillaGameLook.LogBackgroundOpacityFactor(), VanillaGameLook.TabLogBackgroundTint(shown));
        ImGui.SetCursorScreenPos(messages.Min);
        if (tabIndex >= 0)
        {
            var tab = Plugin.Config.Tabs[tabIndex];
            tab.Unread = 0;
            DrawMessageLog(tab, InputHandler.PayloadHandler, messages.H, switched, messages.W, content);
        }
        else
        {
            ImGui.Dummy(messages.Size);
            ChatChrome.NoteScroll();
        }

        ChatChrome.PaintScrollbar(frame.Scroll);

        if (previewH > 0f)
        {
            var origin = new Vector2(frame.Messages.X, frame.Label.Y - 4f * frame.Scale - previewH);
            var previewSize = new Vector2(previewWrap, previewH);
            ImGui.SetCursorScreenPos(origin);
            var previewList = ImGui.GetWindowDrawList();
            previewList.PushClipRect(origin, origin + previewSize, true);
            using (var previewChild = ImRaii.Child("##vanilla-preview", previewSize, false, ImGuiWindowFlags.NoScrollbar))
            {
                if (previewChild)
                    Plugin.InputPreview.DrawPreview();
            }

            previewList.PopClipRect();
        }

        var activeTab = Plugin.CurrentTab;
        if (activeTab.Channel is not null)
            activeTab.CurrentChannel.SetChannel(activeTab.Channel.Value);

        DrawVanillaChannelAndInput(activeTab, ChatChannelPicker, true, GetValidChannels(), channel => Plugin.Functions.Chat.SetChannelWithExtraChat(channel), InputHandler, frame, ref TellSpecial);
        DrawVanillaTabStrip(frame, switched);

        if (ImGui.IsWindowHovered(ImGuiHoveredFlags.ChildWindows))
            InputHandler.LastActivityTime = InputHandler.FrameTime;

        if (Plugin.Config.CanResize)
            ChatChrome.DrawGrip(frame);
    }

    /// <summary>
    /// Channel name above a framed input, with the speech-bubble channel button on the left of the field.
    /// </summary>
    public void DrawVanillaChannelAndInput(
        Tab tab,
        string pickerId,
        bool sendChannelSwitch,
        Dictionary<string, InputChannel> channels,
        Action<InputChannel> applyChannel,
        InputHandler handler,
        VanillaFrame frame,
        ref bool tellSpecial,
        float? inputAlpha = null)
    {
        ChatChrome.Uld.EnsureLoaded();
        // The input frame is behind the channel name. Node 4 is linked in front of component 1012.
        // The tint follows the channel the text will use, including a /command typed this frame.
        Vector4? inputTint = null;
        var showTint = Plugin.Config.TintInputByChannel;
        if (showTint && Plugin.Config.TintInputOnlyWhileTyping)
        {
            showTint = handler.InputFocused
                || handler.Activate
                || handler.FocusedPreview
                || handler.AutoCompleteHandler.AutoCompleteInfo != null;
        }

        if (showTint)
        {
            var inputType = VanillaGameLook.ActiveInputChatType(tab, handler.ChatInput);
            inputTint = VanillaGameLook.InputBackgroundTint(inputType, Plugin.Config.InputChannelTint);
        }

        ChatChrome.DrawInputFrame(frame.Input, inputAlpha, inputTint);
        using (Plugin.FontManager.PushFixedAxis(14f))
        using (ImRaii.PushStyle(ImGuiStyleVar.ItemSpacing, Vector2.Zero))
        {
            var labelY = MathF.Round(frame.Label.Y + Math.Max(0f, (frame.Label.H - ImGui.GetTextLineHeight()) * 0.5f));
            ImGui.SetCursorScreenPos(new Vector2(MathF.Round(frame.Label.X), labelY));
            DrawChannelName(tab, sendChannelSwitch);
        }

        ImGui.SetCursorScreenPos(frame.Bubble.Min);
        if (ImGui.InvisibleButton($"###channel-icon-{pickerId}", frame.Bubble.Size) && tab.Channel is null)
            ImGui.OpenPopup(pickerId);
        var hovered = ImGui.IsItemHovered();
        var tint = hovered ? new Vector4(1.2f, 1.2f, 1.2f, 1f) : Vector4.One;
        if (!ChatChrome.DrawPart(ChatChrome.Uld.ChannelIcon, frame.Bubble.Min, frame.Bubble.Max, 1f, tint))
            ChatChrome.DrawChannelIcon(frame.Bubble.Min, frame.Bubble.W);

        if (tab.Channel is not null && hovered)
            ImGuiUtil.Tooltip(Language.ChatLog_SwitcherDisabled);

        using (var popup = ImRaii.Popup(pickerId))
        {
            if (popup)
            {
                foreach (var (name, channel) in channels)
                {
                    if (ImGui.Selectable(name))
                        applyChannel(channel);
                }
            }
        }

        var textX = MathF.Round(frame.Input.X + 12f * frame.Scale);
        var inputW = Math.Max(20f, frame.Input.W - 20f * frame.Scale);
        var padY = MathF.Max(0f, MathF.Round((frame.Input.H - ImGui.GetTextLineHeight()) * 0.5f));
        ImGui.SetCursorScreenPos(new Vector2(textX, frame.Input.Y));
        using (ImRaii.PushStyle(ImGuiStyleVar.FramePadding, new Vector2(0f, padY)))
            handler.DrawInputArea(tab, inputW, ref tellSpecial);
    }

    private (int Index, bool Switched) ResolveVanillaTab()
    {
        var previous = Plugin.CurrentTab;
        var selected = Plugin.LastTab;
        if (Plugin.WantedTab is int wanted
            && wanted >= 0
            && wanted < Plugin.Config.Tabs.Count
            && !Plugin.Config.Tabs[wanted].PopOut)
            selected = wanted;

        Plugin.WantedTab = null;

        if (selected < 0 || selected >= Plugin.Config.Tabs.Count || Plugin.Config.Tabs[selected].PopOut)
        {
            selected = -1;
            for (var i = 0; i < Plugin.Config.Tabs.Count; i++)
            {
                if (Plugin.Config.Tabs[i].PopOut)
                    continue;

                selected = i;
                break;
            }
        }

        if (selected < 0)
            return (-1, false);

        var switched = selected != Plugin.LastTab;
        if (switched)
            TabSwitched(Plugin.Config.Tabs[selected], previous);

        Plugin.LastTab = selected;
        return (selected, switched);
    }

    private void DrawVanillaTabStrip(VanillaFrame frame, bool switched)
    {
        var scale = frame.Scale;
        // Vanilla tabs sit flush. The rounded ends belong to the strip, not to each pill.
        var gap = 0f;
        var strip = frame.TabStrip;

        var visible = new List<int>();
        for (var i = 0; i < Plugin.Config.Tabs.Count; i++)
        {
            if (!Plugin.Config.Tabs[i].PopOut)
                visible.Add(i);
        }

        var labels = new string[visible.Count];
        var widths = new float[visible.Count];
        using (Plugin.FontManager.PushFixedAxis(12f))
        {
            // 9px each side keeps the four stock names inside the 204px strip. The pill's
            // own nine-grid corners are 8px, so the label stays clear of the rounded ends.
            var padX = 18f * scale;
            for (var n = 0; n < visible.Count; n++)
            {
                labels[n] = Plugin.Config.Tabs[visible[n]].Name;
                widths[n] = Math.Max(ImGui.CalcTextSize(labels[n]).X + padX, 28f * scale);
            }

            // The row fills the strip. The right cap stays at the strip's right edge, just before the buttons.
            var runLeft = strip.X;
            var budget = Math.Max(28f * scale, strip.W);

            if (_vanillaTabFirst >= visible.Count)
                _vanillaTabFirst = Math.Max(0, visible.Count - 1);

            var selectedLocal = -1;
            for (var n = 0; n < visible.Count; n++)
            {
                if (visible[n] != Plugin.LastTab)
                    continue;
                selectedLocal = n;
                break;
            }

            var budgetChanged = Math.Abs(_vanillaTabBudget - budget) > 0.5f;
            _vanillaTabBudget = budget;
            if (selectedLocal >= 0 && (switched || budgetChanged))
                RevealVanillaTab(selectedLocal, widths, gap, budget);
            PullVanillaTabsBack(widths, gap, budget);

            var end = VisibleTabEnd(_vanillaTabFirst, widths, gap, budget);
            if (visible.Count > 0 && end <= _vanillaTabFirst)
                end = Math.Min(visible.Count, _vanillaTabFirst + 1);

            var canLeft = _vanillaTabFirst > 0;
            var canRight = end < visible.Count;
            var rowMax = new Vector2(frame.Close.X + frame.Close.W, strip.Y + strip.H);
            if (visible.Count > 0 && ImGui.IsMouseHoveringRect(new Vector2(runLeft, strip.Y), rowMax))
            {
                var wheel = ImGui.GetIO().MouseWheel;
                if (wheel > 0f && canLeft)
                    _vanillaTabFirst--;
                else if (wheel < 0f && canRight)
                    _vanillaTabFirst++;
                if (wheel != 0f)
                {
                    end = VisibleTabEnd(_vanillaTabFirst, widths, gap, budget);
                    canLeft = _vanillaTabFirst > 0;
                    canRight = end < visible.Count;
                }
            }

            var shown = Math.Max(0, end - _vanillaTabFirst);
            var used = 0f;
            for (var n = _vanillaTabFirst; n < end; n++)
            {
                if (n > _vanillaTabFirst)
                    used += gap;
                used += widths[n];
            }

            // Leftover space goes into the pills so they meet the right cap.
            // Tabs are never cut in half.
            var slack = budget - used;
            if (shown > 0 && slack > 0.5f)
            {
                var add = slack / shown;
                for (var n = _vanillaTabFirst; n < end; n++)
                    widths[n] += add;
            }

            if (shown > 0)
            {
                var snapped = 0f;
                var minTab = MathF.Round(28f * scale);
                for (var n = _vanillaTabFirst; n < end; n++)
                {
                    widths[n] = MathF.Max(minTab, MathF.Round(widths[n]));
                    snapped += widths[n];
                }

                widths[end - 1] = MathF.Max(minTab, widths[end - 1] + (MathF.Round(budget) - snapped));
            }

            var drawList = ImGui.GetWindowDrawList();
            drawList.PushClipRect(new Vector2(runLeft - 10f * scale, strip.Y - 8f * scale), new Vector2(runLeft + budget + 2f * scale, strip.Y + strip.H), true);
            var x = runLeft;
            var clickedTab = -1;
            var drewTab = false;
            VanillaRect activeRect = default;
            var hasActive = false;
            for (var n = _vanillaTabFirst; n < end; n++)
            {
                var tabIndex = visible[n];
                var rect = new VanillaRect(x, strip.Y, widths[n], strip.H);
                drewTab = true;

                ImGui.SetCursorScreenPos(rect.Min);
                if (ImGui.InvisibleButton($"###log-tab-{tabIndex}", rect.Size))
                    clickedTab = tabIndex;

                var hovered = ImGui.IsItemHovered();
                DrawTabContextMenu(Plugin.Config.Tabs[tabIndex], tabIndex);
                var active = tabIndex == Plugin.LastTab;
                var entry = Plugin.Config.Tabs[tabIndex];
                var unread = !active && entry.Unread > 0 && entry.UnreadMode != UnreadMode.None;
                if (active)
                {
                    activeRect = rect;
                    hasActive = true;
                }

                ChatChrome.DrawTabFace(rect, hovered);

                var textSize = ImGui.CalcTextSize(labels[n]);
                var textPos = new Vector2(
                    MathF.Round(rect.X + (rect.W - textSize.X) * 0.5f),
                    MathF.Round(rect.Y + (rect.H - textSize.Y) * 0.5f));
                var color = active
                    ? Vector4.One
                    : unread && Plugin.Config.HighlightUnreadTabs
                        ? new Vector4(0.94f, 0.82f, 0.52f, 1f)
                        : new Vector4(0.86f, 0.86f, 0.86f, 1f);
                var clipPad = 8f * scale;
                drawList.PushClipRect(rect.Min + new Vector2(clipPad, 0f), rect.Max - new Vector2(clipPad, 0f), true);
                VanillaText.DrawAt(textPos, labels[n], color);
                drawList.PopClipRect();
                x += widths[n] + gap;
            }

            drawList.PopClipRect();

            var scrollDelta = 0;
            if (drewTab)
            {
                var run = new VanillaRect(runLeft, strip.Y, Math.Max(1f, x - runLeft), strip.H);
                ChatChrome.DrawTabCaps(run);
                var cap = 28f * scale;
                if (canLeft && ChatChrome.TabCapArrow("###vanilla-tab-left", new Vector2(run.X - cap, run.Y), cap, true))
                    scrollDelta--;
                if (canRight && ChatChrome.TabCapArrow("###vanilla-tab-right", new Vector2(run.X + run.W, run.Y), cap, false))
                    scrollDelta++;
            }

            if (hasActive)
                ChatChrome.DrawTabMarker(activeRect);

            if (scrollDelta != 0)
                clickedTab = -1;

            if (clickedTab >= 0 && clickedTab != Plugin.LastTab)
            {
                var previous = Plugin.CurrentTab;
                TabSwitched(Plugin.Config.Tabs[clickedTab], previous);
                Plugin.LastTab = clickedTab;
                Plugin.Config.Tabs[clickedTab].Unread = 0;
            }

            if (scrollDelta != 0 && visible.Count > 0)
                _vanillaTabFirst = Math.Clamp(_vanillaTabFirst + scrollDelta, 0, visible.Count - 1);
        }

        if (ChatChrome.DrawCircleIcon("###vanilla-add-tab", ChatChrome.Uld.ButtonPlus, frame.Plus, ChatChrome.VanillaGlyph.Plus))
        {
            Plugin.Config.Tabs.Add(new Tab());
            Plugin.SaveConfig();
            Plugin.WantedTab = Plugin.Config.Tabs.Count - 1;
        }

        if (ImGui.IsItemHovered())
            ImGuiUtil.Tooltip(Language.Options_Tabs_NewTab);

        if (ChatChrome.DrawCircleIcon("###vanilla-settings", ChatChrome.Uld.ButtonGear, frame.Gear, ChatChrome.VanillaGlyph.Gear))
            Plugin.SettingsWindow.Toggle();
        if (ImGui.IsItemHovered())
            ImGuiUtil.Tooltip(string.Format(Language.Settings_Title, Plugin.PluginName));

        if (ChatChrome.DrawCircleIcon("###vanilla-hide", ChatChrome.Uld.ButtonClose, frame.Close, ChatChrome.VanillaGlyph.Close))
            UserHide();
        if (ImGui.IsItemHovered())
            ImGuiUtil.Tooltip(Language.ChatLog_HideChat);
    }


    private void RevealVanillaTab(int localIndex, float[] widths, float gap, float viewport)
    {
        if (localIndex < _vanillaTabFirst)
            _vanillaTabFirst = localIndex;

        while (_vanillaTabFirst < localIndex && !TabSpanFits(_vanillaTabFirst, localIndex, widths, gap, viewport))
            _vanillaTabFirst++;
    }

    private void PullVanillaTabsBack(float[] widths, float gap, float viewport)
    {
        if (widths.Length == 0)
            return;

        while (_vanillaTabFirst > 0 && TabSpanFits(_vanillaTabFirst - 1, widths.Length - 1, widths, gap, viewport))
            _vanillaTabFirst--;
    }

    private static bool TabSpanFits(int start, int lastInclusive, float[] widths, float gap, float viewport)
    {
        if (widths.Length == 0 || start > lastInclusive)
            return true;

        var used = 0f;
        for (var i = start; i <= lastInclusive && i < widths.Length; i++)
        {
            if (i > start)
                used += gap;
            used += widths[i];
            if (used > viewport + 0.5f)
                return false;
        }

        return true;
    }

    private static int VisibleTabEnd(int start, float[] widths, float gap, float viewport)
    {
        var used = 0f;
        var end = start;
        for (var i = start; i < widths.Length; i++)
        {
            var next = used + (i > start ? gap : 0f) + widths[i];
            if (next > viewport + 0.5f && i > start)
                break;

            used = next;
            end = i + 1;
        }

        return end;
    }


    private void DrawTabBar()
    {
        using var tabBar = ImRaii.TabBar("##chat2-tabs");
        if (!tabBar.Success)
            return;

        var previousTab = Plugin.CurrentTab;
        for (var tabI = 0; tabI < Plugin.Config.Tabs.Count; tabI++)
        {
            var tab = Plugin.Config.Tabs[tabI];
            if (tab.PopOut)
                continue;

            var unread = tabI == Plugin.LastTab || tab.UnreadMode == UnreadMode.None || tab.Unread == 0 ? "" : $" ({tab.Unread})";
            var flags = ImGuiTabItemFlags.None;
            if (Plugin.WantedTab == tabI)
                flags |= ImGuiTabItemFlags.SetSelected;

            using var tabItem = ImRaii.TabItem($"{tab.Name}{unread}###log-tab-{tabI}", flags);
            DrawTabContextMenu(tab, tabI);

            if (!tabItem.Success)
                continue;

            var hasTabSwitched = Plugin.LastTab != tabI;
            Plugin.LastTab = tabI;

            if (hasTabSwitched)
                TabSwitched(tab, previousTab);

            tab.Unread = 0;
            DrawMessageLog(tab, InputHandler.PayloadHandler, GetRemainingHeightForMessageLog(true), hasTabSwitched);
        }

        Plugin.WantedTab = null;
    }

    private void DrawTabSidebar()
    {
        var currentTab = -1;
        using var tabTable = ImRaii.Table("tabs-table", 2, ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.Resizable);
        if (!tabTable.Success)
            return;

        ImGui.TableSetupColumn("tabs", ImGuiTableColumnFlags.WidthStretch, 1);
        ImGui.TableSetupColumn("chat", ImGuiTableColumnFlags.WidthStretch, 4);

        ImGui.TableNextColumn();

        var hasTabSwitched = false;
        var childHeight = GetRemainingHeightForMessageLog(true);
        using (var child = ImRaii.Child("##chat2-tab-sidebar", new Vector2(-1, childHeight)))
        {
            if (child)
            {
                var previousTab = Plugin.CurrentTab;
                for (var tabI = 0; tabI < Plugin.Config.Tabs.Count; tabI++)
                {
                    var tab = Plugin.Config.Tabs[tabI];
                    if (tab.PopOut)
                        continue;

                    var unread = tabI == Plugin.LastTab || tab.UnreadMode == UnreadMode.None || tab.Unread == 0 ? "" : $" ({tab.Unread})";
                    var clicked = ImGui.Selectable($"{tab.Name}{unread}###log-tab-{tabI}", Plugin.LastTab == tabI || Plugin.WantedTab == tabI);
                    DrawTabContextMenu(tab, tabI);

                    if (!clicked && Plugin.WantedTab != tabI)
                        continue;

                    currentTab = tabI;
                    hasTabSwitched = Plugin.LastTab != tabI;
                    Plugin.LastTab = tabI;
                    if (hasTabSwitched)
                        TabSwitched(tab, previousTab);
                }
            }
        }

        ImGui.TableNextColumn();

        if (currentTab == -1 && Plugin.LastTab < Plugin.Config.Tabs.Count)
        {
            currentTab = Plugin.LastTab;
            Plugin.Config.Tabs[currentTab].Unread = 0;
        }

        if (currentTab > -1)
            DrawMessageLog(Plugin.Config.Tabs[currentTab], InputHandler.PayloadHandler, childHeight, hasTabSwitched);

        Plugin.WantedTab = null;
    }

    private void DrawTabContextMenu(Tab tab, int i)
    {
        using var contextMenu = ImRaii.ContextPopupItem($"tab-context-menu-{i}");
        if (!contextMenu.Success)
            return;

        var anyChanged = false;
        var tabs = Plugin.Config.Tabs;

        ImGui.SetNextItemWidth(250f * ImGuiHelpers.GlobalScale);
        if (ImGui.InputText("##tab-name", ref tab.Name, 128))
            anyChanged = true;

        if (ImGuiUtil.IconButton(FontAwesomeIcon.TrashAlt, tooltip: Language.ChatLog_Tabs_Delete))
        {
            tabs.RemoveAt(i);
            Plugin.WantedTab = 0;

            anyChanged = true;
        }

        ImGui.SameLine();

        var (leftIcon, leftTooltip) = Plugin.Config.SidebarTabView
            ? (FontAwesomeIcon.ArrowUp, Language.ChatLog_Tabs_MoveUp)
            : (FontAwesomeIcon.ArrowLeft, Language.ChatLog_Tabs_MoveLeft);
        if (ImGuiUtil.IconButton(leftIcon, tooltip: leftTooltip) && i > 0)
        {
            (tabs[i - 1], tabs[i]) = (tabs[i], tabs[i - 1]);
            ImGui.CloseCurrentPopup();
            anyChanged = true;
        }

        ImGui.SameLine();

        var (rightIcon, rightTooltip) = Plugin.Config.SidebarTabView
            ? (FontAwesomeIcon.ArrowDown, Language.ChatLog_Tabs_MoveDown)
            : (FontAwesomeIcon.ArrowRight, Language.ChatLog_Tabs_MoveRight);
        if (ImGuiUtil.IconButton(rightIcon, tooltip: rightTooltip) && i < tabs.Count - 1)
        {
            (tabs[i + 1], tabs[i]) = (tabs[i], tabs[i + 1]);
            ImGui.CloseCurrentPopup();
            anyChanged = true;
        }

        ImGui.SameLine();
        if (ImGuiUtil.IconButton(FontAwesomeIcon.WindowRestore, tooltip: Language.ChatLog_Tabs_PopOut))
        {
            tab.PopOut = true;
            anyChanged = true;
        }

        if (anyChanged)
            Plugin.SaveConfig();
    }

    private void AddPopOutsToDraw()
    {
        if (PopOutDocked.Count != Plugin.Config.Tabs.Count)
        {
            PopOutDocked.Clear();
            PopOutDocked.AddRange(Enumerable.Repeat(false, Plugin.Config.Tabs.Count));
        }

        for (var i = 0; i < Plugin.Config.Tabs.Count; i++)
        {
            var tab = Plugin.Config.Tabs[i];
            if (!tab.PopOut)
                continue;

            if (PopOutWindows.Contains(tab.Identifier))
                continue;

            var window = new Popout(Plugin, tab, i);

            Plugin.WindowSystem.AddWindow(window);
            PopOutWindows.Add(tab.Identifier);
            PopOutById[tab.Identifier] = window;
        }
    }

    /// <summary>
    /// Settings replaces every tab object. Point each open pop-out at the replacement
    /// that kept its identifier, and dock one whose PopOut flag is now clear.
    /// </summary>
    public void RebindPopOuts()
    {
        foreach (var (id, window) in PopOutById.ToArray())
        {
            var idx = Plugin.Config.Tabs.FindIndex(tab => tab.Identifier == id);
            if (idx >= 0 && Plugin.Config.Tabs[idx].PopOut)
            {
                window.Retarget(Plugin.Config.Tabs[idx], idx);
                continue;
            }

            window.CloseForConfigSwap();
        }
    }

    internal void UnregisterPopOut(Popout window)
    {
        PopOutWindows.Remove(window.TabIdentifier);
        PopOutById.Remove(window.TabIdentifier);
        if (Plugin.WindowSystem.Windows.Contains(window))
            Plugin.WindowSystem.RemoveWindow(window);
    }
}
