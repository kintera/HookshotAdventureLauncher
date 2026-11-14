# Hookshot Adventure Launcher v0.8

**Release status:** Source preview / testing release  
**Release date:** November 14, 2026  
**Source:** HookshotAdventureLauncher `main` (Python/PyQt6 launcher only)

## Overview

v0.8 is the first named preview release of the standalone Hookshot Adventure Launcher. It packages the launcher UI and related save-manager code; it is not the Hookshot native game engine or a ready-to-play game.

## Features

- Compact Adventure Select with exactly three quick-access profile slots.
- Separate Adventure Details page with read-only save progression, hearts, magic, wallet, inventory, equipment, quest items, masks, and songs.
- Profile management with saved-adventure organization and deletion safeguards.
- Integrated installer, Settings, launch configuration and shader/pipeline options.
- Eight selectable themes: Darunia, Gannon, Link, Royal, Ruto, Tri-force, Zelda and Ruby.
- Ruby neon-crimson visual styling and shared theme background/transparency improvements.
- Theme showcase under the README's Themes section.
- Layout intended for desktop Linux and compact Steam Deck-sized screens.

## Install and use

Python 3.10+ and PyQt6 are required. Integrate this launcher subtree with a compatible full Hookshot source checkout. See the repository README for the full setup and launch commands.

## Validation and known limitations

- Automated source-level tests cover theme registration and UI regressions; they do not replace on-device visual and gameplay testing.
- Ruby styling, the Steam Deck layout, and save-field interpretations need continued real-world verification.
- Some game-icon and save-progression mappings may need additional validation.
- Native Hookshot engine, game assets, ROMs, saves, shaders and prebuilt game binaries are not distributed with the launcher.
- This is an active-development preview; back up important save files before use.

## Release scope

The frozen release source should be the commit containing these release notes on `main`, with no version-number bump beyond **v0.8** until explicitly approved.

---

## Historical initial source snapshot

Launcher-only source extracted from the Hookshot v2.1.0 Adventure UI v0.8 Responsive Manage Save Fixes build. The native game compiler, ROM/game data, caches and binaries are deliberately omitted.

Initial commit title: `Initial commit: Hookshot Adventure Launcher`
