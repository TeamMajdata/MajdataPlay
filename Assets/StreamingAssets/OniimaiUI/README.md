# Original MajdataPlay UI assets

Unmodified assets copied from the same source tree so the native Android companion UI can use the actual game's artwork and font:

- `newui@3x.png` → `Assets/Sprites/Ribbon/newui@3x.png`
- `xxlb.png` → `Assets/Sprites/List/xxlb.png`
- `Jua-Regular.ttf` → `Assets/Fonts/General/Jua-Regular.ttf`
- `AlimamaFangYuanTiVF-Thin-2.ttf` → `Assets/Fonts/General/AlimamaFangYuanTiVF-Thin-2.ttf` (Chinese)

`Tools/Export-OniimaiUI.ps1` losslessly extracts `ui-*.png` from the original Unity sprite metadata (12288×12288, bottom-left coordinates). `GameAssets.java` decodes these small assets directly, avoiding large-atlas parsing when opening the settings. These are the original slate/gold settings card, plus/minus buttons, circular navigation pads, category pill, inner ring and profile card. Existing project and asset licenses apply; keep the original sources and license files when redistributing.

The pale circle/ring/star/bowtie/cross background is rendered by `GameUi.Pattern`. Dynamic text, numbers and controls remain native interactive UI; screenshots are not used as UI backgrounds.
