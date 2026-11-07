using System.Numerics;
using ChatTwo.Code;
using ChatTwo.Ui.Style;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using Dalamud.Bindings.ImGui;
using Lumina.Text.ReadOnly;

namespace ChatTwo.Ui;

public class DebuggerWindow : Window
{
    private readonly Plugin Plugin;
    private readonly ChatLog.ChatLog ChatLogWindow;

    public DebuggerWindow(Plugin plugin) : base("Debugger###chat2vanilla-debugger")
    {
        Plugin = plugin;
        ChatLogWindow = plugin.ChatLog;

        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(475, 600),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
        };

        RespectCloseHotkey = false;
        DisableWindowSounds = true;

        Plugin.Commands.Register(Plugin.DebuggerCommand, showInHelp: false).Execute += Toggle;
    }

    public void Dispose()
    {
        Plugin.Commands.Register(Plugin.DebuggerCommand, showInHelp: false).Execute -= Toggle;
    }

    private void Toggle(string _, string __) => Toggle();

    public override unsafe void Draw()
    {
        var agent = (nint) AgentItemDetail.Instance();
        ImGui.TextUnformatted($"Current Cursor Pos: {ChatLogWindow.InputHandler.CursorPos}");
        if (ImGui.Selectable($"Agent Address: {agent:X}"))
            ImGui.SetClipboardText(agent.ToString("X"));

        ImGuiHelpers.ScaledDummy(5.0f);

        ImGui.TextUnformatted($"Handle Tooltips: {ChatLogWindow.InputHandler.PayloadHandler.HandleTooltips}");
        ImGui.TextUnformatted($"Hovered Item: {ChatLogWindow.InputHandler.PayloadHandler.HoveredItem}");
        ImGui.TextUnformatted($"Hover Counter: {ChatLogWindow.InputHandler.PayloadHandler.HoverCounter}");
        ImGui.TextUnformatted($"Last Hover Counter: {ChatLogWindow.InputHandler.PayloadHandler.LastHoverCounter}");

        ImGuiHelpers.ScaledDummy(5.0f);

        ImGui.TextColored(ImGuiColors.DalamudOrange, "Current Tab");
        ImGui.TextUnformatted($"Name: {Plugin.CurrentTab.Name}");
        ImGui.TextUnformatted($"Channel: {Plugin.CurrentTab.CurrentChannel.Channel.ToChatType().Name()}");
        ImGui.TextUnformatted($"Tell Target: {Plugin.CurrentTab.CurrentChannel.TellTarget?.ToTargetString() ?? "Null"}");
        ImGui.TextUnformatted($"Use Temp? {Plugin.CurrentTab.CurrentChannel.UseTempChannel}");
        ImGui.TextUnformatted($"Temp Channel: {Plugin.CurrentTab.CurrentChannel.TempChannel.ToChatType().Name()}");
        ImGui.TextUnformatted($"Temp Tell Target: {Plugin.CurrentTab.CurrentChannel.TempTellTarget?.ToTargetString() ?? "Null"}");
        ImGui.TextUnformatted($"Name Set? {Plugin.CurrentTab.CurrentChannel.Name.Count > 0}");
        ImGui.TextUnformatted($"Name {string.Join(" ", Plugin.CurrentTab.CurrentChannel.Name.Select(c => c.StringValue()))}");

        ImGuiHelpers.ScaledDummy(5.0f);

        ImGui.TextColored(ImGuiColors.DalamudOrange, "Vanilla Chat");
        ImGui.TextUnformatted($"Channel: {new ReadOnlySeString(AgentChatLog.Instance()->ChannelLabel).ExtractText()}");

        ImGuiHelpers.ScaledDummy(5.0f);

        ImGui.TextColored(ImGuiColors.DalamudOrange, "Vanilla style");
        ChatChrome.Uld.EnsureLoaded();
        ImGui.TextWrapped(ChatChrome.Uld.Summary);

        ImGuiHelpers.ScaledDummy(5.0f);
        ImGui.TextColored(ImGuiColors.DalamudOrange, "Vanilla font");
        ImGui.TextWrapped(Plugin.FontManager.DescribeVanillaFont());
        using (Plugin.FontManager.PushVanillaAxisPreview())
            ImGui.TextUnformatted($"Sample at {ImGui.GetFontSize():0.##} px: The quick brown fox jumps over the lazy dog.");
    }
}
