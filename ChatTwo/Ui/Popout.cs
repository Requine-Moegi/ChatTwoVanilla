using System.Numerics;
using ChatTwo.Code;
using ChatTwo.GameFunctions.Types;
using ChatTwo.Resources;
using ChatTwo.Ui.Handler;
using ChatTwo.Ui.Style;
using ChatTwo.Util;
using Dalamud.Interface.Style;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Lumina.Extensions;

namespace ChatTwo.Ui;

public class Popout : Window, IChatWindow
{
    private readonly Plugin Plugin;
    private readonly Tab Tab;
    private readonly int Idx;

    private long FrameTime; // set every frame
    private long LastActivityTime = Environment.TickCount64;

    private readonly string ChatChannelPicker = "chat-popout-channel-picker";

    public readonly InputHandler InputHandler;

    public Vector2 LastWindowPos { get; set; } = Vector2.Zero;
    public Vector2 LastWindowSize { get; set; } = Vector2.Zero;
    public HideState CurrentHideState { get; set; } = HideState.None;

    public Popout(Plugin plugin, Tab tab, int idx) : base($"{tab.Name}##chat2vanilla-popout")
    {
        Plugin = plugin;
        Tab = tab;
        Idx = idx;

        InputHandler = new InputHandler(this, plugin, $"ChatLog{idx}-{tab.Name}");

        Size = new Vector2(350, 350);
        SizeCondition = ImGuiCond.FirstUseEver;

        IsOpen = true;
        RespectCloseHotkey = false;
        DisableWindowSounds = true;

        ChatChannelPicker += $"-{idx}-{tab.Name}";
    }

    public override void PreOpenCheck()
    {
        if (!Tab.PopOut)
            IsOpen = false;
    }

    public override bool DrawConditions()
    {
        FrameTime = Environment.TickCount64;

        var isHidden = Tab.IndependentHide
            ? HideStateHelper.HideStateCheck(this, Tab.HideInBattle, Tab.HideDuringCutscenes, Tab.HideWhenNotLoggedIn, false)
            : Plugin.ChatLog.IsHidden;

        if (isHidden)
            return false;

        if (!Plugin.Config.HideWhenInactive || (!Plugin.Config.InactivityHideActiveDuringBattle && Plugin.InBattle) || !Tab.UnhideOnActivity)
        {
            LastActivityTime = FrameTime;
            return true;
        }

        // Activity in the tab, this popout window, or the main chat log window.
        var lastActivityTime = Math.Max(Tab.LastActivity, LastActivityTime);
        lastActivityTime = Math.Max(lastActivityTime, InputHandler.LastActivityTime);
        return FrameTime - lastActivityTime <= 1000 * Plugin.Config.InactivityHideTimeout;
    }

    public override void PreDraw()
    {
        Flags = ImGuiWindowFlags.None;
        if (!Tab.CanMove)
            Flags |= ImGuiWindowFlags.NoMove;

        if (!Tab.CanResize)
            Flags |= ImGuiWindowFlags.NoResize;

        if (Plugin.Config.VanillaStyle)
        {
            Flags |= ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoBackground;
            BgAlpha = 0f;
            ChatChrome.PushWindowStyle();
            return;
        }

        if (Plugin.Config is { OverrideStyle: true, ChosenStyle: not null })
            StyleModel.GetConfiguredStyles()?.FirstOrDefault(style => style.Name == Plugin.Config.ChosenStyle)?.Push();

        if (!Plugin.Config.ShowPopOutTitleBar)
            Flags |= ImGuiWindowFlags.NoTitleBar;

        if (!Plugin.ChatLog.PopOutDocked[Idx])
        {
            var alpha = Tab.IndependentOpacity ? Tab.Opacity : Plugin.Config.WindowAlpha;
            BgAlpha = alpha / 100f;
        }
    }

    public override void Draw()
    {
        using var font = ChatChrome.PushContentFont();
        using var id = ImRaii.PushId($"popout-{Tab.Identifier}");

        LastWindowSize = ImGui.GetWindowSize();
        LastWindowPos = ImGui.GetWindowPos();

        if (ImGui.IsWindowHovered(ImGuiHoveredFlags.ChildWindows))
            LastActivityTime = FrameTime;

        var chromeAlpha = Tab.IndependentOpacity ? Tab.Opacity / 100f : VanillaGameLook.LogBackgroundAlpha();
        if (ChatChrome.Enabled)
        {
            ChatChrome.DrawWindowBackground(chromeAlpha);

            var tabH = ChatChrome.TabHeight;
            var width = ImGui.GetContentRegionAvail().X;
            var pos = ImGui.GetCursorScreenPos();
            ChatChrome.DrawTab(pos, new Vector2(width, tabH), true, false);
            var textSize = ImGui.CalcTextSize(Tab.Name);
            VanillaText.DrawAt(pos + (new Vector2(width, tabH) - textSize) * 0.5f, Tab.Name, Vector4.One);
            ImGui.Dummy(new Vector2(width, tabH));
        }
        else if (!Plugin.Config.ShowPopOutTitleBar)
        {
            ImGui.TextUnformatted(Tab.Name);
            ImGui.Separator();
        }

        var remainingHeight = Tab.SupportsInput
            ? Plugin.ChatLog.GetRemainingHeightForMessageLog(false)
            : ImGui.GetContentRegionAvail().Y;

        Plugin.ChatLog.DrawMessageLog(Tab, InputHandler.PayloadHandler, remainingHeight, false);

        if (!Tab.SupportsInput)
        {
            if (ChatChrome.Enabled && Tab.CanResize)
                ChatChrome.DrawResizeGrip(ImGui.GetWindowPos(), ImGui.GetWindowSize());
            return;
        }

        // This tab has a fixed channel, so we force this channel to be always set as current
        if (Tab.Channel is not null)
            Tab.CurrentChannel.SetChannel(Tab.Channel.Value);

        if (ChatChrome.Enabled)
        {
            var inputAlpha = Tab.IndependentOpacity ? chromeAlpha : VanillaGameLook.LogInputAlpha();
            ChatChrome.DrawInputBar(
                ImGui.GetCursorScreenPos(),
                new Vector2(ImGui.GetContentRegionAvail().X, ImGui.GetFrameHeight()),
                inputAlpha);
        }

        using (ImRaii.PushStyle(ImGuiStyleVar.ItemSpacing, Vector2.Zero))
            Plugin.ChatLog.DrawChannelName(Tab);

        if (ImGuiUtil.IconButton(FontAwesomeIcon.Comment) && Tab.Channel is null)
            ImGui.OpenPopup(ChatChannelPicker);

        if (Tab.Channel is not null && ImGui.IsItemHovered())
            ImGuiUtil.Tooltip(Language.ChatLog_SwitcherDisabled);

        using (var popup = ImRaii.Popup(ChatChannelPicker))
        {
            if (popup)
            {
                foreach (var (name, channel) in GetValidPopupChannels())
                    if (ImGui.Selectable(name))
                        Tab.CurrentChannel.SetChannel(channel);
            }
        }

        ImGui.SameLine();

        var tellSpecial = false;
        InputHandler.DrawInputArea(Tab, ImGui.GetContentRegionAvail().X, ref tellSpecial);

        if (ChatChrome.Enabled && Tab.CanResize)
            ChatChrome.DrawResizeGrip(ImGui.GetWindowPos(), ImGui.GetWindowSize());
    }

    public override void PostDraw()
    {
        Plugin.ChatLog.PopOutDocked[Idx] = ImGui.IsWindowDocked();

        if (Plugin.Config.VanillaStyle)
            ChatChrome.PopWindowStyle();
        else if (Plugin.Config is { OverrideStyle: true, ChosenStyle: not null })
            StyleModel.GetConfiguredStyles()?.FirstOrDefault(style => style.Name == Plugin.Config.ChosenStyle)?.Pop();
    }

    public override void OnClose()
    {
        Plugin.ChatLog.PopOutWindows.Remove(Tab.Identifier);
        Plugin.WindowSystem.RemoveWindow(this);

        Tab.PopOut = false;
        Plugin.SaveConfig();
    }

    private Dictionary<string, InputChannel> GetValidPopupChannels()
    {
        var channels = new Dictionary<string, InputChannel>();
        foreach (var channel in Enum.GetValues<InputChannel>())
        {
            if (channel is InputChannel.Invalid or InputChannel.Tell)
                continue;

            var name = Sheets.LogFilterSheet.FirstOrNull(row => row.LogKind == (byte) channel.ToChatType())?.Name.ToString() ?? channel.ToChatType().Name();
            if (channel.IsLinkshell())
            {
                var lsName = GameFunctions.Chat.GetLinkshellName(channel.LinkshellIndex());
                if (string.IsNullOrWhiteSpace(lsName))
                    continue;

                name += $": {lsName}";
            }

            if (channel.IsCrossLinkshell())
            {
                var lsName = GameFunctions.Chat.GetCrossLinkshellName(channel.LinkshellIndex());
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
}
