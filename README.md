# VRChat Riichi Mahjong

A networked 4-player riichi mahjong table for VRChat worlds, written in UdonSharp. Empty seats can be filled with bots.

## Features

- Full riichi rules: chi, pon, kan, riichi, ron and tsumo, dora, ura dora and red fives, furiten, and yaku and fu scoring with limit hands.
- Each tile shows its number (or wind) in the corner of the picture. Turn it off with `showTileNumbers`.
- Tiles you can claim light up, and a timed call window gives you time to decide before play moves on.
- A large announcer above the table shows the last discard and calls such as PON!, RIICHI! and RON!.
- **Bots:** they discard by shanten and tile acceptance, keep dora, red fives and honor pairs, and declare riichi on good waits while avoiding furiten. They only call when it speeds up a hand that keeps a yaku (yakuhai, tanyao or honitsu). Against riichi they push when close to ready and fold otherwise, using safe tiles, suji and visible honors. If a player leaves mid-game, a bot takes over their seat.
- The next hand starts on its own after a short countdown, so bot games keep flowing. You can change the countdown or turn it off.
- Material slots on the game object let you restyle the table, tiles, tile backs, tile faces and buttons.

## Requirements

- Unity 2022.3 (the VRChat-supported version)
- VRChat Worlds SDK 3.8 or newer (it uses networked events with parameters)
- UdonSharp 1.x
- TextMeshPro

## Install

1. Copy the `Mahjong` folder (and `Mahjong.meta`) into your project's `Assets` folder.
2. Run **Tools → Mahjong → Build Table**.
3. Optional: select the table and run **Tools → Mahjong → Save Selected Table As Prefab**.
4. In game, click **Sit** to take a seat or **Bot** to add a bot, then press **Start**.

To restyle the table, drop materials into the slots on `MahjongGame` and run **Tools → Mahjong → Apply Materials**.

## Tile faces

Tile faces come from a 2048×1024 atlas assigned to `Materials/MJ_TileFace`. The atlas is 10 columns by 4 rows of 184×256 cells:

- Rows 1 to 3 are man, pin and sou. Each row holds 1–5, then the red 5, then 6–9.
- Row 4 is the honors: East, South, West, North, Red dragon, White dragon, Green dragon.

The included `Textures/MahjongTileAtlas.png` is assigned by default. To use your own art, replace it with an atlas in the same layout. If no atlas is assigned, the tiles show text labels instead.

## License

MIT, see [LICENSE](LICENSE).
