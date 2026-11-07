using ChatTwo.Resources;
using ChatTwo.Ui.Style;
using ChatTwo.Util;
using Dalamud.Bindings.ImGui;

namespace ChatTwo.Ui.SettingsTabs;

public sealed class VanillaStyleTab : ISettingsTab
{
    private Configuration Mutable { get; }

    public string Name => Language.Options_VanillaStyleTab_Name + "###tabs-vanilla-style";

    public VanillaStyleTab(Plugin plugin, Configuration mutable)
    {
        _ = plugin;
        Mutable = mutable;
    }

    public void Draw(bool changed)
    {
        ImGuiUtil.HelpText(Language.Options_VanillaStyleTab_Intro);
        ImGui.Spacing();

        ImGuiUtil.OptionCheckbox(ref Mutable.VanillaStyle, Language.Options_VanillaStyle_Name, Language.Options_VanillaStyle_Description);
        ImGui.Spacing();

        ImGuiUtil.OptionCheckbox(ref Mutable.HighlightUnreadTabs, Language.Options_HighlightUnreadTabs_Name, Language.Options_HighlightUnreadTabs_Description);
        ImGui.Spacing();

        ImGuiUtil.OptionCheckbox(ref Mutable.TintInputByChannel, Language.Options_TintInputByChannel_Name, Language.Options_TintInputByChannel_Description);
        if (Mutable.TintInputByChannel)
        {
            ImGuiUtil.DragFloatVertical(Language.Options_TintInputIntensity_Name, ref Mutable.InputChannelTint, 1f, 0f, 100f, $"{Mutable.InputChannelTint:N0}%%", ImGuiSliderFlags.AlwaysClamp);
            ImGui.Spacing();
        }

        if (!Mutable.VanillaStyle)
            ImGuiUtil.HelpText(Language.Options_TintInputByChannel_VanillaOnly);
        ImGui.Spacing();

        var followedGame = Mutable.UseGameLogOpacity;
        ImGuiUtil.OptionCheckbox(ref Mutable.UseGameLogOpacity, Language.Options_LogOpacity_UseGame_Name, Language.Options_LogOpacity_UseGame_Description);
        if (followedGame && !Mutable.UseGameLogOpacity)
            Mutable.LogBackgroundOpacity = Math.Clamp(VanillaGameLook.LogBackgroundAlpha() * 100f, 0f, 100f);

        if (Mutable.UseGameLogOpacity)
        {
            var gamePercent = Math.Clamp(VanillaGameLook.LogBackgroundAlpha() * 100f, 0f, 100f);
            ImGui.BeginDisabled();
            ImGuiUtil.DragFloatVertical(Language.Options_LogOpacity_Name, ref gamePercent, 1f, 0f, 100f, $"{gamePercent:N0}%%", ImGuiSliderFlags.AlwaysClamp);
            ImGui.EndDisabled();
        }
        else
        {
            ImGuiUtil.DragFloatVertical(Language.Options_LogOpacity_Name, ref Mutable.LogBackgroundOpacity, 1f, 0f, 100f, $"{Mutable.LogBackgroundOpacity:N0}%%", ImGuiSliderFlags.AlwaysClamp);
            ImGuiUtil.HelpText(Language.Options_LogOpacity_Description);
        }

        ImGui.Spacing();
        ImGuiUtil.HelpText(Language.Options_VanillaStyle_PerTabColor);
        ImGui.Spacing();
        GameChatTabImportUi.Draw();
        ImGui.Spacing();
        ImGuiUtil.HelpText(Language.Options_VanillaStyle_FontNote);
        ImGui.Spacing();

        ImGuiUtil.OptionCheckbox(ref Mutable.CanMove, Language.Options_CanMove_Name);
        ImGui.Spacing();
        ImGuiUtil.OptionCheckbox(ref Mutable.CanResize, Language.Options_CanResize_Name);
        ImGuiUtil.HelpText(Language.Options_VanillaStyle_ResizeNote);
    }
}
