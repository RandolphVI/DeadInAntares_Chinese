# Dead in Antares - Simplified Chinese Localization

Fan-made Simplified Chinese (简体中文) localization patch for [Dead in Antares](https://store.steampowered.com/app/2511800/Dead_in_Antares/).

> [中文说明请看这里 / Chinese README](README_CN.md)

## Features

- **12,900+ translated text entries** covering all UI, dialogues, quests, items, skills, and tutorials
- **In-game language switcher** — select "中文" in Options to toggle Chinese
- **CJK font support** via BepInEx plugin with dynamic SDF rendering (Microsoft YaHei from system fonts)
- **Automatic Steam language detection** — if Steam is set to Simplified Chinese, the game defaults to Chinese

## Installation

1. **Download** the latest release zip from the [Releases](../../releases) page
2. **Extract** the zip contents into your game installation folder:
   ```
   Steam/steamapps/common/Dead in Antares/
   ```
3. **Overwrite** when prompted — this adds/replaces the following files:
   - `winhttp.dll`, `doorstop_config.ini` (BepInEx loader)
   - `BepInEx/` folder (mod framework + Chinese font plugin)
   - `Dead In Antares_Data/resources.assets` (game data with Chinese translations)
4. **Launch** the game and go to **Options → Language → 中文**

## Uninstallation

In Steam: **Right-click the game → Properties → Installed Files → Verify integrity of game files**

This restores all modified files to their original state.

## Compatibility

- **Game version**: Built for the current Steam release (2026-03)
- **Platform**: Windows 10/11 (requires Microsoft YaHei font, included with all Windows 10/11 installations)
- **Game updates**: If the game updates, `resources.assets` may be overwritten. Re-download and apply the patch.

## Technical Details

This patch consists of two components:

1. **Modified `resources.assets`** — The game's localization CSV files (`Loc_DiV - Loc` and `Loc_DiV - Dialogues`) are patched to include a "中文" column with Chinese translations, appended to the end of the original asset file via binary patching.

2. **BepInEx CJK Font Plugin** — Since the game's bundled TMP fonts (SourceSansPro, Xolonium, LiberationSans) lack CJK glyphs, a BepInEx 5.x plugin creates a dynamic TMP_FontAsset from the system's Microsoft YaHei font at runtime and injects it as a fallback into all game fonts. It also removes italic styling from camp station names for better CJK readability.

Source code for the plugin is available in the [`src/`](src/) directory.

## Credits

- **Translation**: Powered by DeepSeek API, with manual review and corrections
- **Font Plugin**: BepInEx 5.x + Harmony + Unity TextMeshPro runtime font injection
- **Game**: [Dead in Antares](https://store.steampowered.com/app/2511800/) by Ishtar Games / CCCP

## License

This localization patch is provided as-is for personal use. The translation content and plugin code are released under the MIT License. Game assets remain the property of their respective owners.
