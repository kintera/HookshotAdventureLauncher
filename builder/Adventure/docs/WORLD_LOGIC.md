# Randomizer world logic

The goal is a Ganon-beatable seed, with access logic and fill coverage comparable to
Ship of Harkinian. Its [region logic](https://github.com/HarbourMasters/Shipwright/tree/develop/soh/soh/Enhancements/randomizer/location_access)
and [fill implementation](https://github.com/HarbourMasters/Shipwright/blob/develop/soh/soh/Enhancements/randomizer/3drando/fill.cpp)
are useful references. This implementation uses its own C# model and Hookshot's
reviewed remake-specific location identities.

[OOTRR](https://github.com/ootrando/ootrr) also targets this remake and exposes
Hookshot-compatible mods. Its remake-specific logic and event coverage are useful
for cross-checking, but a rule or placement still needs to be matched to this
build's game resources and settings. OOTRR's current code is GPL-3.0-or-later;
retain its required notices when reusing code or data.

`HAL_WorldReachability` evaluates explicit region connections for child/adult and
day/night, persistent events, item requirements, token checks and a goal. Only
regions explicitly marked as passing time permit waiting from day to night or
back; a region does not implicitly change age.
`HAL_WorldPlacement` places progression through reachable checks, fills the
remaining slots, and verifies the goal again with the configured token cap.
Items not needed for the goal can be placed beyond that cap.

The world engine has synthetic tests. The seed generator invokes it on its final
chest assignments for the reviewed opening sword/slingshot route. Its child forest
graph separates the Lost Woods target and the forest bridge, models Mido's
sword-and-shield requirement, and requires a separately established Gohma defeat
before the closed-forest bridge is reached. The target requires the slingshot.
The Deku lobby and Slingshot Room are separate: reaching the room requires a
Deku Shield, Slingshot or Boomerang for the scrub. The normal opening audit
still assumes a shield can be purchased, so this refinement preserves that
assumption while preventing the generic open-Deku graph from treating the room
as freely reachable. When the Deku map is granted at start, its freed lobby
chest may also hold the slingshot; its exact placement and source resource are
checked, and its map-specific event is removed. An isolated game run confirmed
the shuffled chest gave the slingshot and seeds, and that it stayed open and the
slingshot worked after save/reload. That run teleported into the tree and did
not verify the preceding closed-forest route or the starting-map grant together.
These rules use SoH's [Kokiri Forest](https://github.com/HarbourMasters/Shipwright/blob/develop/soh/soh/Enhancements/randomizer/location_access/overworld/kokiri_forest.cpp)
and [Lost Woods](https://github.com/HarbourMasters/Shipwright/blob/develop/soh/soh/Enhancements/randomizer/location_access/overworld/lost_woods.cpp)
regions as a reference. The graph also follows the child route through Lost Woods
to the [Sacred Forest Meadow](https://github.com/HarbourMasters/Shipwright/blob/develop/soh/soh/Enhancements/randomizer/location_access/overworld/sacred_forest_meadow.cpp)
entrance and gates the meadow behind defeating its Wolfos; the Kokiri Sword is a
known route, while other combat methods require an explicit capability. The
Hyrule Field-to-Kakariko and Skulltula House route includes the five one-time
reward thresholds. The child Know-It-All House token is recognized by its local
map placement and actor, with conservative night and sword requirements. The
Kokiri bean-patch token is also identified and requires a sword and the separately
established ability to release bugs. The adult House of Twins token requires night
and Hookshot or Longshot. Their remake locations were cross-checked
against the NX2-ZOOTR map but have not been playtested here. Most other token locations
are not yet mapped, so this does not
enforce the cap for generated full-game seeds. The
child graph also includes the field's direct exits to the Market entrance, Zora
River front, Lon Lon Ranch, Gerudo Valley and Lake Hylia. Lake Hylia connects to
the lab and fishing island, and its underwater passage to Zora's Domain requires
a silver or gold scale in either direction. Those edges follow the remake
reference's [Lake Hylia](https://github.com/ootrando/ootrr/blob/0fa64f4/data/logic/World/Overworld/Lake%20Hylia.json)
and Zora's Domain rules. The [Zora River](https://github.com/ootrando/ootrr/blob/0fa64f4/data/logic/World/Overworld/Zoras%20River.json)
route also models the front obstacle and Zelda's Lullaby at the waterfall: the
former requires an explicit blasting capability until consumables are modeled,
and the latter requires a playable ocarina and the song. The reverse exit is
open. The Lost Woods underwater shortcut requires a scale and forest access on
the way out, while returning from the river requires a scale. Their checks and the rest of those interiors are not yet represented or
playtested here. The child Market graph connects the entrance, back alley, and
Temple of Time. The Market entrance returns to Hyrule Field only during the day,
and time does not pass in those regions. This follows the remake reference's
[Market rules](https://github.com/ootrando/ootrr/blob/0fa64f4/data/logic/World/Overworld/Market.json);
the connections have not been verified in-game. The Market guard
house token's actor and placement were cross-checked against the local map scan
and remake catalog; its conservative rule requires a crate-breaking capability
and the Kokiri Sword. This token has not yet been verified in-game. No Market
item checks or Door of Time rule are mapped yet. The local scan and remake
catalog also identify the Lake Hylia bean-patch and Zora River ladder tokens.
The former requires bugs and a child attack, and the latter requires access to
the river, night and a child attack; these token rules are not playtested. The
opening audit currently assumes the Deku Shield can be bought. Two Kokiri map
checks are explicitly excluded from immediately reachable opening placements:
the Song of Storms grotto chest and the high bridge chest. Their positions were
cross-checked against the [NX2-ZOOTR remake region map](https://github.com/abslswrld/NX2-ZOOTR/blob/main/data/world/KokiriForest.json);
that project marks the bridge chest's access as unknown. Other Kokiri chest access
still needs individual review. Existing immutable seeds are not regenerated by
this safeguard; seeds generated before it may have placed an opening item at one
of these checks. The bridge chest has a second placement in Lost Woods, with a
different candidate collection flag; both copies remain vanilla until their
cross-map collection behavior is verified. The audit does not infer that defeating
Gohma is possible from entering the tree. The conservative child dungeon graph
follows the lobby, basement, back room, ledge, before-boss room and Gohma room;
it requires an explicitly supplied torch-lighting capability, a projectile, a
scrub interaction and the sword plus slingshot for the boss before marking
Gohma defeated. This subset follows the
[Deku Tree](https://github.com/ootrando/ootrr/blob/0fa64f4/data/logic/World/Deku%20Tree.json)
and [boss](https://github.com/ootrando/ootrr/blob/0fa64f4/data/logic/World/Bosses.json)
reference rules; it is not yet verified through a complete in-game dungeon run,
and the seed validator does not yet derive torch capability from inventory.
Open-forest and open-Deku graph options
exist for tests, but seed validation deliberately does not consume the launcher
switches until their experimental gameplay effects have been verified.
It does not use a complete world graph or certify a path to Ganon. The current
Skulltula shuffle exchanges its one-time rewards with rupee chests, and its
maximum is not enforced for a full-game route.

An audit of OOTRR's local reference world at commit `0fa64f4` found 349 region
records, 1,588 location rules, 70 event rules and 678 exit rules across 35
world files. The 2,336 rules parsed as Python-style expressions in that audit;
they include region-dependent `at`/`here` checks, age-sensitive item and song
helpers, settings comparisons, and dungeon-specific key rules. Parsing those
expressions alone is not enough: the helpers and their state semantics must be
implemented, and the reference locations must be matched to this build's actual
placements before the final seed can be validated against the world.

Before enabling a full-game beatability claim, the model needs reviewed remake
region and location mappings, child/adult and time transitions, dungeon access
and completion events, vanilla and shuffled rewards, Skulltula token locations,
starting items and access settings. Dungeon keys and other consumable or
irreversible resources need a model that accounts for their use; the current
monotone inventory evaluator does not establish those routes. Each rule needs
regression cases and local gameplay verification, and generated overlays must
be checked against the same final placements that the player receives.
