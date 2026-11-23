# Hookshot Adventure Launcher 0.9.02a

This alpha updates HAL for Hookshot 3.0.2 and includes the guided build setup,
adventure profiles, save editor, cosmetics, and experimental randomizer from
0.9.01 alpha.1. Rebuild the separate Hookshot-Dev installation with this
version before launching randomized adventures; older builds lack the new
bombchu capacity hook.

## What's new

- Hookshot 3.0.2 integration, including its fix for rolling while moving.
- Randomized small and large ammo chests can show and award a blue rupee when
  the matching bomb bag, quiver, or bullet bag is missing. Large chests retain
  their size. Existing seeds receive this at launch without being regenerated.
- Bombchus hold 20 before a bomb bag, then match the bag's 20/30/40 capacity.
  The save editor follows that rule and preserves bombchus when removing a bag.
- Starting-consumable controls are easier to read. Cosmetic patch preparation
  can use an installed or bundled ASTC codec or download and verify Arm's
  supported codec automatically.
- The launcher's Information tab and the project README contain this release's
  What's New information.

## Downloads and setup

- **Linux x86-64 AppImage:** make it executable and run it. HAL updates are
  manual. The package includes the Hookshot 3.0.2 build sources and tools.
- **Windows x86-64 experimental ZIP:** extract it and run
  `HookshotAdventure.App.exe`. The launcher and integrated sources are included;
  Windows Hookshot build prerequisites must be installed separately. The
  package is cross-built and has not been tested on Windows.
- **SHA256SUMS.txt:** checksums for the downloads.

Both packages require your own supported game files. They contain no game
dump, built game, personal save, or generated game assets. When HAL detects an
older or stock build, choose **Set up randomizer build** and build a separate
Hookshot-Dev installation from your files.

## Testing and limitations

On Manjaro Linux with an NVIDIA RTX PRO 4000 Blackwell Laptop GPU, the 3.0.2
development runtime started and ran. A player confirmed a large ammo chest
kept its size and showed a blue rupee without a quiver. Isolated tests verified
bombchu limits for all bag sizes, medium and large bag grants during play, and
save/reload at 40. The launcher core and headless UI tests passed.

Ammo-chest pickups with bags, other ammo types, the no-bake variant, and actual
bombchu chest pickups after upgrades still need broader game testing. The
randomizer does not yet establish full-game beatability. Windows, Steam Deck
installation, and AMD gameplay remain unverified. Keep backups of saves.

The broader game-free suite reported 922 passes, 3 failures and 22 skips. A
lift-cache failure introduced during this work was fixed and passed on rerun;
the inherited third-party provenance mismatch and the vector-byte test's
180-second timeout remain. The vector-byte test had previously passed its
longer standalone run.

Report issues on [GitHub](https://github.com/kintera/HookshotAdventureLauncher/issues)
or in the report-issue area of [Discord](https://discord.gg/xETQwGr9W). Include
versions, hardware, steps and, where possible, an F9 gameplay trace. Do not
upload game files, saves, or extracted game resources.
