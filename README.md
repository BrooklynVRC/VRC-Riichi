# VRChat Riichi Mahjong

A networked 4-player riichi mahjong table for VRChat worlds, written in UdonSharp. Empty seats can be filled with bots.

## Features

- Full riichi rules: chi, pon, kan, riichi, ron and tsumo, dora, ura dora and red fives, furiten, and yaku and fu scoring with limit hands.
- Tiles you can claim light up, and a timed call window gives you time to decide before play moves on.
- A large announcer above the table shows the last discard and calls such as PON!, RIICHI! and RON!.
- Bots fill empty seats. If a player leaves mid-game, a bot takes over their seat.
- Material slots on the game object let you restyle the table, tiles, tile backs, tile faces and buttons.

## Requirements

- Unity 2022.3 (the VRChat-supported version)
- VRChat Worlds SDK 3.x
- UdonSharp 1.x
- TextMeshPro

## Install

1. Copy the `Mahjong` folder (and `Mahjong.meta`) into your project's `Assets` folder.
2. Run **Tools → Mahjong → Build Table**.
3. Players click **Sit** to take a seat, or **Bot** to give a seat to a bot, and then start the game.

## Tile faces

Tile faces come from a 2048×1024 atlas assigned to `Materials/MJ_TileFace`. The atlas is 10 columns by 4 rows of 184×256 cells:

- Rows 1 to 3 are man, pin and sou. Each row holds 1–5, then the red 5, then 6–9.
- Row 4 is the honors: East, South, West, North, Red dragon, White dragon, Green dragon.

The included `Textures/MahjongTileAtlas.png` is assigned by default. To use your own art, replace it with an atlas in the same layout. If no atlas is assigned, the tiles show text labels instead.

## License

MIT, see [LICENSE](LICENSE).
