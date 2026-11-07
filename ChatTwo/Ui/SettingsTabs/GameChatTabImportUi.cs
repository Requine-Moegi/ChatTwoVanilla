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

    public static void Draw(Configuration mutable)
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

        if (pending.Notes.Count > 0)
        {
            ImGui.Spacing();
            foreach (var note in pending.Notes)
                ImGui.TextWrapped(note);
        }

        ImGui.Spacing();
        ImGuiUtil.HelpText(pending.How);

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
        ImGui.OpenPopup(PopupId);
    }
}
