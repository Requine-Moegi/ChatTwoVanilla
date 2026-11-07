using System.Numerics;
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
    private const string WindowId = "Import tabs from game chat###import-game-tabs";

    private static GameTabImport? Pending;
    private static string? Error;
    private static string? Applied;

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
    /// A normal window, not a modal. BeginPopupModal sets ImGuiWindowFlags.Modal, and the docking
    /// ImGui then fills the viewport with ModalWindowDimBg from RenderDimmedBackgrounds. This window
    /// never takes that flag. Alpha is forced to 1 because the settings window may already have
    /// pushed a fade alpha for this Draw call.
    /// </summary>
    public static void DrawModal(Configuration mutable)
    {
        var pending = Pending;
        if (pending == null)
            return;

        var center = ImGui.GetWindowPos() + ImGui.GetWindowSize() * 0.5f;
        ImGui.SetNextWindowPos(center, ImGuiCond.Always, new Vector2(0.5f, 0.5f));
        ImGui.SetNextWindowFocus();
        ImGui.SetNextWindowBgAlpha(1f);

        var style = ImGui.GetStyle();
        var windowBg = Opaque(style.Colors[(int)ImGuiCol.WindowBg]);
        var titleBg = Opaque(style.Colors[(int)ImGuiCol.TitleBgActive]);
        using var colors = ImRaii.PushColor(ImGuiCol.WindowBg, windowBg);
        colors.Push(ImGuiCol.TitleBg, titleBg);
        colors.Push(ImGuiCol.TitleBgActive, titleBg);
        using var alpha = ImRaii.PushStyle(ImGuiStyleVar.Alpha, 1f);

        const ImGuiWindowFlags flags = ImGuiWindowFlags.NoCollapse
            | ImGuiWindowFlags.AlwaysAutoResize
            | ImGuiWindowFlags.NoSavedSettings
            | ImGuiWindowFlags.NoDocking;

        var open = true;
        if (!ImGui.Begin(WindowId, ref open, flags))
        {
            ImGui.End();
            if (!open)
                Pending = null;
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
        }

        ImGui.SameLine();
        if (ImGui.Button(Language.Options_Tabs_ImportGame_Replace))
        {
            mutable.Tabs.Clear();
            foreach (var tab in pending.Tabs)
                mutable.Tabs.Add(GameChatTabImporter.ToPluginTab(tab));
            Applied = Language.Options_Tabs_ImportGame_Replaced;
            Pending = null;
        }

        ImGui.SameLine();
        if (ImGui.Button(Language.Options_Tabs_ImportGame_Cancel))
            Pending = null;

        ImGui.End();
        if (!open)
            Pending = null;
    }

    private static Vector4 Opaque(Vector4 color) => new(color.X, color.Y, color.Z, 1f);

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
    }
}
