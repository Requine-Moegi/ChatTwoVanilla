# Chat 2 Vanilla

Fork of [Chat 2](https://github.com/Infiziert90/ChatTwo) by Anna (ascclemens) and Infi (Infiziert90), licensed under the [EUPL-1.2](LICENCE). This copy was modified on 2026-11-03. See [NOTICE](NOTICE).

Chat 2 Vanilla keeps Chat 2's tabs, filters, input, autocomplete, links, and emotes. A **Vanilla style** option draws the main chat window with the game's chat-log textures, the Axis font, the game's channel colours, and the game's log transparency. Turn the option off to get the original Chat 2 look. Drawing stays in ImGui (`Dalamud.Bindings.ImGui` and `ImRaii`). It does not use KamiToolKit or a native addon.

The assembly name is `ChatTwoVanilla`, so it can be installed next to Chat 2. Commands are `/chat2vanilla` and `/clearlog2vanilla`. IPC channels are `ChatTwoVanilla.*`. Details of the reskin are in [VANILLA_STYLE.md](VANILLA_STYLE.md).

Source: <https://github.com/Dillard-Coffey/ChatTwoVanilla>

## Install

Chat 2 Vanilla is distributed as a Dalamud custom plugin repository.

1. In game, run `/xlsettings`.
2. Open **Experimental**.
3. Under custom plugin repositories, add:

   `https://raw.githubusercontent.com/Dillard-Coffey/ChatTwoVanilla/main/repo.json`

4. Open the plugin installer and install **Chat 2 Vanilla**.

It uses its own config file and can be installed beside the original Chat 2. After you install it, `/chat2vanilla` opens the chat window.

## Build

The project targets `net10.0-windows` through `Dalamud.NET.Sdk` 15.0.0.

1. Install the .NET 10 SDK.
2. Put a Dalamud API 15 dev build where the SDK looks for it: `~/.xlcore/dalamud/Hooks/dev` on Linux, or `%AppData%\XIVLauncher\addon\Hooks\dev` on Windows. `DALAMUD_HOME` overrides that directory. The dev folder must contain `Dalamud.dll` and the other assemblies from a Dalamud 15 distribution (`latest.zip` from [dalamud-distrib](https://github.com/goatcorp/dalamud-distrib)).
3. From the repository root:

```bash
dotnet build ChatTwo/ChatTwo.csproj -c Release
```

The packager writes `ChatTwo/bin/Release/ChatTwoVanilla.dll` and `ChatTwoVanilla.json`. Install that output the way you install any locally built Dalamud plugin. The internal name in the manifest is `ChatTwoVanilla`, and the config file is separate from Chat 2's.

Pushing a tag named `v*` (for example `v1.42.1`) runs the release workflow. It builds this project, attaches `latest.zip` to the GitHub release, and commits that assembly version and a new `LastUpdate` in `repo.json` to `main`. The published zip is the Windows build: the plugin dll, manifest, dependencies, and the `win-x64` native runtime.

This repository was built on a machine that cannot launch FFXIV. A successful `dotnet build` does not prove the window matches the game. Use the checklist in [VANILLA_STYLE.md](VANILLA_STYLE.md) in-game.

## Credits

Chat 2 is by Anna (ascclemens) and Infi (Infiziert90), with the translators listed in the plugin's About tab. This fork keeps that authorship and the EUPL-1.2 licence.
