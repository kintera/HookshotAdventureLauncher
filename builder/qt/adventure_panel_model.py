"""Read-only, SoH-inspired Adventure v0.8 presentation model.

The Switch 2 save reader exposes direct stock for swords, shields, masks,
tunics and strength upgrades, but only some other inventory quantities.
Unknown acquisition flags MUST remain unknown; the panel may show their
authentic silhouettes but cannot claim them as collected.
"""
from __future__ import annotations

from dataclasses import dataclass
from collections.abc import Mapping

ADVENTURE_UI_VERSION = 'v0.8'

# Exact seven-column inventory arrangement requested for Adventure v0.8.
# "Seeds" is displayed as Deku Nuts here: ordinary slingshot Deku Seed ammo
# remains a number on the Slingshot, NOT a second ammunition tile.
# Progressive role keys are layout slots, not texture names; resolve them to
# the strongest *confirmed* owned icon at render time.
INVENTORY_COLUMNS = 7
INVENTORY_ROWS = 4
PROGRESSIVE_TIERS = {
    'progressive_ocarina': ('ocarina_fairy', 'ocarina_time'),
    'progressive_hookshot': ('hookshot', 'longshot'),
    'progressive_strength': ('bracelet_goron', 'gauntlets_silver', 'gauntlets_gold'),
}
INVENTORY_ROLES = (
    # Slingshot, Boomerang, Sticks, Nuts, Beans, Ocarina, Lens
    'slingshot', 'boomerang', 'deku_sticks', 'deku_nuts',
    'magic_bean', 'progressive_ocarina', 'lens_of_truth',
    # Bow, Hookshot/Longshot, Hammer, Bombs, Bombchus, Scrub Seeds, Iron Boots
    'bow', 'progressive_hookshot', 'hammer', 'bomb', 'bombchu',
    'business_scrub_seed', 'boots_iron',
    # Spells, elemental arrows and Hover Boots
    'dins_fire', 'farores_wind', 'nayrus_love',
    'fire_arrow', 'ice_arrow', 'light_arrow', 'boots_hover',
)
BOTTLE_ROLES = tuple('bottle_slot_' + str(i) for i in range(1, 6))
INVENTORY_GRID_ROLES = INVENTORY_ROLES + BOTTLE_ROLES + ('agony_stone', 'progressive_strength')
assert len(INVENTORY_ROLES) == INVENTORY_COLUMNS * (INVENTORY_ROWS - 1)
assert len(INVENTORY_GRID_ROLES) == INVENTORY_COLUMNS * INVENTORY_ROWS

# Swords and shields retain separate visible slots. Tunics form their own row.
EQUIPMENT_ROLES = (
    'sword_kokiri', 'sword_master', 'sword_biggoron',
    'shield_deku', 'shield_hylian', 'shield_mirror',
)
OTHER_GEAR_ROLES = ('tunic_kokiri', 'tunic_goron', 'tunic_zora')
MASK_ROLES = (
    'mask_keaton', 'mask_skull', 'mask_spooky', 'mask_bunny',
    'mask_truth', 'mask_goron', 'mask_zora', 'mask_gerudo',
    'mask_amiibo_0', 'mask_amiibo_1',
)
GEAR_ROLES = frozenset(EQUIPMENT_ROLES + OTHER_GEAR_ROLES + MASK_ROLES + (
    'boots_iron', 'boots_hover', 'bracelet_goron',
    'gauntlets_silver', 'gauntlets_gold',
))
# Counts are shown inside their associated icon tile, never as extra ammo icons.
SUPPLY_ROLES = {
    'slingshot': 'deku_seeds',
    'bow': 'arrows',
    'deku_sticks': 'deku_sticks',
    'deku_nuts': 'deku_nuts',
    'bomb': 'bombs',
    'bombchu': 'bombchus',
    'magic_bean': 'magic_beans',
    'business_scrub_seed': 'business_scrub_seeds',
}


@dataclass(frozen=True)
class ItemAppearance:
    role: str
    owned: bool | None
    quantity: int | None = None
    equipped: bool = False


def _explicit_flag(stats, group, role):
    flags = stats.get(group)
    if not isinstance(flags, Mapping):
        return None
    value = flags.get(role)
    return value if type(value) is bool else None


def item_appearance(stats, role):
    """Display a true item only when acquisition/equipment is established.

    Progressive slots show the highest confirmed owned tier, rather than
    every historical tier. If no tier is confirmed, show its initial icon
    ghosted, without pretending that it is owned. In particular, ammunition
    counts don't prove ownership of the Slingshot or Bow.
    """
    stats = stats if isinstance(stats, Mapping) else {}
    if role in PROGRESSIVE_TIERS:
        return progressive_appearance(stats, role)
    supply = SUPPLY_ROLES.get(role)
    quantity = stats.get(supply) if supply else None
    if type(quantity) is not int or not 0 <= quantity <= 999:
        quantity = None
    group = 'equipment_flags' if role in GEAR_ROLES else 'inventory_flags'
    owned = _explicit_flag(stats, group, role)
    # Permit inventory_flags for equipment while older save readers transition
    # to a dedicated equipment_flags dictionary, without overriding False.
    if owned is None and group == 'equipment_flags':
        owned = _explicit_flag(stats, 'inventory_flags', role)
    if role not in ('bow', 'slingshot') and owned is None and quantity is not None and quantity > 0:
        owned = True
    equipped = False
    if role in EQUIPMENT_ROLES and '_' in role:
        item_type, variant = role.split('_', 1)
        items = (stats.get('equipped_' + item_type), stats.get('second_equipped_' + item_type))
        for item in items:
            if isinstance(item, str):
                lower = item.casefold()
                if item_type in lower and (variant in lower or (variant == 'hylian' and 'hylia' in lower)):
                    equipped = True
    if role in OTHER_GEAR_ROLES:
        selected = stats.get('equipped_tunic')
        if isinstance(selected, str):
            key = selected.casefold()
            role_aliases = {
                'tunic_kokiri': ('obj_armor_child_normal', 'armor_adult_normal', 'tunic_kokiri'),
                'tunic_goron': ('armor_adult_goron', 'tunic_goron'),
                'tunic_zora': ('armor_adult_zora', 'tunic_zora'),
            }
            equipped = key in role_aliases[role]
    # An item explicitly equipped in either era is owned even when the pouch
    # stock reads zero: equipped shields/swords may not be counted as loose
    # stock. Only this specific equipped role is promoted, not other items.
    if equipped:
        owned = True
    return ItemAppearance(role, owned, quantity, equipped)


def progressive_appearance(stats, role):
    """Return the highest positively verified upgrade; never infer a tier.

    The ordering in PROGRESSIVE_TIERS is lowest to highest. A newer item
    replaces the older icon without creating extra grid slots or claiming
    an unverified upgrade. A standalone, older vanilla save can therefore
    still show Fairy Ocarina or Goron Bracelet when that's all it owns.
    """
    if role not in PROGRESSIVE_TIERS:
        raise KeyError(role)
    tiers = PROGRESSIVE_TIERS[role]
    views = tuple(item_appearance(stats, tier) for tier in tiers)
    for view in reversed(views):
        if view.owned is True:
            return view
    # Lowest-tier artwork is the stable empty/unknown placeholder.
    base = views[0]
    return ItemAppearance(base.role, base.owned, base.quantity, False)


def appearance_without_qt(stats, role):
    """Explicitly named headless alias for source/data regression tests."""
    return item_appearance(stats, role)


def safe_character_name(stats):
    """Do not substitute the launcher profile name for an unverified Link name."""
    if not isinstance(stats, Mapping):
        return 'Unknown'
    for key in ('character_name', 'player_name', 'link_name'):
        value = stats.get(key)
        if isinstance(value, str) and value.strip():
            return value.strip()[:32]
    return 'Unknown'


# Every bottle's contents are shown directly below its authentic icon.
# Compact captions fit one 75-pixel inventory column without hover text.
BOTTLE_CAPTIONS = {
    'Empty Bottle': 'Empty',
    'Red Potion': 'Red Potion',
    'Green Potion': 'Green Potion',
    'Blue Potion': 'Blue Potion',
    'Blue Fire': 'Blue Fire',
    'Fish': 'Fish',
    "Ruto's Letter": "Ruto's Letter",
    'Big Poe': 'Big Poe',
    'Poe': 'Poe',
    'Bugs': 'Bug',
    'Fairy': 'Fairy',
    'Milk': 'Milk',
    'Half Milk': 'Half Milk',
    'No bottle': '—',
    'Unknown Contents': 'Unknown',
    'Unknown': 'Unknown',
}


def bottle_caption(view):
    """Permanent visible label for a saved bottle; independent of tooltips.

    Do not label an absent or unrecognized bottle as empty. Only confirmed
    empty bottles get the Empty caption, and valid contents use their names.
    """
    if not isinstance(view, Mapping):
        return 'Unknown'
    if view.get('owned') is False:
        return '—'
    label = view.get('label')
    if not isinstance(label, str):
        return 'Unknown'
    return BOTTLE_CAPTIONS.get(label, 'Unknown')


def bottle_appearance(stats, slot):
    """View model for one saved bottle; never fabricate contents from a count."""
    if not isinstance(stats, Mapping) or not isinstance(slot, int) or not 1 <= slot <= 5:
        return {'slot': slot, 'owned': None, 'role': None, 'label': 'Unknown'}
    values = stats.get('bottle_slots')
    if not isinstance(values, (tuple, list)) or len(values) < slot:
        return {'slot': slot, 'owned': None, 'role': None, 'label': 'Unknown'}
    value = values[slot - 1]
    if not isinstance(value, Mapping) or type(value.get('owned')) not in (bool, type(None)):
        return {'slot': slot, 'owned': None, 'role': None, 'label': 'Unknown'}
    return value

# Steam Deck Adventure Details: these five independent capacities are already
# decoded by save_reader.CAPACITIES. "Deku-Seed Bag" and "Bullet Bag" are two
# names for the SAME slingshot pouch, never two separate save progressions.
# Values outside the exact known tiers remain unknown (including edited saves).
BAG_UPGRADES = (
    ('sticks', 'Deku Stick Bag', 'deku_stick_capacity', (10, 20, 30), 'deku_sticks'),
    ('nuts', 'Deku Nut Bag', 'deku_nut_capacity', (20, 30, 40), 'deku_nuts'),
    ('seeds', 'Deku-Seed / Bullet Bag', 'deku_seed_capacity', (30, 40, 50), 'deku_seeds'),
    ('quiver', 'Quiver', 'quiver_capacity', (30, 40, 50), 'bow'),
    ('bombs', 'Bomb Bag', 'bomb_bag_capacity', (20, 30, 40), 'bomb'),
)


@dataclass(frozen=True)
class BagUpgrade:
    key: str
    name: str
    capacity: int | None
    tier: int | None  # 0 absent, 1 base, 2 bigger, 3 biggest, None unknown
    icon_role: str | None
    item_role: str

    @property
    def level(self) -> str:
        return {0: 'Not acquired', 1: 'Base', 2: 'Bigger',
                3: 'Biggest', None: 'Unknown'}[self.tier]


def bag_upgrade(stats, bag_key):
    """Decode capacity without conflating current ammo count and bag size."""
    desc = next((row for row in BAG_UPGRADES if row[0] == bag_key), None)
    if desc is None:
        raise KeyError(bag_key)
    key, name, field, tiers, item = desc
    value = stats.get(field) if isinstance(stats, Mapping) else None
    if type(value) is not int:
        return BagUpgrade(key, name, None, None, None, item)
    if value == 0:
        return BagUpgrade(key, name, 0, 0, None, item)
    if value not in tiers:
        return BagUpgrade(key, name, None, None, None, item)
    tier = tiers.index(value) + 1
    return BagUpgrade(key, name, value, tier, f'bag_{key}_{tier}', item)


@dataclass(frozen=True)
class TradeProgress:
    era: str
    label: str
    stage: int | None
    total: int
    completed: bool
    role: str | None
    # True only when a currently held item or Mask of Truth is directly read.
    evidence: str | None


def trading_quest(stats, era):
    """Select last *positively held* trade item; don't invent history.

    Child completion requires the Mask of Truth stock, not merely Bunny Hood.
    Adult stages show currently verified items; stock alone cannot prove that
    a consumed/traded stage has been completed when no later item is held.
    """
    if era not in ('child', 'adult'):
        raise KeyError(era)
    try:
        from .save_reader import CHILD_TRADE_STOCKS, ADULT_TRADE_STOCKS
    except ImportError:
        from save_reader import CHILD_TRADE_STOCKS, ADULT_TRADE_STOCKS
    stages = CHILD_TRADE_STOCKS if era == 'child' else ADULT_TRADE_STOCKS
    flags = stats.get('trade_flags', {}) if isinstance(stats, Mapping) else {}
    sources = stats.get('trade_sources', {}) if isinstance(stats, Mapping) else {}
    if not isinstance(flags, Mapping):
        flags = {}
    if not isinstance(sources, Mapping):
        sources = {}
    for index in reversed(range(len(stages))):
        role, name, _ = stages[index]
        if flags.get(role) is True:
            complete = era == 'child' and role == 'mask_truth'
            return TradeProgress(era, 'Complete · Mask of Truth' if complete else name,
                                 index + 1, len(stages), complete, role,
                                 sources.get(role))
    return TradeProgress(era, 'Not Collected', None, len(stages), False, None, None)


def trading_hover_text(stats, era):
    view = trading_quest(stats, era)
    header = 'Child Trading Quest' if era == 'child' else 'Adult Trading Quest'
    if view.stage is None:
        return f'{header}\nNot Collected'
    note = ('\nRegular masks unlocked (amiibo masks excluded)'
            if view.completed else '\nCurrent verified item; earlier trades not independently tracked')
    return f'{header}\n{view.label} · {view.stage}/{view.total}{note}'



def latest_trading_item(stats):
    """One icon for the latest positively verified trading item.

    Adult trading starts after child trading in the normal game sequence, so
    an adult item takes precedence when both eras have held items in the save.
    If no adult item is positively held, preserve the latest confirmed child
    item (especially Mask of Truth). The save does not establish pickup dates
    or completion of consumed trades, so this is *not* a history counter.
    """
    adult = trading_quest(stats, 'adult')
    if adult.stage is not None:
        return adult
    return trading_quest(stats, 'child')


def trading_quest_overview_hover_text(stats):
    """Short combined hover; the panel never splits trading by age."""
    progress = latest_trading_item(stats)
    if progress.role is None:
        return 'Trading Quest\nNot Collected'
    if progress.completed:
        return 'Trading Quest\nMask of Truth · Complete'
    return f'Trading Quest\n{progress.label}'


def gerudo_pass_hover_text(stats):
    flags = stats.get('inventory_flags', {}) if isinstance(stats, Mapping) else {}
    owned = flags.get('gerudo_pass') is True if isinstance(flags, Mapping) else False
    return "Gerudo Pass\nCollected" if owned else "Gerudo Pass\nNot Collected"


ITEM_NAMES = {
    'slingshot': 'Fairy Slingshot', 'boomerang': 'Boomerang',
    'deku_sticks': 'Deku Sticks', 'deku_nuts': 'Deku Nuts',
    'magic_bean': 'Magic Beans', 'ocarina_fairy': 'Fairy Ocarina',
    'ocarina_time': 'Ocarina of Time', 'lens_of_truth': 'Lens of Truth',
    'bow': 'Fairy Bow', 'hookshot': 'Hookshot', 'longshot': 'Longshot',
    'hammer': 'Megaton Hammer', 'bomb': 'Bombs', 'bombchu': 'Bombchus',
    'business_scrub_seed': 'Business Scrub Seeds', 'boots_iron': 'Iron Boots',
    'dins_fire': "Din's Fire", 'farores_wind': "Farore's Wind",
    'nayrus_love': "Nayru's Love", 'fire_arrow': 'Fire Arrows',
    'ice_arrow': 'Ice Arrows', 'light_arrow': 'Light Arrows',
    'boots_hover': 'Hover Boots', 'agony_stone': 'Echo Gem',
    'bracelet_goron': "Goron's Bracelet", 'gauntlets_silver': 'Silver Gauntlets',
    'gauntlets_gold': 'Golden Gauntlets',
    'sword_kokiri': 'Kokiri Sword', 'sword_master': 'Master Sword',
    'sword_biggoron': "Biggoron's Sword", 'shield_deku': 'Deku Shield',
    'shield_hylian': 'Hylian Shield', 'shield_mirror': 'Mirror Shield',
    'tunic_kokiri': 'Kokiri Tunic', 'tunic_goron': 'Goron Tunic',
    'tunic_zora': 'Zora Tunic',
    'mask_keaton': 'Keaton Mask', 'mask_skull': 'Skull Mask',
    'mask_spooky': 'Spooky Mask', 'mask_bunny': 'Bunny Hood',
    'mask_truth': 'Mask of Truth', 'mask_goron': 'Goron Mask',
    'mask_zora': 'Zora Mask', 'mask_gerudo': 'Gerudo Mask',
    'mask_amiibo_0': 'Fairy-Kid Mask', 'mask_amiibo_1': 'Princess Mask',
}
SUPPLY_LABELS = {'slingshot': 'Deku Seeds', 'bow': 'Arrows',
                 'deku_sticks': 'Sticks', 'deku_nuts': 'Nuts',
                 'bomb': 'Bombs', 'bombchu': 'Bombchus',
                 'magic_bean': 'Beans', 'business_scrub_seed': 'Scrub Seeds'}


def item_hover_text(stats, role):
    """Real mapped identity, verified quantity, and only defensible ownership."""
    view = item_appearance(stats, role)
    name = ITEM_NAMES.get(view.role, view.role.replace('_', ' ').title())
    parts = [name]
    if view.quantity is not None:
        parts.append(f'{SUPPLY_LABELS.get(role, "Quantity")}: {view.quantity}')
    if view.owned is not True:
        # UI language only: retain None in the read-only decoder for diagnostics.
        parts.append('Not Collected')
    available = stats.get('mask_available_flags', {}) if isinstance(stats, Mapping) else {}
    if (view.owned is not True and isinstance(available, Mapping)
            and available.get(view.role) is True):
        parts.append('Unlocked at Mask Shop')
    return '\n'.join(parts)


def bottle_hover_text(stats, slot):
    bottle = bottle_appearance(stats, slot)
    if bottle['owned'] is False:
        return f'Bottle {slot}\nNot collected'
    if bottle['owned'] is not True:
        return f'Bottle {slot}\nContents unknown'
    return f'Bottle {slot}\n{bottle["label"]}'


def bag_hover_text(stats, bag_key):
    bag = bag_upgrade(stats, bag_key)
    if bag.tier is None:
        return f'{bag.name}\nCapacity unknown'
    if bag.tier == 0:
        return f'{bag.name}\nNot acquired'
    return f'{bag.name}\n{bag.level} · Holds {bag.capacity}'
