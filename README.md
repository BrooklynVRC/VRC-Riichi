# VRChat Riichi Mahjong

A networked 4-player riichi mahjong table for VRChat worlds, written in UdonSharp. Empty seats can be filled with bots.

## Features

- Full riichi rules: chi, pon, kan, riichi, ron and tsumo, dora, ura dora and red fives, furiten, and yaku and fu scoring with limit hands.
- All the standard yaku, including double riichi, chankan (robbing a kan), haitei/houtei, rinshan, and the yakuman tenhou and chiihou. Nagashi mangan and the nine-terminals redeal are on by default and can be turned off. Yakuhai are named in results (Haku, Hatsu, Chun, Seat Wind, Round Wind).
- Two-click discards: the first click raises and highlights a tile, a second click on it discards it. Clicking a different tile moves the selection.
- Waits: while you're tenpai, the tiles you can win on float above your hand with a count of how many are still unseen, and a FURITEN warning when it applies. On your turn, raising a tile previews the waits you'd have after discarding it. Only you see your own waits.
- Sound slots: tile select, tile put down (pick from several clips at random, with slight pitch variation), ron, tsumo, riichi, calls, and draws. All optional.
- In riichi, your drawn tile is discarded for you when you can't win on it (`autoDiscardInRiichi`).
- Each tile shows its number (or wind) in the corner of the picture. Turn it off with `showTileNumbers`.
- Context buttons: only the moves you can make right now appear, such as Tsumo, Riichi, Ron, Pon, Pass, or one Chi button per sequence (Chi 3-4-5m). Nothing appears when you just need to discard.
- Dora tiles and red fives glow with a slow golden pulse (`showDoraShine`, plus color and speed settings). It uses material emission, so a custom tile material needs emission for the glow to show.
- Readable table: on your screen, every other player's discards and called sets, plus the dora indicators, turn to face you and tilt up like a display stand. Your own stay flat. Each player sees the table turned toward themselves; it's visual only, so the game state isn't affected. Tilt, size and on/off settings are in the Readable discards section.
- Tiles you can claim light up, and a timed call window gives you time to decide before play moves on.
- A large announcer above the table shows the last discard and calls such as PON!, RIICHI! and RON!. Wins show the hand's value and stay up longer. When the hand ends, the winner's hand is revealed with the winning tile lit up.
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

To add sounds, drop audio clips into the Sounds section on `MahjongGame`. They play through the `Sounds` AudioSource over the table, which Build Table creates.

## Tile faces

Tile faces come from a 2048×1024 atlas assigned to `Materials/MJ_TileFace`. The atlas is 10 columns by 4 rows of 184×256 cells:

- Rows 1 to 3 are man, pin and sou. Each row holds 1–5, then the red 5, then 6–9.
- Row 4 is the honors: East, South, West, North, Red dragon, White dragon, Green dragon.

The included `Textures/MahjongTileAtlas.png` is assigned by default. To use your own art, replace it with an atlas in the same layout. If no atlas is assigned, the tiles show text labels instead.

## License

MIT, see [LICENSE](LICENSE).
