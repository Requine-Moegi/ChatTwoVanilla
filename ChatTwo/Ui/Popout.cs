using System.Numerics;
using ChatTwo.Code;
using ChatTwo.GameFunctions.Types;
using ChatTwo.Resources;
using ChatTwo.Ui.Handler;
using ChatTwo.Ui.Style;
using ChatTwo.Util;
using Dalamud.Interface.Style;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Lumina.Extensions;

namespace ChatTwo.Ui;

public class Popout : Window, IChatWindow
{
    private readonly Plugin Plugin;
    private Tab Tab;
    private int Idx;

    private long FrameTime; // set every frame
    private long LastActivityTime = Environment.TickCount64;

    private readonly string ChatChannelPicker = "chat-popout-channel-picker";

    public readonly InputHandler InputHandler;

    public Vector2 LastWindowPos { get; set; } = Vector2.Zero;
    public Vector2 LastWindowSize { get; set; } = Vector2.Zero;
    public HideState CurrentHideState { get; set; } = HideState.None;

    internal static string WindowTitle(Tab tab) => $"{tab.Name}###chat2vanilla-popout-{tab.Identifier:N}";

    public Popout(Plugin plugin, Tab tab, int idx) : base(WindowTitle(tab))
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
        // Saving settings replaces every tab object. Follow the tab that kept this
        // identifier so this window is not closed just because its old object was dropped.
        var tabs = Plugin.Config.Tabs;
        for (var i = 0; i < tabs.Count; i++)
        {
            if (tabs[i].Identifier != Tab.Identifier)
                continue;

            if (!ReferenceEquals(tabs[i], Tab))
                Retarget(tabs[i], i);
            break;
        }

        if (!Tab.PopOut || Plugin.Config.Tabs.All(t => t.Identifier != Tab.Identifier))
            IsOpen = false;
    }

    /// <summary>
    /// Point this window at the tab object that replaced the one it was opened with.
    /// The ImGui id stays on the tab identifier, so the saved position stays with this window.
    /// </summary>
    internal void Retarget(Tab tab, int idx)
    {
        Tab = tab;
        Idx = idx;
        var title = WindowTitle(tab);
        if (WindowName != title)
            WindowName = title;
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
            Flags |= ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoResize;
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

        if (ChatChrome.Enabled)
        {
            var chromeAlpha = Tab.IndependentOpacity ? Tab.Opacity / 100f : VanillaGameLook.LogBackgroundOpacityFactor();
            var inputAlpha = Tab.IndependentOpacity ? chromeAlpha : VanillaGameLook.LogInputAlpha();
            DrawVanillaPopout(chromeAlpha, inputAlpha);
            return;
        }

        if (!Plugin.Config.ShowPopOutTitleBar)
        {
            ImGui.TextUnformatted(Tab.Name);
            ImGui.Separator();
        }

        var remainingHeight = Tab.SupportsInput
            ? Plugin.ChatLog.GetRemainingHeightForMessageLog(false)
            : ImGui.GetContentRegionAvail().Y;

        Plugin.ChatLog.DrawMessageLog(Tab, InputHandler.PayloadHandler, remainingHeight, false);

        if (!Tab.SupportsInput)
            return;

        // This tab has a fixed channel, so we force this channel to be always set as current
        if (Tab.Channel is not null)
            Tab.CurrentChannel.SetChannel(Tab.Channel.Value);

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
    }

    private void DrawVanillaPopout(float chromeAlpha, float inputAlpha)
    {
        var frame = ChatChrome.Frame();
        ChatChrome.BeginScrollDrag(frame.Scroll);
        ChatChrome.DrawLogBackground(frame, chromeAlpha, VanillaGameLook.TabLogBackgroundTint(Tab));

        var content = ChatChrome.LogContentRect(frame);
        var messages = MessageListRect(frame, content);
        ImGui.SetCursorScreenPos(messages.Min);
        Plugin.ChatLog.DrawMessageLog(Tab, InputHandler.PayloadHandler, messages.H, false, messages.W, content);
        ChatChrome.PaintScrollbar(frame.Scroll);

        if (Tab.SupportsInput)
        {
            if (Tab.Channel is not null)
                Tab.CurrentChannel.SetChannel(Tab.Channel.Value);

            var tellSpecial = false;
            Plugin.ChatLog.DrawVanillaChannelAndInput(
                Tab,
                ChatChannelPicker,
                false,
                GetValidPopupChannels(),
                channel => Tab.CurrentChannel.SetChannel(channel),
                InputHandler,
                frame,
                ref tellSpecial,
                inputAlpha);
        }

        DrawVanillaPopoutStrip(frame);

        if (Tab.CanResize)
            ChatChrome.DrawGrip(frame);
    }

    /// <summary>
    /// The message child is drawn after the tab and input bars. Its top and bottom stay
    /// inside the log texture's opaque center, not the nine-grid's transparent border.
    /// </summary>
    private VanillaRect MessageListRect(VanillaFrame frame, VanillaRect content)
    {
        var messages = frame.Messages;
        var top = Math.Max(messages.Y, content.Y);
        var bottom = Math.Min(messages.Y + messages.H, content.Y + content.H);
        if (!Tab.SupportsInput)
            bottom = content.Y + content.H;

        var chromeTop = frame.TabStrip.Y;
        if (Tab.SupportsInput)
            chromeTop = Math.Min(chromeTop, frame.Input.Y);
        bottom = Math.Min(bottom, chromeTop);

        return new VanillaRect(messages.X, top, messages.W, Math.Max(frame.Scale * 20f, bottom - top));
    }

    private void DrawVanillaPopoutStrip(VanillaFrame frame)
    {
        var right = frame.Gear.X - 21f * frame.Scale;
        var strip = new VanillaRect(frame.TabStrip.X, frame.TabStrip.Y, Math.Max(36f * frame.Scale, right - frame.TabStrip.X), frame.TabStrip.H);
        var tab = strip;
        ChatChrome.DrawTabFace(tab, false);
        ChatChrome.DrawTabCaps(tab);
        using (Plugin.FontManager.PushFixedAxis(12f))
        {
            var textSize = ImGui.CalcTextSize(Tab.Name);
            var textPos = new Vector2(
                MathF.Round(tab.X + (tab.W - textSize.X) * 0.5f),
                MathF.Round(tab.Y + (tab.H - textSize.Y) * 0.5f));
            VanillaText.DrawAt(textPos, Tab.Name, Vector4.One);
        }

        ChatChrome.DrawTabMarker(tab);

        if (ChatChrome.DrawCircleIcon("###popout-settings", ChatChrome.Uld.ButtonGear, frame.Gear, ChatChrome.VanillaGlyph.Gear))
            Plugin.SettingsWindow.Toggle();
        if (ImGui.IsItemHovered())
            ImGuiUtil.Tooltip(string.Format(Language.Settings_Title, Plugin.PluginName));

        if (ChatChrome.DrawCircleIcon("###popout-close", ChatChrome.Uld.ButtonClose, frame.Close, ChatChrome.VanillaGlyph.Close))
            OnClose();
        if (ImGui.IsItemHovered())
            ImGuiUtil.Tooltip(Language.ChatLog_Tabs_Dock);
    }

    public override void PostDraw()
    {
        Plugin.ChatLog.PopOutDocked[Idx] = ImGui.IsWindowDocked();

        if (Plugin.Config.VanillaStyle)
            ChatChrome.PopWindowStyle();
        else if (Plugin.Config is { OverrideStyle: true, ChosenStyle: not null })
            StyleModel.GetConfiguredStyles()?.FirstOrDefault(style => style.Name == Plugin.Config.ChosenStyle)?.Pop();
    }

    internal Guid TabIdentifier => Tab.Identifier;

    public override void OnClose()
    {
        IsOpen = false;
        Plugin.ChatLog.UnregisterPopOut(this);

        Tab.PopOut = false;
        Plugin.SaveConfig();
    }

    /// <summary>
    /// Drop the window after a settings save already stored the new PopOut flag.
    /// Does not write that flag again, so a tab that is still popped out cannot be docked by the close.
    /// </summary>
    internal void CloseForConfigSwap()
    {
        IsOpen = false;
        Plugin.ChatLog.UnregisterPopOut(this);
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
