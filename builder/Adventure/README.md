# Hookshot Adventure development launcher

This Avalonia project adapts the earlier Adventure profile UI to Hookshot. Hookshot Builder remains responsible for installing and updating the game. The app starts the installed binary with the selected adventure's `--user` directory.

The randomizer port includes a reviewed chest catalog, deterministic item-pool planner and opening logic checks, profile-bound seed files, runtime overlays, starting maps and compasses, Child Stealth starting state, and experimental conversation and reward event edits. Native gameplay hooks are compiled only with `HOOKSHOT_DEV_RANDOMIZER=1`; generated game output carries a capability marker, and the launcher rejects randomized profile creation or launch when that marker is absent.

Randomized adventures hold 20 bombchus before a bomb bag is found, then follow the bag's 20, 30, or 40 capacity. An isolated chest, item use, bag upgrades through inventory events, and save/reload were tested; chest pickups after upgrades still need verification.

The Avalonia app owns randomizer settings, profile creation, seed generation and validation, BYML resource editing, runtime overlay composition, cosmetics and icon preparation in C#. Seed generation is bound to the profile and source resource hashes; existing seeds are validated rather than regenerated at launch. Vanilla profile launches clear all randomizer environment variables. Installing or updating the game still uses Hookshot's shared Python build pipeline.

Adventure launch paths are implemented in C#. Hookshot continues to use its own Python build pipeline.

Seed input normalization runs in launcher C#: hexadecimal input is padded to 16 digits, and text input is deterministically hashed to a 16-digit seed using the existing convention. The normalized seed is stored with the profile. This is not an in-game bridge.

New seeds use the experimental full-game chest scope. Logic checks enforce one use per available item and validate the reviewed child opening; they do not prove full-game beatability. Existing seeds retain their recorded scope. Story skip coverage and broader location/item support remain incomplete. See the [testing handoff](docs/TESTING_HANDOFF.md) for scenarios and current limitations.

**Full game — experimental supported chests** scans loose child and adult maps in the selected game when creating a seed. It randomizes supported chest rewards; unreviewed checks and packed resources stay vanilla. The target, Saria and Skulltula House event exchanges are separate experimental options. Starting maps/compasses and selected heart-piece chest rewards are supported, but full-game reachability and beatability are not established.

The Avalonia launcher selects Hookshot-Dev by default; the stable install can be selected explicitly. External saves can be launched directly. The selected save slot can be edited from the adventure details screen; see below. Development builds stay separate from the stable installation. Supported intro and Deku Tree skips save independently from the seed and are composed into runtime overlays at launch.

### Save editor

Choose an adventure and slot, then select **Edit Save**. Click hearts to set the number of heart containers, or supported item icons to add or remove them. Adding a slingshot also adds a basic seed bag if needed; adding a bow also adds a basic quiver. Existing larger bags are preserved, and removing the weapon leaves its bag in place. Click a bottle, bag, ocarina, hookshot, strength or scale icon to show its choices as a horizontal row of icons. The age selector and magic meter are also editable. Controls are available only when the save contains the fields they need. Equipped swords and shields must be unequipped in the game before removal. Some interactions still need gameplay verification; see the testing handoff.

Changes are held in memory until **Save**. Close Hookshot first. The editor refuses to save if any opened save file changed on disk, and backs up replaced files under the selected game folder's `save-backups/`. **Cancel** discards pending changes. The backend in `nx2/saveedit/` is adapted from an MIT-licensed save editor; its license is retained there. [OOT-SW2SE](https://github.com/Traiver/OOT-SW2SE) is another reference for this save format. Use copies for experimental changes and keep saves and backups on your PC.

From the repository root, start the development launcher with the .NET 10 SDK:

```sh
./hookshot-dev.sh
```

Run the launcher core smoke tests from the same directory:

```sh
./hookshot-dev.sh --test
```

The root command delegates to `HAL_dev_launcher.sh`; both entry points work from any working directory. These commands build the launcher or its tests, not the native game executable. Select your Hookshot-Dev game directory for development gameplay.

From `builder/Adventure`, the existing core test command also works:

```sh
bash scripts/test-core.sh
```

To scan a locally owned ROMFS and write randomizer research reports, use the C# command:

```sh
dotnet run --project builder/Adventure/src/HookshotAdventure.Cli -- scan-chests --romfs /path/to/romfs --out /path/to/private/reports --scope childhood --seed 1234
```

The reports are derived from local game resources; keep them on the PC. Add `--heart-pieces` or `--starting-maps` when examining those supported options.

To run the Avalonia app from this source tree:

```sh
dotnet run --project src/HookshotAdventure.App/HookshotAdventure.App.csproj
```

No game data, extracted resources, builds, or saves are included in this folder. Location listings are scanned from the selected game on the player's PC. Game execution writes only to the selected profile's user directory.

### Game installation and updates

When HAL finds no installed Hookshot game, its first-run setup offers **Download Hookshot Builder**. Choose where to save the official Linux AppImage or Windows Builder; downloads are cancellable and never replace an existing file. Open the downloaded Builder and install using your own local game files, then return to HAL and select that installation as the source for Hookshot-Dev. The official Builder is separate from HAL's development build and does not supply its randomizer hooks.

Adventure manages installation, review, updates and reinstalls directly. On a fresh setup it opens a wizard with three separate screens: **01 Introduction**, **02 Locations and build setup**, and **03 Installation**. Press **Next** on the introduction, then choose your local `.nup` or extracted game folder and configure the install directory, language, build jobs, shader workers and retained objects. **Start installation** opens the final screen with progress and the live log already expanded. **Back** preserves your choices; navigation is disabled while a build runs. The locations screen fits at 980×660 without scrolling; smaller windows remain scrollable.

**Tools → Dev Build Setup → Install / update Hookshot-Dev** opens the same screen with the current development settings. Choose any separate development directory, including a custom path such as `~/Games/Remakes/hookshot-dev`. The path is remembered for later builds and when switching back to Hookshot-Dev. The stable installation and overlapping directories remain protected. The completed build is installed into the selected development directory. Later updates reuse retained game files when available.

For a first development install, the wizard suggests an existing Hookshot installation when it finds retained game files, including a custom Linux installation recorded by Hookshot Builder. You can also select **Existing Hookshot installation** and browse to its folder. The build copies those game files independently into a separate development installation; the original installation and saves stay in place. This needs space for a second copy of the game. If the existing installation does not have its retained files, select your original NUP or extracted game folder instead.

**Settings → Reinstall** opens the stock Hookshot Builder. Profiles, randomizer settings and development builds remain managed here.

On Linux, starting a stock or development build first checks for prepared build tools. If missing, Adventure opens **Prepare Linux build tools** before running the game builder. Confirm **Prepare build tools**, wait for setup to finish, then close the setup window to continue the build. Cancelling leaves the game untouched. Both installers use the prepared Python environment, compiler/linker, FFmpeg, SDL2 and Vulkan headers; you do not need to install the build's Python packages into system Python. The launcher can also recognize Hookshot's bundled AppImage tools when launched from that environment.

For an experimental Linux image, publish Adventure self-contained for `linux-x64`, prepare a directory containing the .NET runtime and launcher dependencies' license and third-party notices, then pass their paths as `HOOKSHOT_ADVENTURE_PUBLISH` and `HOOKSHOT_ADVENTURE_NOTICES` to `scripts/linux/appimage.sh` or the Ubuntu container wrapper `scripts/linux/docker.sh`. Provide an `astcenc-sse2` executable built for the Ubuntu 22.04 release baseline as `HOOKSHOT_ADVENTURE_ASTCENC` and its license file as `HOOKSHOT_ADVENTURE_ASTC_LICENSE`; the image includes them for texture editing and icon extraction. Set `HOOKSHOT_REPOSITORY` to the fork's `owner/repository` on git.notrexed.dev: packaging requires it so both the image update metadata and the built-in updater use the fork instead of replacing its tools with upstream Hookshot. The container wrapper mounts those inputs read-only and forwards the release settings. An image with Adventure starts it by default; `--builtin` opens the original Hookshot Builder. The existing `--play`, `--cli`, update and launch modes remain available. The resulting alpha image still needs package and hardware validation before distribution.

Build the codec on that baseline with its C++ runtime linked in (for CMake, pass `-DCMAKE_EXE_LINKER_FLAGS="-static-libstdc++ -static-libgcc"`). The container's compiler PPA can otherwise give the executable a newer `libstdc++` dependency than a stock Ubuntu 22.04 system provides.

The first setup downloads a compiler archive of about 1.9 GB and can take tens of minutes on a slower connection, followed by several minutes of extraction. The setup log opens automatically and shows the compiler download alongside the other tools being prepared. A message says when setup is waiting for the compiler to finish.

Installation and launch require the built Vulkan renderer beside the game executable (`hookshot_vk.so` on Linux, `hookshot_vk.dll` on Windows). Before launching, Adventure also runs `vulkaninfo --summary` when that tool is available, with a 15-second timeout, and reports driver initialization failures. The system GPU driver is installed through your operating system; preparing Hookshot's build tools does not install it. After a driver update, restart the PC if the loaded kernel driver and installed libraries have different versions.

### Local icon database

Adventure builds its own save and inventory icon cache from the selected local game's `romfs`, using the verified mappings in `resources/icon_manifest.json`. If no usable cache exists, it generates one when the launcher opens or an installation completes. **Rebuild icon database** in Tools or Settings creates a fresh cache and reloads the displayed icons. The Qt launcher is no longer needed for this step.

PNG files and `index.json` stay under `$XDG_CACHE_HOME/hookshot/save-assets` (normally `~/.cache/hookshot/save-assets` on Linux, or Local AppData on Windows). These are game-derived files and must never be distributed. Failed icons are recorded in the index and retain their placeholders; a failed rebuild preserves the previous cache. ASTC textures require `astcenc`. Cosmetic patch preparation uses an installed or bundled codec, or downloads and verifies Arm's pinned 5.7.1 codec on x86-64 Linux and Windows and caches it locally. No game files are uploaded. For other systems or offline setup, install `astcenc`, set `HOOKSHOT_ASTCENC`, or, from the source root, run `dotnet run --project builder/Adventure/src/HookshotAdventure.Cli -- build-astc --source <local astc-encoder checkout>`.

The experimental NPC and quest rewards setting currently exchanges the Lost Woods target first-clear reward with one green-rupee chest. Saria's Ocarina and the five one-time Skulltula House rewards have separate experimental exchanges. These are limited exchanges, not a general event-reward shuffle; see the testing handoff for gameplay coverage. To research additional event rewards, run `dotnet run --project builder/Adventure/src/HookshotAdventure.Cli -- scan-event-rewards --romfs <local-romfs> --out <local-report.json>`. The report lists candidate item-giving subflows and flows the current reader could not inspect. Other candidates are not verified checks and are not automatically added to seeds. Keep reports derived from game data on the PC.

For Skulltula reachability research, `dotnet run --project builder/Adventure/src/HookshotAdventure.Cli -- scan-skulltulas --romfs <local-romfs> --out <local-report.json>` lists candidate gold enemy placements in loose maps. Map variants and access rules are not yet reviewed; the report does not establish seed beatability.
