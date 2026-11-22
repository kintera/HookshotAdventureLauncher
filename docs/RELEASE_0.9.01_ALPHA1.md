# Hookshot Adventure Launcher 0.9.01 alpha.1

This update makes randomizer setup easier to find. When a selected Hookshot installation lacks the randomizer hooks, creating a randomized adventure offers **Set up randomizer build**. HAL builds a separate Hookshot-Dev installation from the player's own game files and shows its progress and build log. It checks the build tools for the HAL integration and verifies the randomizer capability after an update.

The Linux AppImage includes the integrated Hookshot 3.0.0 build sources and build tools. The Windows x64 ZIP includes integrated sources but remains experimental: the Windows compiler, Python packages and other Hookshot build prerequisites must be installed separately, and this package has not been tested on Windows. Neither download contains game files or a built game.

The randomizer is an alpha: its event coverage is partial and full-game beatability is not established. The update does not port HAL to Hookshot 3.0.2. Updating Hookshot-Dev outside HAL can remove the hooks; use HAL to rebuild it.
