# Spire Mouse

[한국어](README.ko.md) · [Steam Workshop](https://steamcommunity.com/sharedfiles/filedetails/?id=3808397695)

Right-click to play cards, adjust the game speed, and use your mouse side buttons to undo, redo, or restart a room in **Slay the Spire 2**.

![Spire Mouse controls](workshop/image.png)

## Install

Subscribe on the [Steam Workshop](https://steamcommunity.com/sharedfiles/filedetails/?id=3808397695) and enable Spire Mouse in the game's mod menu. No prerequisite mods are required. Tested on Windows with game v0.111.1 (stable) and v0.113.1 (public-beta).

## Default controls

| Input | Action |
|---|---|
| Right-click a card | Play it. If it needs a target, right-click the target next or right-drag the card onto it and release |
| Right-click a card in a selector | Select or deselect. Right-click empty space to confirm a valid selection |
| Double right-click empty space in combat | End the turn |
| Right-click empty space after claiming rewards or completing a room's choices | Continue |
| Rear thumb button (M4) | Undo |
| Front thumb button (M5) | Redo |
| Both thumb buttons together, or F5 | Restart the room |
| Esc | Cancel targeting |

Change bindings, speed, and history options in **Settings → Spire Mouse**.

## Undo a combat restart

After a combat restart, **Undo** takes you back to where you were, including an open card selection. **Redo** returns you to the restarted opening. Taking a new action after Undo clears the redo history.

Undo/Redo and room restarts are singleplayer features. Undo history is limited to the current combat and ends when combat finishes or you quit. Restarts outside combat and event choices cannot be undone.

## Report a problem

Include your game and mod versions and the steps to reproduce the problem. [GitHub Issues](https://github.com/rpr123/SpireMouse/issues)

## License and credits

Spire Mouse is MIT licensed. The speed component is adapted from OLC's STS2-QuickAnimationMode. [MIT license](LICENSE) · [Speed component license](LICENSE-SpeedControl.txt) · [Developer docs](docs/BUILD.md)
