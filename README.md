# Hookshot Adventure Launcher

**Source update: 0.9.01 alpha**

Hookshot Adventure Launcher manages adventures, saves, customizations and experimental randomizer settings for Hookshot. This alpha adds the C# Adventure launcher and its matching native Hookshot integration. The earlier v0.8 PyQt launcher remains in `builder/qt/`.

This is a **source update for testers**, not a ready-to-run game or a finished randomizer. The supported opening has checks, but full-game beatability is not established. Event reward and Skulltula shuffles are limited and experimental; save editing and game behavior need further play testing. Back up saves before testing.

## Set up 0.9.01

1. Obtain a fresh full Hookshot v3.0.0 source checkout at commit `29619a1cdc9d15e450761600f4e8adc0ba14e23e`, and meet Hookshot's own requirements for your legally obtained game files. This repository does not contain Hookshot or the game:

   ```sh
   git clone https://git.notrexed.dev/Hookshot/Hookshot.git Hookshot-v3.0.0
   git -C Hookshot-v3.0.0 checkout 29619a1cdc9d15e450761600f4e8adc0ba14e23e
   ```
2. From this repository on Linux, run `./install-into-hookshot.sh /path/to/Hookshot-v3.0.0`. It checks the patch and refuses to overwrite an existing Adventure installation. It modifies the checkout, so keep a separate checkout for testing.
3. Install the .NET 10 SDK, then run `./hookshot-dev.sh` from that Hookshot checkout. Use the launcher setup to select an existing Hookshot installation or your game files and create the separate development build. `./hookshot-dev.sh --builtin` opens Hookshot's original builder.

The native integration patch is tied to the revision above; it is not interchangeable with an arbitrary Hookshot build. A development game must be rebuilt with the matching native hooks before randomized profiles can be used. Windows and Steam Deck package installation have not been validated for this update.

See [Adventure documentation](builder/Adventure/README.md) for the save editor, build setup, and commands, and [testing handoff](builder/Adventure/docs/TESTING_HANDOFF.md) for outstanding gameplay verification. The legacy launcher remains available under `builder/qt/`.

## Tests

After integration, `./hookshot-dev.sh --test` runs the C# core smoke tests. See the Adventure documentation for other tests. Game-free tests do not establish game compatibility.

## Game data and license

No game files, generated game binaries, saves, extracted textures or caches are distributed here. The launcher scans locations and generates icons from the player's local game files; those files remain on the PC. GPL-3.0; see [LICENSE](LICENSE) and third-party notices in the relevant source projects.
