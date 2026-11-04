# Vanilla style

Chat 2 Vanilla draws its chat log in ImGui and, when **Vanilla style** is on, paints that window to match the game's chat log. The one layout difference is tabs: every Chat 2 tab is on the bottom strip, not only the game's four. Filters, the input line, autocomplete, links, emotes, and popouts still run. The original Chat 2 chrome, including the sidebar, is what you get when the option is off.

The game is not running in this repository's build environment. Everything below that depends on a live client is called out as uncertain and is on the in-game checklist.

## Design

The reskin is a separate layer under `ChatTwo/Ui/Style/`. Chat windows call it. They do not contain texture coordinates.

| Type | Role |
| --- | --- |
| `VanillaGameLook` | Reads the live UI theme, log transparency, and log font size. Builds themed texture paths. Resolves channel colour. |
| `VanillaUldCatalog` | Parses `ui/uld/ChatLog.uld` and `ui/uld/ChatLogPanel.uld` and chooses which part is the background, a tab, the input bar, the scrollbar, or the resize grip. |
| `VanillaDrawer` | Loads the chosen texture with `ITextureProvider.GetFromGame` and draws a nine-grid or a single image. |
| `VanillaText` | Draws the dark text edge before the coloured glyph, so link hit-testing still uses the ImGui item. |
| `ChatChrome` | The facade the windows call. Every method does nothing useful when vanilla style is off, except the font push, which returns an empty disposable. |

`GetFromGame` does not apply the player's UI theme. The drawer does that itself.

Theme index comes from `SystemConfigOption.ColorThemeType` (the theme dropdown). If `SystemConfigOption.UiAssetType` differs, textures follow the asset index, because that is the texture-set selector. Index 0 is `ui/uld/` (Dark). Index `n > 0` is `ui/uld/img{n:00}/` when the asset's `ThemeSupportBitmask` has bit `n - 1` set and `WidgetData.AssetsHaveThemeSupport` is true. The names used in the debugger are Dark, Light, Classic FF, Clear Blue, Clear White, Clear Green, Clear Grey, Clear Pink.

`SystemConfigOption.TextureRezoType` chooses a preference: 0 tries the standard sheet first, any other value tries `_hr1` first. Whichever file exists is the one that is drawn. ULD coordinates are standard-resolution pixels. They are doubled only when the loaded path contains `_hr1` and the doubled rectangle fits the texture.

Missing textures draw a shaped stand-in (frame, pill, rounded field, circle, scrollbar, corner grip). They do not invent UVs.

Nine-grid margins come from the ULD node (`Left`/`Top`/`Right`/`Bottom` offsets), scaled by `ImGuiHelpers.GlobalScale`, and clamped so the corners fit the destination. The center is stretched. The client can tile that center; this renderer does not.

### What the windows do

While vanilla style is on:

- The main window and popouts use `NoTitleBar` and `NoBackground`. Chat 2's opacity slider is not stacked on top. Background alpha is the game's log permeation (see below). A popout with independent opacity uses that tab's opacity instead.
- `CanMove` and `CanResize` are still honoured. The Dalamud style override is not pushed. The title-bar toggles are ignored for these windows until vanilla style is turned off.
- The background is the first thing drawn on the window draw list.
- The order inside the frame is the game's: messages, the channel name, a framed input with the speech-bubble channel button on its left, then the tab strip along the bottom. Sidebar tabs are not drawn while vanilla style is on.
- Every non-popout tab is a content-sized pill on that strip. The active tab gets the highlight dot. Unread is a dot on the pill, not an unread count in the label. When the pills do not fit, left and right arrows scroll them one at a time, and the mouse wheel does the same while the pointer is over the strip. The strip does not shrink the pills to squeeze them in.
- After the pills, four round buttons: add a tab, open settings, focus the input, hide the log. A popped-out tab uses the same bottom row with that one pill, settings, focus, and a button that sends the tab back to the main window. FontAwesome icons are not drawn on these windows. Rename, reorder, delete, and pop-out stay on the tab right-click menu. Novice Network stays in settings.
- Scrollbar colours are pushed to alpha 0 so ImGui still handles drag and wheel. The game track and thumb are painted from the scroll position. The log scrollbar has no arrow buttons. The tab-strip arrows are only for extra tabs.
- The resize grip uses the texture when the catalog finds one, drawn into the bottom-right corner. Its ImGui colours are transparent. If a texture is missing, the frame, pills, input, circles, scrollbar, and grip fall back to simple shapes so the layout is still readable.
- Chat text, the input preview, and the command-help window use the Axis game font at the game's log font size. Settings keep the user's font, because that push is only inside the chat windows.
- Channel colour is the user's Colours-tab override if one is set, otherwise `Chat.GetChannelColor` while vanilla style is on, otherwise Chat 2's default. User overrides still win. The HTTP API and the database viewer are not switched over.
- Italic text uses an Axis handle with `SkewStrength` while vanilla style is on.
- Wrapped and unwrapped chat text, and pretty timestamps, get an 8-direction 1px outline (scaled by `max(1, GlobalScale * 0.75)`) drawn before `TextUnformatted`.

Part choice follows the component type stored on the ULD node, and the node's own part index. It is logged:

- Window background: a nine-grid that has slice margins, preferring one whose texture path contains `ChatLog.uld`. The input field does not reuse this texture.
- Text-input component: the input field. The largest nine-grid in that component wins.
- Tabbed component: tab faces wider and taller than 12. One shared face is tinted (inactive 0.68, hover 0.9, active 1). Two similar faces use the larger as active. The smallest tab part, up to 16 pixels, is the active dot.
- Channel icon: a small image just left of the text-input node in `ChatLog.uld`.
- Tab-strip caps: images in the bottom 45% of the shell, short and not wide, ordered by X.
- Button component: the round button face. Scrollbar component: track, then a smaller part as the thumb.
- Small button nearest the bottom-right: resize grip.

`/chat2vanillaDebugger` prints the mapping under "Vanilla style".

## Texture paths

These paths are not hardcoded as UV tables. `VanillaUldCatalog` reads the asset paths out of the ULD, and `VanillaDrawer` turns each one into a themed file.

ULDs loaded:

- `ui/uld/ChatLog.uld`
- `ui/uld/ChatLogPanel.uld`

For an asset path `ui/uld/SomeName.tex`, theme index `n`, and the high-resolution preference, the candidates are:

1. `ui/uld/img{n:00}/SomeName.tex` or `SomeName_hr1.tex` when `n > 0` and the asset supports that theme. `img01` is Light, `img02` Classic FF, `img03` Clear Blue, then Clear White, Clear Green, Clear Grey, Clear Pink.
2. `ui/uld/SomeName.tex` or `ui/uld/SomeName_hr1.tex` (Dark, and the fallback when the themed file is absent).

Order inside each pair follows `TextureRezoType`, as described above.

The exact `SomeName` values come from the ULD. Current clients commonly reference `ui/uld/ChatLog.tex` for the log frame and tabs. Shared components often reference `ui/uld/ScrollBarA.tex` and `ui/uld/TextInputA.tex`. Treat those names as a hint. The debugger summary is the list this build actually resolved, including theme masks. If a part is `(none)`, that piece is using the flat fallback.

## Settings

**Vanilla style** is on the Chat Log tab, and it is the default for a new config. Saving settings copies it through `Configuration.UpdateFrom`. Turning it off restores Chat 2's ImGui window, font, and colours on the next draw.

Related behaviour while it is on:

- Colours set on the Colours tab still override the game channel colour.
- Log background alpha follows `UiConfigOption.LogPermeationRate`. The input bar follows `LogPermeationRateInput` when that value is present. Text stays opaque.
- If the game config cannot be read, the background falls back to Chat 2's Window opacity slider. That slider is not applied on top of a successful game read.
- Font size follows `UiConfigOption.LogFontSize`, in the same point unit as Chat 2's `FontSizeV2`. `GameFontStyle` is given `points * 4/3` pixels.
- The Dalamud style override is skipped for the chat log and popouts. The Chat Log tab says so under Override style.

Commands and window ids, so both plugins can be loaded:

| | Chat 2 | This fork |
| --- | --- | --- |
| Internal name | `ChatTwo` | `ChatTwoVanilla` |
| Main command | `/chat2` | `/chat2vanilla` |
| Clear log | `/clearlog2` | `/clearlog2vanilla` |
| History | `/chat2Viewer` | `/chat2vanillaViewer` |
| Debugger | `/chat2Debugger` | `/chat2vanillaDebugger` |
| SeString debugger | `/chat2SeString` | `/chat2vanillaSeString` |
| Main window | `###chat2` | `###chat2vanilla` |
| IPC | `ChatTwo.Register`, `ChatTwo.GetChatInputState`, … | `ChatTwoVanilla.Register`, `ChatTwoVanilla.GetChatInputState`, … |

The C# namespace stays `ChatTwo`, including `ChatTwo.Code.ChatType`. ExtraChat's IPC is inbound and was not renamed. See [ipc.md](ipc.md).

## Uncertainties

These are decisions a live client needs to confirm. The code picks one behaviour and the checklist asks you to compare it with the real log.

- **Permeation scale.** `LogPermeationRate` at 0–100 is treated as percent clear (alpha = `1 - rate/100`). Values above 100 are treated as a 0–255 byte. Node alpha 0 in the ULD is treated as unset (fully opaque) so a zero byte does not hide the window. The texture's own alpha is then multiplied by the permeation alpha, which is what the game does to the node. If the texture already contains the default translucency, the result can be darker than the real log.
- **Font size unit.** Values 8–36 are used as points. A smaller non-zero integer is an index into 10, 12, 14, 16, 18, 20, 23. Zero falls back to 12.
- **Theme source.** `ColorThemeType` is used unless `UiAssetType` disagrees. Both numbers are in the debugger summary.
- **High-resolution UVs.** Doubling is based on the file that loaded, not only on the config. If both sheets exist and the preference is wrong, the image is sharp or soft but the UVs still match the sheet that was opened. Compare UI Resolution Standard and High (4K).
- **Which part is which.** Component type picks the input, the tab face, the round button, and the scrollbar. The channel icon, the tab caps, and the corner grip still use position. The summary log is the way to see a wrong guess: it prints `partList`, `part`, and the node size. One shared tab part is tinted instead of swapped.
- **Nine-grid center** is stretched, not tiled.
- **Log scrollbar arrows** are not separate hit targets. Drag and wheel use ImGui's invisible bar. Arrows on the tab strip only scroll extra tabs.
- **Sidebar tabs** are ignored while vanilla style is on. Turn vanilla style off to get Chat 2's sidebar.
- **Text outline** is eight extra `AddText` calls per wrapped piece. Turn vanilla style off to disable it.
- **Tab click.** The message list is drawn before the strip, so a click highlights on the next frame. A keyboard tab change is applied before the messages, so that switch is the same frame.
- **Popout opacity.** Independent opacity replaces permeation for that window, including the input bar. The game only has one log.

## In-game checklist

Build with the steps in the README, install `ChatTwoVanilla.dll` next to its json, and enable the plugin. Then:

1. Toggle **Vanilla style** off and on. Off must look like Chat 2, including the Dalamud style override if you use one. On must drop the ImGui title bar and the ImGui frame.
2. Walk the UI theme list (Dark, Light, Classic FF, Clear Blue, and any later theme you have). The frame, tabs, input, and scrollbar should follow. Open `/chat2vanillaDebugger` and confirm the summary's theme index and texture list.
3. Switch UI Resolution between Standard and High (4K). Edges of the frame should stay aligned, not show a doubled or halved slice.
4. Move Log Window Transparency. The background should track the game log. Chat text should stay readable and not fade with the background. Compare a value near opaque and a value near clear.
5. Change a channel colour in the game's log settings, with no override on the Colours tab. New lines and the input text should pick it up. Set a Colours-tab override and confirm it wins. Clear the override and confirm the game colour returns.
6. Tabs sit on the bottom, as pills, with the active dot. Add more than four. The strip should scroll with the arrows and with the wheel, without shrinking the pills or showing an ImGui tab bar. Unread is a dot, not a count in the name. Right-click still renames, reorders, deletes, and pops out. A popped-out tab leaves the main strip and keeps the same bottom row. Switching tabs should keep filters and the input channel behaviour Chat 2 already has.
7. Turn on sidebar tabs while vanilla style is on. The bottom strip should stay. Turn vanilla style off and the sidebar should come back.
8. The channel name sits above the field. The field is framed. The speech bubble on its left opens the channel list. The round buttons are add tab, settings, focus input, and hide. No FontAwesome glyphs on the log itself. Type, use autocomplete, click a link, and send an emote. Links must still be clickable (the outline is not the hit target).
9. Scroll with the wheel and by dragging the thumb. The thumb should track the log. The log scrollbar has no arrow buttons.
10. Resize from the corner. The grip, if the summary lists one, should sit on the corner and not cover the hide button.
11. Change the game's log font size. Axis in this window should follow. Open settings and confirm settings are still in your Dalamud font.
12. Italic emotes or emphasis should slant in Axis while vanilla style is on.
13. Load original Chat 2 at the same time. Each plugin should have its own window, its own commands, its own config file, and its own IPC. Registering a context menu on `ChatTwo.Register` must not fire inside this fork, and `ChatTwoVanilla.Register` must not fire inside Chat 2.
