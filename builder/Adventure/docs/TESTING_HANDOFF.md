# Development randomizer testing handoff

This is a development test build, not a feature-parity or full-game beatability
release. Start the launcher from the checkout with `./hookshot-dev.sh`; select
the updated Hookshot-Dev game in Dev Build Setup before creating a randomized
adventure. The launcher and game build are separate: rebuilding the launcher
alone does not add the starting-inventory and other native hooks to an older
game executable. Use a new isolated adventure for each seed. Existing seeds are
fixed to their original profile and must not be regenerated in place.

The launcher uses Hookshot's own Python save-editor backend and stock build
scripts. Seed planning, resource overlays and cosmetic preparation run in C#.
First-time development setup can use retained files from a selected existing
Hookshot installation or the install path saved by the Linux Builder. It makes
independent ROMFS copies in a separate installation. A local build from an
existing development install completed, and its copied files had no shared
inodes; its startup reached shader preparation but gameplay was not checked.

## What to test

- Create and reopen new experimental full-game seeds. Check sword and slingshot placement from the seed data against the
  closed-forest opening: the sword must be reachable in Kokiri before Deku;
  the slingshot must be reachable there, in the Deku slingshot chest, or in the
  freed lobby map chest when maps start in inventory. Full-game
  progression and final-boss reachability are **not** verified by the solver.
  Existing seeds retain their original recorded scope.
- Test starting equipment, songs, ammunition and maps/compasses on a fresh save,
  then save and reload to check grants do not repeat. A shuffled chest source
  for a starting item is replaced with a blue rupee; some non-chest vanilla
  sources remain unchanged. In particular, burn a starting Deku Shield and
  reload to verify it does not reappear; test a second slot independently.
  Upgrade a starting Bomb Bag and verify the larger capacity survives the
  periodic starting-item check and a reload.
- A newly granted starting slingshot receives the game's basic 30-seed bag,
  including its automatic initial fill. The seeds control is therefore a
  minimum, not a way to start with fewer than 30 on that first grant. An empty
  50-capacity bag was not refilled or reduced when an edited save reloaded;
  a bag upgraded through normal gameplay still needs verification.
  Starting slingshot seeds require a game built with starting-inventory
  capability 6; older capability 5 builds remain usable for other starting
  items. Rebuild Hookshot-Dev in Dev Build Setup if the launcher requests it.
- With NPC rewards enabled, test both first-clear Lost Woods target branches,
  including before and after obtaining a Bullet Bag. With Saria's Ocarina enabled,
  test the bridge gift, the replacement chest, inventory and reload. Test the
  five one-time Skulltula House rewards and their replacement chests at 10, 20,
  30, 40 and 50 tokens, plus the unchanged repeatable reward. These event
  exchanges currently use rupee chests; they are not general event shuffles.
- The requested Skulltula maximum is recorded but not enforced by the solver.
  Do not assume a seed is beatable within that maximum.
- Test heart-piece chest shuffle, per-location filler-only exclusions and
  starting inventory together with the event options. Check the displayed
  locations and the actual rewards after save/reload.
- Test child stealth skip, Gohma intro skip, Deku exit skip and the owl/entrance
  skips individually and in combination. Verify the letter, song, spiritual
  stone, remaining story interactions and camera state; an event edit passing
  an offline resource check does not establish in-game behavior.
- Test save editing on a disposable copy: stage changes, cancel, save, reload,
  and verify the backup. Cover inventory, quantities and bag levels, bottles,
  trade items, songs, magic, hearts and supported age changes.
  Adding a slingshot or bow in the editor also stages its basic seed bag or
  quiver when absent, preserving larger bags; the editor reports the addition.
  The scale selector has authored save and UI coverage; a Golden Scale edited
  into an isolated save loaded with its stock and acquisition flags set. Dive
  depth and Silver Scale gameplay still need checking.

Enable the launcher's Debug Key setting for an F9 trace when recording an
unexpected event or reward. Include the seed, chosen settings, profile/slot,
location and steps alongside the log. Keep saves and game-derived resources on
the local PC. The two bridge-area chest copies remain vanilla until their shared
collection behavior is established with gameplay evidence.

## Known incomplete controls

**Bombchus:** randomized adventures have an independent minimum capacity of 20
without granting a bomb bag, then follow the bag's 20/30/40 capacity. The
launcher requires a runtime with the matching bombchu-capacity marker. The save
editor uses the same rule and retains up to 20 when a bag is removed.
In isolated headless 3.0.2 Kokiri Forest runs, debug grants reached 20, 20, 30,
and 40 with bag capacities 0, 20, 30, and 40 respectively; extra grants stayed
at the limit. A separate run started without a bag and used native inventory
events to acquire medium and large bags: bombchus rose from 20 to 30 to 40,
and the inventory displayed 40/40. A manual save reloaded with 40 bombchus.
An earlier pre-fix chest test granted and saved bombchus with no bag. Actual
chest pickups after upgrades, other operating systems and GPUs remain untested.

**Ammo reward regression:** randomized small and large chests now use a launch-time
conditional reward: a blue rupee without the matching quiver, bomb bag or
bullet bag, and the assigned ammo when the capacity is owned. The launcher
applies this to a derived runtime overlay for new and existing seeds; it does
not change the seed or unrelated chests. For large chests it creates a private
conditional actor inheriting the original large chest's model and physics.
Bombchus keep their independent capacity. In a visible 3.0.2 test with no
quiver, the player first reproduced the unguarded arrow pickup from the large
Kokiri Sword chest, then confirmed the guarded chest stayed large and gave a
blue rupee. The integrated actor and registrations match the tested candidate.
An experimental event-flow guard awarded a rupee but still
displayed the arrow model, so it remains disabled.
The game's Hylian Shield chest has a native conditional reward mechanism. In
isolated tests, a small chest using that mechanism showed a blue rupee for
arrows without a quiver, showed arrows with an editor-created quiver, and
showed and saved five rupees for bombs without a bomb bag. The newer autosave
in the quiver-owned arrow test contains five arrows, a bomb-bag-owned test
saved five bombs, and a seed-bag-owned test saved five seeds. The no-bag seed
test and the bow-without-quiver test each showed and saved five rupees. The
seed-bag save reloaded with five seeds and its chest still open; an
editor-created largest seed bag also retained five seeds. Other reloads,
bags upgraded through gameplay still need testing. The small-chest game runs
used the experimental 3.0.0 overlay; the 3.0.2 large-chest no-quiver test used
the private-actor candidate. The 3.0.2 launch integration has been checked
against a copied existing seed and synthetic fixtures. Large-chest quiver-owned
pickups, save/reload, other ammo types, the no-bake variant and other hardware
still need in-game tests.

**Deku meeting journal:** skipping the meeting previously bypassed the game's
`Meet_DekuTree` completion and `Get_StoneOfForest` start actions. The updated
overlay routes through those original actions without the meeting scene; it has
resource-level and authored-fixture checks but still needs a fresh in-game
meeting and journal check. Saves that already skipped the meeting are not
automatically repaired by this edit.

Open Forest and Open Deku apply experimental new-save defaults; neither gate's
independent gameplay behavior is verified, and existing saves keep their stored flags. Door of Time, Jabu-Jabu and trial access are live flag
experiments, not established access logic. Song, shop and scrub shuffles,
general dungeon-key/reward shuffles, hints and traps are not implemented.
The updated Deku-clear skip bypasses the stone scene. Its native fallback grants
a missed Kokiri Emerald only after Gohma and the Deku quest are complete. That
fallback repaired an affected isolated save and the stone survived a save and
reload, but the updated skip and fallback together still need a fresh
boss-to-exit playthrough. The user's earlier run showed the stone after the
original scene; it did not test the updated skip.
Trackers are hidden. See [randomizer feasibility](RANDOMIZER_FEASIBILITY.md)
and [world logic](WORLD_LOGIC.md) for the precise current boundaries.

## Verification performed

The launcher core and UI fixture suites run without game data. A local full-game
seed was also generated with target, Saria and Skulltula exchanges; the composed
runtime overlay retained its shuffled Hyrule Field chests with child-stealth,
entrance, owl and Gohma-intro skips enabled together. This is a resource-level
check, not a playthrough. Cross-platform and hardware testing remain necessary.
For the optional Linux AppImage integration, AppRun starts a supplied
self-contained Adventure build by default and retains the stock Builder through
`--builtin`. Dispatch and copied resources have game-free fixtures, and both
windows started from the extracted local Ubuntu 22.04 image. The image passed
the game-free player-build check, including its normal and AMD FSR 4 dry runs,
and its AppImage runtime started the command-line builder. The bundled ASTC
codec passed synthetic image round trips. This local image uses the placeholder
repository `local/HookshotAdventure` and must not be distributed as a release.
The stock Builder reported an X11 BadWindow error when forcibly closed by an
automated window-close command; a subsequent normal window-manager close exited
successfully.
The container wrapper passes the launcher, notices and release-baseline ASTC
codec as read-only inputs. An authored update fixture confirms that the
launcher, codec, license and fork update repository survive an installed update;
it is not a test of a published image or remote release. Windows packaging
has not been integrated.
The event-reward scanner inspected 653 local flowcharts and reported 138
candidate reward sites. It also inspected clip and one-shot string parameters
in 261 timeline-only resources and reported 42 item-related references; these
local references are model bindings and visual effects, not confirmed grants.
There were no parse failures in this scan. Other timeline parameter types are
not searched, and candidates need individual flow and gameplay review before
they can be randomized.
The local game-free repository run excluding the separately completed vbytes
stress case passed 837 checks and failed six. The walker/provenance mismatch was
fixed and its tests passed afterward; the ThinLTO check passed with the bundled
compiler and linker. Three older builder fixtures could not restore their .NET 8
reference packs on this machine, and the unchanged renderer target-retention
check failed. These outstanding checks prevent a clean repository-wide result.
A later sandboxed run of the same subset passed 824 and failed 18: it used system
Python, which lacks Hookshot's prepared `lz4` dependency, and sandbox restrictions
also prevented local sockets and LeakSanitizer from working in several checks.
The walker test passed again with the prepared Python. These results do not
replace a clean run with the supported build tools and without those restrictions.
On a local new-save run with both forest options enabled, the two blocker flags
were clear in Kokiri Forest and remained clear after an autosave reload. Neither
physical gate crossing nor the options independently were verified in that run.
A later run with both flags clear crossed the forest exit into Hyrule Field;
the Deku entrance and each option on its own remain unverified.
On a rebuilt Linux development pack, isolated edited saves loaded with the
selected inventory, song, heart and magic values intact; corresponding removals
also loaded as expected. All 14 trade-item stocks were checked individually
when given and removed; each ocarina, hookshot and strength tier, all 13 bottle
contents, every bag tier and selected ammunition values were checked separately.
All wallet tiers and the bag/bottle removal case, including clamped rupees and
ammunition, also loaded as edited. An acquired-but-absent Deku
Shield remained absent, and a diagnostic large-only Bomb Bag remained at 40
capacity. These checks read game state after loading; they do not establish item
use, quest hand-ins, shield burning or saving after play. A separate retest of
edited Fairy Ocarina and Ocarina of Time confirmed that both open the playing
screen after the editor was fixed to update the equipped-ocarina slot; actual
song completion remains untested. Removing the Ocarina of Time in the editor
also cleared the equipment slot and the play control stayed inactive on reload.
The native starting Fairy Ocarina also equipped and opened its playing screen
on a fresh save; later upgrades and reload remain untested.
One experimental 50-token seed was also played through the Skulltula family
conversation: all five one-time rupee exchanges matched the planned amounts,
their flags set, and a second conversation did not pay them again. The displaced
chest rewards, other seeds, 100-token repeatable reward and save/reload remain
unverified in-game. A separate Lost Woods target first-clear run with the small
seed bag awarded its planned green rupee and set the first-clear flag without
granting the medium bag. The upgraded-bag branch, repeat reward and displaced
chest remain unverified in-game. The same seed's Saria bridge scene awarded its
planned green rupee with Fairy Ocarina stock and acquisition flag at zero. A
follow-up identified its one-time ocarina tutorial as the reported invisible
instrument. The equipment slot remained empty and the D-pad did not open the
instrument, but a subsequent local capture confirmed that the arrival prompt
still appeared. After patching the Hyrule Field arrival event, a further visible
run and local frame capture showed no guide at the transition, the autosave
indicator appeared, and the equipment slot and acquisition remained empty. A
copy of that autosave reloaded in Hyrule Field with the rupee and empty ocarina
state intact. In another seed the Kokiri Sword chest granted the shuffled Fairy
Ocarina, which was playable and remained equipped after saving and reloading;
the chest remained open, but its item-get animation had no visible ocarina
model. A further isolated run entered Saria's scene with a Fairy Ocarina
already equipped: the exchange awarded its rupee, kept the instrument equipped,
and the playing controls opened afterward. Saving and reloading that particular
case and a later return to the scene remain unverified.
An isolated child-stealth grant test exposed that directly writing a live song
flag did not save it. After using the game's queued Bool setter, Zelda's Letter
and the Lullaby flag survived a manual save and reload with the grant disabled.
The songbook listed Lullaby and the game recognized the complete song on that
reloaded save. The full castle skip route and a world interaction using the
song remain unverified.
A starting Deku Tree map flag also survived manual saving and a reload with
the grant disabled after the same queue fix; other dungeon flags and the map
screen have not been individually checked.
A separate seed put the slingshot in the freed Deku lobby map chest: it gave
the slingshot and 30 seeds, remained open after saving and reloading, and the
slingshot fired. The test teleported into the tree with sword and shield; it
did not verify the preceding closed-forest route or starting maps in that run.
