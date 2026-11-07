using ChatTwo.Game;
using ChatTwo.Resources;
using ChatTwo.Util;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility.Raii;

namespace ChatTwo.Ui.SettingsTabs;

/// <summary>
/// Confirmation for importing the game's four chat tabs. Nothing is written until Append or Replace.
/// </summary>
internal static class GameChatTabImportUi
{
    private const string PopupId = "import-game-tabs";

    private static GameTabImport? Pending;
    private static string? Error;
    private static string? Applied;
    private static bool OpenRequested;

    public static void Draw()
    {
        if (ImGui.Button(Language.Options_Tabs_ImportGame_Name))
            BeginRead();

        ImGuiUtil.HelpText(Language.Options_Tabs_ImportGame_Description);

        if (!string.IsNullOrEmpty(Error))
        {
            using var color = ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.DalamudRed);
            ImGui.TextWrapped(Error);
        }

        if (!string.IsNullOrEmpty(Applied))
            ImGui.TextWrapped(Applied);
    }

    /// <summary>
    /// Drawn from the settings window itself, after the scrolling child closes, so the modal
    /// is not parented to that child and does not pick up its style alpha.
    /// </summary>
    public static void DrawModal(Configuration mutable)
    {
        if (OpenRequested)
        {
            ImGui.OpenPopup(PopupId);
            OpenRequested = false;
        }

        if (!ImGui.IsPopupOpen(PopupId))
            return;

        var popupBg = ImGui.GetStyle().Colors[(int)ImGuiCol.PopupBg];
        popupBg.W = 1f;
        using var popupColor = ImRaii.PushColor(ImGuiCol.PopupBg, popupBg);
        using var alpha = ImRaii.PushStyle(ImGuiStyleVar.Alpha, 1f);
        ImGui.SetNextWindowBgAlpha(1f);

        var open = true;
        if (!ImGui.BeginPopupModal(PopupId, ref open, ImGuiWindowFlags.AlwaysAutoResize))
        {
            if (!open)
                Pending = null;
            return;
        }

        var pending = Pending;
        if (pending == null)
        {
            ImGui.CloseCurrentPopup();
            ImGui.EndPopup();
            return;
        }

        ImGui.TextWrapped(Language.Options_Tabs_ImportGame_Prompt);
        ImGui.Spacing();
        foreach (var tab in pending.Tabs)
            ImGui.BulletText($"{tab.Name} — {tab.Channels.Count} channels");

        ImGui.Spacing();
        ImGui.TextWrapped(Language.Options_Tabs_ImportGame_Actions);
        ImGui.Spacing();

        if (ImGui.Button(Language.Options_Tabs_ImportGame_Append))
        {
            foreach (var tab in pending.Tabs)
                mutable.Tabs.Add(GameChatTabImporter.ToPluginTab(tab));
            Applied = Language.Options_Tabs_ImportGame_Appended;
            Pending = null;
            ImGui.CloseCurrentPopup();
        }

        ImGui.SameLine();
        if (ImGui.Button(Language.Options_Tabs_ImportGame_Replace))
        {
            mutable.Tabs.Clear();
            foreach (var tab in pending.Tabs)
                mutable.Tabs.Add(GameChatTabImporter.ToPluginTab(tab));
            Applied = Language.Options_Tabs_ImportGame_Replaced;
            Pending = null;
            ImGui.CloseCurrentPopup();
        }

        ImGui.SameLine();
        if (ImGui.Button(Language.Options_Tabs_ImportGame_Cancel))
        {
            Pending = null;
            ImGui.CloseCurrentPopup();
        }

        ImGui.EndPopup();
    }

    private static void BeginRead()
    {
        Applied = null;
        Error = null;
        Pending = null;
        if (!GameChatTabImporter.TryRead(out var import, out var error) || import == null)
        {
            Error = error ?? Language.Options_Tabs_ImportGame_Failed;
            return;
        }

        Pending = import;
        OpenRequested = true;
    }
}
