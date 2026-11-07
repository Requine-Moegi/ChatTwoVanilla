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

While vanilla style is on, `VanillaFrame` places every node the way `ChatLog.uld` does on its 324×200 widget, then keeps those distances when the window is a different size. Bottom chrome stays a fixed design distance from the bottom of the window. The log nine-grid grows to fill the space above it. Positions are design pixels times `ImGuiHelpers.GlobalScale`. Window padding is zero, so the ULD origin is the window origin. `WindowBg` is pushed transparent and the window uses `NoTitleBar` and `NoBackground`, so the stone (or whatever is behind the window) shows through the fade. Chat 2's opacity slider is not stacked on top.

At the default 324×200 size the nodes are:

| Piece | ULD | Where |
| --- | --- | --- |
| Log background | Node 14, part list 1 part 12, nine-grid 20, node alpha 127 | 0,0 and 324×148. Height becomes `windowH - 52`. It does not cover the input or the tabs. |
| Channel label | Node 4, Axis 14, left-aligned in a 21px box | x+26, `windowH - 67` |
| Text input | Component 1012, part 17, nine-grid 13 | x+23, `windowH - 53`, width `windowW - 27`, height 28. Text inset is 12px. |
| Channel button | DropDown 1008, CircleButtons part 5 | x+0, same row as the input, 28×28 |
| Tab strip | Component 1011, width 204 | x+23, `windowH - 28`, ending 21px before the plus button |
| Left cap | Part 6 | 5px left of the strip |
| Right cap | Part 15, wrap mode horizontal flip | at the right end of the strip |
| Plus, gear, close | Buttons 1001, 1004, 1003 (CircleButtons parts 8, 0, 24) | 28px, step 24, flush to the right edge. The sprout (part 11) is not drawn. |
| Resize grip | Button 1005, parts 3 (dim) and 4 (glow, hover only) | top-right of the log, `windowW - 36`, y+10. The hit target is that sprite plus 4px, in front of the message list, and hover, glow, and drag all use it. Dragging grows the window right and up, so the bottom-left stays put. ImGui's own grip is off. |
| Scrollbar | `ChatLogPanel.uld` ScrollBarB, track part 0 (slices 4,5,5,5), thumb part 1 (5,5,6,5) | x+12, y+12, 12px wide, height `background - 24`. Left side, not ImGui's bar. |

- Every non-popout tab is a pill on that strip (text width plus 18, at least 28). Pills sit flush against each other. Part 15, flipped, is one rounded cap at the right end of the whole strip, drawn at the strip's right edge. The same sprite, mirrored, is the left cap. Extra width is added to the visible pills so they meet that right cap, which stays just before the buttons. The active tab gets part 9 at the ULD offset (-6, -4), so the diamond sits at the tab's top-left. An inactive tab with unread messages, and whose unread mode is not None, draws its label in soft gold. Opening the tab clears the count. When whole tabs do not fit, the end caps gain an arrow centred on the visible part of the cap, and a click scrolls one tab. The caps are plain when nothing is hidden. The wheel does the same while the pointer is over the row. The active tab is kept in view.
- Log text stops 4px before the resize grip, and the grip sprite is drawn above the message list, so a line cannot run under the arrow.
- The three circles are add a tab, open settings, and hide the log. Clicking the field focuses the input. There is no fourth circle: the file only has three slots, and the close slot is the X rather than the sprout. A popout draws one pill across the strip, the gear, and a close button that docks the tab. FontAwesome is not drawn on these windows. Rename, reorder, delete, and pop-out stay on the tab right-click menu. Novice Network stays in settings. Sidebar tabs are not drawn while vanilla style is on.
- The message child uses `NoScrollbar`. The ScrollBarB thumb is drawn on the left only while the log overflows. Its height is the visible fraction of the log, and clicking or dragging the column moves it. The log scrollbar has no arrow buttons.
- Chat text uses Axis at the game's log font size. The channel label is Axis 14 and tab labels are Axis 12, which is what those ULD text nodes specify, not the log slider. Settings keep the user's font.
- Channel colour is the user's Colours-tab override if one is set, otherwise `Chat.GetChannelColor` while vanilla style is on, otherwise Chat 2's default. User overrides still win. The HTTP API and the database viewer are not switched over.
- Italic text uses an Axis handle with `SkewStrength` while vanilla style is on.
- Wrapped and unwrapped chat text, and pretty timestamps, get an 8-direction 1px outline (scaled by `max(1, GlobalScale * 0.75)`) drawn before `TextUnformatted`.
- Background alpha is node alpha 127/255 multiplied by log permeation. The input frame uses permeation for the input (`LogPermeationRateInput`) and does not use the 127 node alpha. Tabs and buttons stay opaque. A popout with independent opacity uses that tab's opacity in place of permeation, including the input frame.
- If a texture is missing, that one piece falls back to a simple shape. The fallback background is a soft black fade, not a hard rectangle.

Part ids are fixed in `BindKnownParts`. `/chat2vanillaDebugger` prints each one (`partList`, `part`, UV, slice).

## Texture paths

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

**Vanilla style** is on the Chat Log tab and on its own **Vanilla Style** tab, and it is the default for a new config. Saving settings copies it through `Configuration.UpdateFrom`. Turning it off restores Chat 2's ImGui window, font, and colors on the next draw. Shared options stay on their original tabs as well.

Related behaviour while it is on:

- Colors set on the Colors tab still override the game channel color.
- **Highlight tabs with new messages** is on the Vanilla Style tab and starts on. Unread inactive tab names are soft gold. Off keeps those names the normal color. The active tab stays white either way.
- **Tint input box by chat channel** is off by default. The control is on the Chat Log tab and on the Vanilla Style tab. When it is on, the dark pixels of the input texture (ChatLog part 17) are replaced with a darkened channel hue and that texture is drawn in place of the normal one, through the same nine-grid and the same rectangle. The light rim and the transparent margin are unchanged, so the frame keeps its shape. The tinted copy is cached per channel color. A high-resolution sheet doubles the cropped pixels and does not move the destination edges. The log panel is not tinted by the channel. Say and other near-white or gray channels stay the normal dark box. The tint intensity slider defaults to 45. A temporary channel, including one typed as a `/command`, updates the box on that frame.
- Each tab has **Log background color**, off by default, on the Tabs tab. When it is on, that tab's log nine-grid is multiplied by the picked color in the main window and in a pop-out. The color is copied by `Tab.Clone`.
- Log background alpha follows `UiConfigOption.LogPermeationRate` while **Use game setting** is on. The override slider is 0–100% and replaces that permeation factor. The node's alpha is still multiplied, so 100% is the texture at its designed translucency. The input bar follows `LogPermeationRateInput` when that value is present. Text stays opaque. A popped-out tab with independent opacity replaces the factor for that window.
- If the game config cannot be read, the background falls back to Chat 2's Window opacity slider. That slider is not applied on top of a successful game read.
- Font size follows `UiConfigOption.LogFontSize`, in the same point unit as Chat 2's `FontSizeV2`. `GameFontStyle` is given `points * 4/3` pixels through `IFontAtlas.NewGameFontHandle`. The handle is pushed only once `Available` is true, including inside the message child and in pop-outs. Until then the current font stays for that frame. The Fonts tab does not change vanilla text. `/chat2vanillaDebugger` prints the handle, the last push, and a sample line.
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
- **Which part is which.** Part ids are the ones named in `ChatLog.uld` and `ChatLogPanel.uld` (background 12, input 17, pill 7, marker 9, caps 6 and 15, grip 3+4, circles 8/0/24, bubble 5, scrollbar 0 and 1). The summary log prints those ids. One shared pill is used for every tab; the marker is what shows the active tab.
- **Permeation times node alpha.** The log nine-grid is drawn at `127/255 * permeation`. The opacity slider does not replace that node alpha. "Use game setting" (the default) keeps today's permeation factor. The override is a 0–100% factor in the same place: 100% is the texture at its designed translucency, matching the game at permeation 0, and 0% is clear. A popped-out tab with its own opacity still replaces the factor for that window. The input bar is unchanged.
- **Nine-grid center** is stretched, not tiled. The 20px border of part 12 already contains the fade, so the stretched center is the solid middle.
- **Right cap flip.** Node 8 wrap mode 1 is drawn by swapping U. If the client mirrors around a different origin, the cap can be off by a pixel.
- **Active marker.** The template places part 9 at (-6, -4) on a 28px tab with multiply 50. The marker is centred on whatever width the pill ended up, 4px above it, at full multiply. A wide tab's diamond should sit in the middle. The dim multiply is the template's idle state and is not used for the active or unread pill.
- **Grip hit target.** The graphic is the top-right of the log, and dragging that graphic resizes. ImGui still also resizes from the window edges. The two can disagree while the window is docked: the graphic does not resize a docked window.
- **Log scrollbar.** The main `ChatLog.uld` has no scrollbar node. The track and thumb are the parts from `ChatLogPanel.uld`, placed on the left at the panel's (12, 12) inset. Drag starts only on a click over the track. The thumb size uses the child's scroll range from the previous frame.
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
5. Change a channel color in the game's log settings, with no override on the Colors tab. New lines and the input text should pick it up. Set a Colors-tab override and confirm it wins. Clear the override and confirm the game color returns.
6. Tabs sit on the bottom, as pills, with the active dot. Add more than four. The strip should scroll with the arrows and with the wheel, without shrinking the pills or showing an ImGui tab bar. Unread is a dot, not a count in the name, and the unread name is soft gold while **Highlight tabs with new messages** is on. Turn that off and the unread name stays the normal color. Right-click still renames, reorders, deletes, and pops out. A popped-out tab leaves the main strip and keeps the same bottom row. Switching tabs should keep filters and the input channel behaviour Chat 2 already has.
7. Turn on sidebar tabs while vanilla style is on. The bottom strip should stay. Turn vanilla style off and the sidebar should come back.
8. The channel name sits on the lower edge of the dark panel. The field below it is the rounded input frame, with the speech bubble overlapping its left side. The round buttons are plus, gear, and X. No FontAwesome glyphs on the log itself. Type, use autocomplete, click a link, and send an emote. Links must still be clickable (the outline is not the hit target).
9. The scrollbar is the thin track on the left of the text. Scroll with the wheel and by dragging the thumb. There is no grey ImGui bar in the text.
10. The resize graphic is the diagonal mark at the top-right of the dark panel. Dragging it should grow the window to the right and upward.
11. Change the game's log font size. Axis in this window should follow. Open settings and confirm settings are still in your Dalamud font.
12. Italic emotes or emphasis should slant in Axis while vanilla style is on.
13. Load original Chat 2 at the same time. Each plugin should have its own window, its own commands, its own config file, and its own IPC. Registering a context menu on `ChatTwo.Register` must not fire inside this fork, and `ChatTwoVanilla.Register` must not fire inside Chat 2.
14. Pop out a tab with resizing allowed. Drag the main window's top-right grip, then the pop-out's. Each window keeps its own position and size; the other one must not jump.
15. With that pop-out still open, open settings and click Save. The tab stays popped out, in the same window, and does not return to the main strip. Unchecking **Display in separate window** and saving docks that tab.
16. **Tint input box by chat channel** starts off, and the input box is the normal dark frame. Turn it on. Party, shout, yell, and FC should recolor the dark pixels of that same frame, and the color should stay dark. The light rim, including the rounded ends, must stay the normal frame color at UI scale 100% and 150%, and with high-resolution textures on. No extra shape is drawn, and no tint pixel sits on the rim. Say, and any channel you set to white or gray on the Colors tab, should leave the box untinted. Type `/yell` or `/p` and the box should change before you send. The log background should not change. The intensity slider should strengthen and remove the tint. A popped-out tab follows its own channel. The same controls are on the Vanilla Style tab.
17. **Log background color** on each tab starts off. Turn it on for one tab, pick a color, and only that tab's log panel should show the color laid over the dark texture, in the main window and in a pop-out. The log opacity slider still fades it. Other tabs stay the normal log. Saving and reloading should keep the color.
18. **Log background opacity** starts on **Use game setting**, and the panel should still track Log Window Transparency. Uncheck it. The slider should start at the game's current opacity and then replace it: 100% matches a fully opaque game log (the texture's own translucency stays), 0% clears the background, and the text does not fade. The input box should keep the game's input transparency. A popped-out tab with its own opacity still uses that.
19. The **Vanilla Style** tab lists the vanilla toggle, the input tint, the log opacity, a note for the per-tab color, the Axis font, and the move and resize checkboxes. Those shared checkboxes are still on the Chat log tab.
20. **Axis font.** Set the game's log font size, and set a different Dalamud UI font and global font scale. Vanilla chat text, tab labels, and pop-outs should stay Axis at the game's size (the global scale still sizes it with the rest of the frame). Open `/chat2vanillaDebugger` and confirm the vanilla font readout says the Axis handle is ready and that the last log push matches it. On the first frame after a size change the readout can say the atlas is not ready; the next frame should switch.
21. **Import tabs from game chat**, on the Tabs tab and the Vanilla Style tab. It should list the four live tab names and a channel count, then wait. Append adds them. Replace removes the draft's tabs. Cancel changes nothing. Save to keep the result. A read that cannot match the log-filter checkboxes should explain that and leave the tabs alone.
