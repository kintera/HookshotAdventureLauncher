"""Read-only Switch 2 Ocarina of Time slot summary for Hookshot's adventure selector.

Field-table structure and identifiers independently adapted from Traiver's
OOT-SW2SE (MIT, copyright 2026 Traiver), https://github.com/Traiver/OOT-SW2SE
No editing, patching, or guessed offsets. Unrecognized fields are unavailable.

The parser intentionally reads only files within an explicitly selected Hookshot
profile's `save/<account>/slot_NN` tree; it does not write any game data.
"""
from __future__ import annotations

from dataclasses import dataclass
from datetime import datetime
from pathlib import Path
import struct

MAGIC = (0x01020304, 0x002D80EB)
HEADER_PROGRESS = 0x175D8
HEADER_CAPTION = 0x2A0
MAX_SAVE_BYTES = 24 * 1024 * 1024
MAX_JPEG_BYTES = 3 * 1024 * 1024

# Values drawn from the field-name/hash table published with OOT-SW2SE.
PROGRESS = {
    'rupees': 0xA77921D7,
    'hearts_current': 0x31AB5580,
    'hearts_max': 0xFBE01DA1,
    'magic_current': 0x76B18200,
    'magic_max': 0x12D63A97,
    'skulltulas': 0x04735978,
    'location': 0x1D6189DA,
    'progress_id': 0x874C7ED4,
}
# Additional IDs checked against Traiver/OOT-SW2SE's field catalog.
# These are independently read, never edited. Unknown IDs stay unavailable.
WALLET = {
    'type': 0x3E42AFFA, 'capacity': 0xE0010A50,
    'small': 0x1D74617E, 'medium': 0x2D2C874B, 'large': 0xAFA4BB52,
}
SUPPLIES = {
    'deku_seeds': (0x5FADF7FC, 50), 'deku_nuts': (0xF30D9F54, 40),
    'deku_sticks': (0xB9494411, 30), 'arrows': (0x518EC74B, 50),
    'bombs': (0xDB1BCD4F, 40), 'bombchus': (0xDF7C517A, 99),
    'magic_beans': (0xB003124D, 10),
    'business_scrub_seeds': (0x600E29EB, 30),
    'heart_pieces': (0x51EC97B3, 3),
}
CAPACITIES = {
    'deku_seed_capacity': (0xAE8CC742, 50),
    'deku_nut_capacity': (0x735B00F7, 40),
    'deku_stick_capacity': (0x457616B0, 30),
    'bomb_bag_capacity': (0xE753508B, 40),
    'quiver_capacity': (0x418DB791, 50),
}
DUNGEON_KEYS = {
    'well': (0x7DD79D10, 2), 'forest': (0x90237092, 5),
    'fire': (0x1DF11487, 7), 'water': (0x877BDCEE, 4),
    'shadow': (0x5EDDA9E3, 5), 'spirit': (0x79EEEC8E, 5),
    'fortress': (0x3A28B6A4, 4), 'training': (0xEA9786F5, 8),
    'castle': (0xFBFD2910, 1),
}
# Exact stock / IsValid / content-enum triples and content enums from
# Traiver/OOT-SW2SE editor.html (MIT). Only reads the slot's field table.
BOTTLE_SLOTS = (
    (0xD53CE3A7, 0xF39CE410, 0x7D48DF76),
    (0xFCA29A04, 0x000FF6EA, 0x1EDD164A),
    (0x893216A1, 0xF63B1194, 0xB80C70A8),
    (0x5324F9D3, 0xB7F052CE, 0xE4C1B1FC),
    (0xC92B16BE, 0xE27B136D, 0x365E1953),
)
BOTTLE_STOCK_HASHES = tuple(fields[0] for fields in BOTTLE_SLOTS)  # compatibility
BOTTLE_CONTENTS = {
    0xBE69DE49: ('bottle_empty', 'Empty Bottle'),
    0xEABA6067: ('bottle_red_potion', 'Red Potion'),
    0x049D37C1: ('bottle_green_potion', 'Green Potion'),
    0xF383D6E5: ('bottle_blue_potion', 'Blue Potion'),
    0x1C293517: ('bottle_blue_fire', 'Blue Fire'),
    0x1E834EF8: ('bottle_fish', 'Fish'),
    0x0F24E808: ('bottle_letter', "Ruto's Letter"),
    0xCDC21471: ('bottle_big_poe', 'Big Poe'),
    0x02084F9E: ('bottle_poe', 'Poe'),
    0x74D62ECB: ('bottle_bugs', 'Bugs'),
    0x6F49E357: ('bottle_fairy', 'Fairy'),
    0x73356827: ('bottle_milk', 'Milk'),
    0xEEF6E4B0: ('bottle_milk_half', 'Half Milk'),
}


def decode_bottle_slots(table: 'FieldTable') -> tuple[dict, ...]:
    """Read each bottle without treating a missing/mismatched field as empty.

    A bad/unknown content enum leaves only that bottle's content unknown;
    other decoded bottle slots remain usable. The original file is immutable.
    """
    result = []
    for index, (stock_key, valid_key, content_key) in enumerate(BOTTLE_SLOTS, 1):
        stock, valid, content = (table.i32(stock_key), table.i32(valid_key), table.i32(content_key))
        slot = {'slot': index, 'owned': None, 'role': None, 'label': 'Unknown', 'content_hash': None}
        if stock in (0, 1) and (valid is None or valid == stock):
            if not stock:
                slot.update(owned=False, label='No bottle')
            else:
                slot['owned'] = True
                if content is not None:
                    enum = content & 0xFFFFFFFF
                    slot['content_hash'] = enum
                    role_label = BOTTLE_CONTENTS.get(enum)
                    if role_label is not None:
                        slot['role'], slot['label'] = role_label
                    else:
                        slot['label'] = 'Unknown Contents'
        result.append(slot)
    return tuple(result)

EQUIPMENT = {'sword': 0xA95A3521, 'shield': 0xEE463235,
             'second_sword': 0xA1F7EE2B, 'second_shield': 0x2342B619}

# PouchData.*.StockNum identifiers supplied from the Switch 2 field catalog.
# These represent CURRENT inventory stock, independently of PlayerStatus's
# equipped-item strings. A stock count of zero means absent; a positive count
# means owned. Keep the Giant's Knife and broken variant out of this display.
SWORD_STOCKS = {
    'sword_kokiri': 0xEEB495B9,   # Weapon_Sword_Kokiri
    'sword_master': 0xF1C45294,   # Weapon_Sword_Master
    'sword_biggoron': 0xB0B3E399, # Weapon_Sword_Biggoron
}
SHIELD_STOCKS = {
    'shield_deku': 0x7CA64F23,    # Weapon_Shield_Deku
    'shield_hylian': 0x08BEFD3A,  # Weapon_Shield_Hylian
    'shield_mirror': 0xF58E0E2F,  # Weapon_Shield_Mirror
}

# All StockNum fields are MurmurHash3_x86_32(name, seed=0). The Slingshot,
# Bow, Gerudo Pass and trade sequence hashes match the supplied catalog.
# Boomerang was calculated from PouchData.Item_Boomerang.StockNum using
# that verified naming algorithm; confirm against an owned gameplay save.
RANGED_STOCKS = {
    'slingshot': 0x80D34CE9,  # Weapon_Range_SlingShot
    'boomerang': 0xA1D76D31, # Item_Boomerang (derived, needs live-save confirmation)
    'bow': 0x938D56BF,        # Weapon_Range_Bow
}
GERUDO_PASS_STOCK = 0xFEB4D56B  # PouchData.Item_Token_Gerudo.StockNum

# Current held items, in the user's canonical progression order. These
# StockNum fields do NOT record historical quests or trades. Previous steps
# are not marked collected just because a later step is being carried.
CHILD_TRADE_STOCKS = (
    ('trade_weird_egg', 'Weird Egg', 0x24596E8E),
    ('trade_wake_up_cucco', 'Wake-Up Cucco', 0xB6CA6093), # Item_AlarmCucco
    ('trade_zelda_letter', "Zelda's Letter", 0x6BE5C3B0),
    ('mask_keaton', 'Keaton Mask', 0xB5C10899),
    ('mask_skull', 'Skull Mask', 0x47B2D775),
    ('mask_spooky', 'Spooky Mask', 0xF046629E),
    ('mask_bunny', 'Bunny Hood', 0x7E607033),
    ('mask_truth', 'Mask of Truth', 0x96459CD3),
)
ADULT_TRADE_STOCKS = (
    ('trade_pocket_egg', 'Pocket Egg', 0x07F10AAF),
    ('trade_pocket_cucco', 'Pocket Cucco', 0x0165E7CA), # Item_PocketCucco
    ('trade_cojiro', 'Cojiro', 0x51D89EF0),
    ('trade_odd_mushroom', 'Odd Mushroom', 0x07DE3E6F),
    ('trade_odd_poultice', 'Odd Poultice', 0x58C9BC78),
    ('trade_poachers_saw', "Poacher's Saw", 0xA862D276),
    ('trade_broken_goron_sword', 'Broken Goron Sword', 0x2815C82A),
    ('trade_prescription', 'Prescription', 0x897CF7EB),
    ('trade_eyeball_frog', 'Eyeball Frog', 0x919EA324),
    ('trade_eye_drops', 'Specialty Eye Drops', 0x5C41A6B1),
    ('trade_claim_check', 'Biggoron Receipt', 0xDED8E8D3),
)


# Direct permanent song acquisition fields supplied from the Switch 2 save
# catalog. These are distinct from NPC conversation or cinematic state flags.
# The catalog's field type (0) is not a saved value: read actual i32 content.
SONG_ISGET_FIELDS = {
    'song_zelda_lullaby': 29491202,       # IsGet.Item_Melody_ZeldaLullaby
    'song_saria': 3556294604,             # IsGet.Item_Melody_SariaSong
    'song_storms': 725212495,             # IsGet.Item_Melody_StormSong
    'song_forest_minuet': 3691645780,     # IsGet.Item_Melody_ForestMinuet
    'song_fire_bolero': 1225005753,       # IsGet.Item_Melody_FireBolero
    'song_water_serenade': 1067858310,    # IsGet.Item_Melody_WaterSerenade
    'song_shadow_nocturne': 3271267828,   # IsGet.Item_Melody_ShadowNocturne
    'song_spirit_requiem': 2150250152,    # IsGet.Item_Melody_SpiritRequiem
}

# Other songs do not necessarily have IsGet.* fields. The following are
# *event evidence candidates*, not equivalent to a direct song inventory flag.
# A saved 1 is positive evidence the obtain-event ran; a zero is NOT proof the
# song has never been learned. Avoid generic HaveTalked / story-time fields.
SONG_GET_EVENT_FIELDS = {
    'song_epona': 3682874359,          # HavePlayedEvent.Dm_MelodyGet_Epona
    'song_sun': 1262575547,           # RoyalFamilyTomb_MelodyGet_Clear (candidate)
    'song_light_prelude': 4017837317, # HavePlayedGetEvent.Item_Melody_LightPrelude
}
# Do NOT interpret Item_Melody_SariaSong_Time (2880902647) as Song of Time.
# A save with Song of Time already learned was observed without that flag set.
# Song of Time is inferred only from explicit Ocarina of Time selection
# in decode_pair; Scarecrow's Song still needs an acquisition field.


def decode_song_progress(table: 'FieldTable') -> tuple[dict[str, bool], dict[str, str]]:
    """Return evidence-backed song statuses; never infer from story progress.

    Direct IsGet values 0/1 establish negative/positive status. For get-event
    records only 1 establishes positive evidence; an absent/zero/invalid event
    does not establish non-ownership. Those entries remain undetermined until
    observed in a real save, and the UI displays them as dimmed.
    """
    flags: dict[str, bool] = {}
    sources: dict[str, str] = {}
    for role, field_hash in SONG_ISGET_FIELDS.items():
        value = table.i32(field_hash)
        if value in (0, 1):
            flags[role] = bool(value)
            sources[role] = f'isget:0x{field_hash:08X}'
    for role, field_hash in SONG_GET_EVENT_FIELDS.items():
        if table.i32(field_hash) == 1:
            flags[role] = True
            sources[role] = f'get_event_candidate:0x{field_hash:08X}'
    return flags, sources


def decode_inventory_stock(table: 'FieldTable') -> tuple[dict[str, bool], dict[str, str]]:
    """Read owned ranged weapons and Gerudo Pass, not ammo as ownership.

    Missing and unsupported values are left unknown, not assigned false.
    This is critical for Slingshot: zero Deku Seeds does not mean unowned.
    """
    flags: dict[str, bool] = {}
    sources: dict[str, str] = {}
    for role, hash_id in {**RANGED_STOCKS, 'gerudo_pass': GERUDO_PASS_STOCK}.items():
        value = table.i32(hash_id)
        if value in (0, 1):
            flags[role] = bool(value)
            sources[role] = f'item_stock:0x{hash_id:08X}'
    return flags, sources


def decode_trade_items(table: 'FieldTable') -> tuple[dict[str, bool], dict[str, str]]:
    """Read ONLY current item stocks, never assume earlier trades are saved.

    A current item can be consumed/traded away; zero is not evidence that a
    corresponding quest step was never completed. The UI presents the most
    advanced positively-held stage, plus Mask of Truth completion evidence.
    """
    flags: dict[str, bool] = {}
    sources: dict[str, str] = {}
    for role, _label, hash_id in CHILD_TRADE_STOCKS + ADULT_TRADE_STOCKS:
        value = table.i32(hash_id)
        if value in (0, 1):
            flags[role] = bool(value)
            sources[role] = f'item_stock:0x{hash_id:08X}'
    return flags, sources



# The equipped equipment is stored separately for Link's two eras.  A weapon
# currently equipped in either era is positive ownership evidence even when
# the corresponding pouch stock is zero (for example, the equipped Hylian
# Shield). Never derive a different weapon from another weapon's equipped name.
EQUIPPED_WEAPON_NAMES = {
    'weapon_sword_kokiri': 'sword_kokiri',
    'weapon_sword_master': 'sword_master',
    'weapon_sword_biggoron': 'sword_biggoron',
    'weapon_shield_deku': 'shield_deku',
    'weapon_shield_hylian': 'shield_hylian',
    'weapon_shield_hylia': 'shield_hylian',
    'armor_shield_hylian': 'shield_hylian',
    'armor_shield_hylia': 'shield_hylian',
    'weapon_shield_mirror': 'shield_mirror',
}


def decode_weapon_ownership(table: 'FieldTable') -> tuple[dict[str, bool], dict[str, str]]:
    """Combine direct pouch stocks and explicit equipped item names.

    Stock > 0 means owned, stock 0 means no item *in that pouch field*.
    A matching item explicitly equipped in either era is also owned, and wins
    over pouch stock 0. Other weapons are not inferred from story progression.
    Missing / out-of-range stock without a matching equipped name stays None
    internally, so missing data remains distinguishable from an actual zero.
    """
    flags: dict[str, bool] = {}
    sources: dict[str, str] = {}
    for role, field_hash in {**SWORD_STOCKS, **SHIELD_STOCKS}.items():
        stock = table.i32(field_hash)
        if stock is not None and 0 <= stock <= 99:
            flags[role] = stock > 0
            sources[role] = f'item_stock:0x{field_hash:08X}'
    for slot_name, field_hash in EQUIPMENT.items():
        item = table.string64(field_hash)
        role = (EQUIPPED_WEAPON_NAMES.get(item.strip().casefold().replace('-', '_'))
                if isinstance(item, str) else None)
        # A sword must come from a sword equip slot and a shield from a shield
        # equip slot; bogus/edited values must not unlock unrelated equipment.
        if role is None or ('sword' in slot_name) != role.startswith('sword_'):
            continue
        if flags.get(role) is not True:
            sources[role] = f'equipped_item:0x{field_hash:08X}'
        flags[role] = True
    return flags, sources

# Switch 2 remake field IDs, resolved against Traiver/OOT-SW2SE's catalog.
# Each stock field stores the CURRENT owned state (0 or 1), independently of
# acquisition history, selected equipment, and story progression.  Only direct
# stock values may drive the save-information Stone/medallion icons.
SPIRITUAL_STONE_STOCKS = {
    'Kokiri Emerald': 0xCBD1E58C,  # Item_Stone_Forest
    'Goron Ruby': 0x7D69AD47,      # Item_Stone_Fire
    'Zora Sapphire': 0xB8E3BF45,   # Item_Stone_Water
}
# (stock field, acquisition history, related flag) from OOT-SW2SE's
# `registrations` array.  The latter two fields are not ownership evidence.
MEDALLION_REGISTRATIONS = {
    'Fire':   (0x5CE254A6, 0x103A0DC7, 0xD4FB9D9D),
    'Forest': (0x692D57CB, 0xFCD8D653, 0x413DCC88),
    'Light':  (0x235C7D9A, 0x9026C187, 0xC9C90DAE),
    'Shadow': (0x204D2063, 0x12D63AC6, 0x1581D060),
    'Spirit': (0x0C7A5FD0, 0xE269DF38, 0x67A9036C),
    'Water':  (0x0151A68F, 0xCF64E780, 0x43EB4899),
}

# Verified against Traiver/OOT-SW2SE's inventory catalog and the user's
# progress.sav. These are CURRENT stock/ownership fields, not history flags.
# The highest owned tier is displayed by progressive_strength in the UI.
STRENGTH_STOCKS = {
    'bracelet_goron': 0xD1317941,  # Armor_Child_Gauntlet_Goron
    'gauntlets_silver': 0x0012B8C0,  # Armor_Adult_Gauntlet_Silver
    'gauntlets_gold': 0x40AE1183,  # Armor_Adult_Gauntlet_Gold
}
# Direct ownership IDs provided from OOT-SW2SE and verified in progress.sav.
# The equipment display must not mistake equipped strings or story progress
# for acquired tunics / the Echo Gem.
TUNIC_STOCKS = {
    'tunic_kokiri': 0x84BA0D24,  # Obj_Armor_Child_Normal
    'tunic_goron': 0x21ECBD19,   # Armor_Adult_Goron
    'tunic_zora': 0xB816AF94,    # Armor_Adult_Zora
}
ECHO_GEM_STOCK = 0xF3B14CE2  # Item_AgonyStone, displayed as Echo Gem

# Ten current stock/ownership fields independently confirmed against OOT-SW2SE's
# equipment catalog (two additional registration hashes are NOT ownership).
# Unrecognized values stay unknown and do not light an icon.
MASK_STOCKS = {
    'mask_amiibo_0': 0xF5E647DA,  # Fairy-Kid Mask
    'mask_amiibo_1': 0x486C59A4,  # Princess Mask
    'mask_bunny': 0x7E607033,     # Bunny Hood
    'mask_gerudo': 0x0E94A8DF,    # Gerudo Mask
    'mask_goron': 0xFC508DA7,     # Goron Mask
    'mask_keaton': 0xB5C10899,    # Keaton Mask
    'mask_skull': 0x47B2D775,     # Skull Mask
    'mask_spooky': 0xF046629E,    # Spooky Mask
    'mask_truth': 0x96459CD3,     # Mask of Truth
    'mask_zora': 0x924E9A46,      # Zora Mask
}


def derived_child_tunic(location: str | None, flags: dict[str, bool]) -> str | None:
    """Child Link's tunic is Kokiri; adult selection requires a real equip flag.

    Adult armor cannot be equipped by Child Link. This is a constrained
    equipment-state derivation, not a generic ownership->equipped guess.
    """
    if (isinstance(location, str) and location.casefold().endswith('_child')
            and flags.get('tunic_kokiri') is True):
        return 'Obj_Armor_Child_Normal'
    return None


def decode_strength_ownership(table: 'FieldTable') -> tuple[dict[str, bool], dict[str, str]]:
    """Read independent strength upgrade stocks; no progression inference."""
    flags: dict[str, bool] = {}
    sources: dict[str, str] = {}
    for role, field_hash in {**STRENGTH_STOCKS, **TUNIC_STOCKS, **MASK_STOCKS}.items():
        value = table.i32(field_hash)
        if value in (0, 1):
            flags[role] = bool(value)
            sources[role] = f'item_stock:0x{field_hash:08X}'
    return flags, sources


def magic_upgrade_level(max_mp: int | float | None) -> int | None:
    """Upgrade slots from total magic capacity, never current MP remaining.

    Zero means magic not acquired; 24 means first magic meter; 48 means
    doubled magic capacity. Unsupported capacities are unknown, not rounded.
    """
    if type(max_mp) not in (int, float):
        return None
    if max_mp == 0:
        return 0
    if max_mp == 24:
        return 1
    if max_mp == 48:
        return 2
    return None


SELECTED_ITEMS = {'ocarina': 0xB72CD3FC, 'quest_stone': 0x0F4E78BC}  # compatibility; quest_stone is never ownership
SELECTED_ITEM_NAMES = {
    'Item_Ocarina_Fairy': ('inventory_flags', 'ocarina_fairy'),
    'Item_Ocarina_Time': ('inventory_flags', 'ocarina_time'),
}


def decode_quest_ownership(table: 'FieldTable') -> tuple[dict, dict, dict, dict]:
    """Return direct, per-item booleans and their source field IDs.

    Missing or non-boolean stock is unknown; do not infer from DoorOfTime,
    acquisition history, the currently highlighted Stone or dungeon progress.
    The game save is read-only and no external field catalog is required.
    """
    stone_flags: dict[str, bool] = {}
    medallion_flags: dict[str, bool] = {}
    stone_sources: dict[str, str] = {}
    medallion_sources: dict[str, str] = {}
    for name, field_hash in SPIRITUAL_STONE_STOCKS.items():
        value = table.i32(field_hash)
        if value in (0, 1):
            stone_flags[name] = bool(value)
            stone_sources[name] = f'item_stock:0x{field_hash:08X}'
    for name, (stock_hash, _acquired_hash, _related_hash) in MEDALLION_REGISTRATIONS.items():
        value = table.i32(stock_hash)
        if value in (0, 1):
            medallion_flags[name] = bool(value)
            medallion_sources[name] = f'item_stock:0x{stock_hash:08X}'
    return stone_flags, medallion_flags, stone_sources, medallion_sources


def decode_selected_items(table: 'FieldTable') -> dict[str, dict[str, bool]]:
    """Selected Ocarina identity only; never interpret the selected Stone as owned."""
    states: dict[str, dict[str, bool]] = {'inventory_flags': {}, 'stone_flags': {}}
    field_hash = SELECTED_ITEMS['ocarina']
    if field_hash in table.fields:
        try:
            name = table.string64(field_hash)
        except UnsupportedSave:
            name = None
        match = SELECTED_ITEM_NAMES.get(name)
        if match is not None:
            category, role = match
            states[category][role] = True
    return states

CAPTION = {
    'day': 0xBD46F485,
    'month': 0xDFD840D3,
    'year': 0x9811A3F7,
    'hour': 0x23F3D75E,
    'minute': 0x27853BF7,
    'autosave': 0x25F03CAA,
    'jpeg': 0x63696A32,
}


class UnsupportedSave(ValueError):
    pass


class FieldTable:
    """Validated, bounded little-endian hash/offset reader."""
    def __init__(self, data: bytes, header_end: int):
        if len(data) < header_end or len(data) > MAX_SAVE_BYTES:
            raise UnsupportedSave('Unexpected save size')
        if struct.unpack_from('<III', data) != (*MAGIC, header_end):
            raise UnsupportedSave('Unsupported save format/version')
        self.data = data
        self.header_end = header_end
        self.fields: dict[int, int] = {}
        for off in range(40, header_end, 8):
            key = struct.unpack_from('<I', data, off)[0]
            if key:
                if key in self.fields:
                    raise UnsupportedSave('Duplicate field identifier')
                self.fields[key] = off + 4

    def i32(self, key: int) -> int | None:
        off = self.fields.get(key)
        return struct.unpack_from('<i', self.data, off)[0] if off is not None else None

    def u32(self, off: int) -> int:
        if off < 0 or off > len(self.data) - 4:
            raise UnsupportedSave('Field points outside file')
        return struct.unpack_from('<I', self.data, off)[0]

    def block(self, key: int, length: int) -> int | None:
        field = self.fields.get(key)
        if field is None:
            return None
        off = self.u32(field)
        if off < self.header_end or length < 0 or off > len(self.data) - length:
            raise UnsupportedSave('Data block outside file')
        return off

    def string64(self, key: int) -> str | None:
        off = self.block(key, 64)
        if off is None:
            return None
        raw = self.data[off:off+64].split(b'\x00', 1)[0]
        value = raw.decode('utf-8', errors='replace').strip()
        return ''.join(c for c in value if c.isprintable())[:64] or None

    def jpeg(self, key: int) -> bytes | None:
        off = self.block(key, 8)
        if off is None:
            return None
        size = self.u32(off)
        if not 4 <= size <= MAX_JPEG_BYTES or off + 4 + size > len(self.data):
            return None
        img = self.data[off+4:off+4+size]
        return img if img.startswith(b'\xff\xd8') and img.endswith(b'\xff\xd9') else None


def plausible(value: int | None, lower: int, upper: int) -> int | None:
    return value if value is not None and lower <= value <= upper else None


def quarter_hearts(value: int | None) -> float | None:
    raw = plausible(value, 0, 80)
    return raw / 4 if raw is not None else None


@dataclass(frozen=True)
class AdventureSlot:
    account: str
    slot: int
    modified: float
    statistics: dict
    saved_in_game: str | None = None
    preview_jpeg: bytes | None = None
    warning: str | None = None


def decode_pair(progress: bytes, caption: bytes | None, *,
                vanilla_story_assumptions: bool = True) -> tuple[dict, str | None, bytes | None]:
    p = FieldTable(progress, HEADER_PROGRESS)
    c = FieldTable(caption, HEADER_CAPTION) if caption is not None else None
    stats = {
        'rupees': plausible(p.i32(PROGRESS['rupees']), 0, 9999),
        'hearts': quarter_hearts(p.i32(PROGRESS['hearts_max'])),
        'current_hearts': quarter_hearts(p.i32(PROGRESS['hearts_current'])),
        'magic': plausible(p.i32(PROGRESS['magic_current']), 0, 480),
        'max_magic': plausible(p.i32(PROGRESS['magic_max']), 0, 480),
        'gold_skulltulas': plausible(p.i32(PROGRESS['skulltulas']), 0, 100),
        'location': p.string64(PROGRESS['location']),
        'progress_id': p.string64(PROGRESS['progress_id']),
    }
    # Absence of a field is not equivalent to owning zero items.
    for key, (field_hash, maximum) in SUPPLIES.items():
        stats[key] = plausible(p.i32(field_hash), 0, maximum)
    for key, (field_hash, maximum) in CAPACITIES.items():
        stats[key] = plausible(p.i32(field_hash), 0, maximum)
    dungeon = {name: plausible(p.i32(field_hash), 0, maximum)
               for name, (field_hash, maximum) in DUNGEON_KEYS.items()}
    stats['dungeon_keys'] = dungeon if any(v is not None for v in dungeon.values()) else None
    bottle_slots = decode_bottle_slots(p)
    stats['bottle_slots'] = bottle_slots
    stats['bottles'] = (sum(slot['owned'] is True for slot in bottle_slots)
                        if all(slot['owned'] is not None for slot in bottle_slots) else None)
    capacity = plausible(p.i32(WALLET['capacity']), 0, 1000)
    wallet_name = p.string64(WALLET['type'])
    known_wallets = {'Item_Wallet_Small': 200, 'Item_Wallet_Medium': 500, 'Item_Wallet_Large': 1000}
    if wallet_name in known_wallets and capacity == known_wallets[wallet_name]:
        stats['wallet'] = {'Item_Wallet_Small': 'Kokiri', 'Item_Wallet_Medium': 'Adult',
                           'Item_Wallet_Large': 'Giant'}[wallet_name]
        stats['wallet_capacity'] = capacity
    else:
        stats['wallet'] = None
        stats['wallet_capacity'] = None
    stats.update(decode_selected_items(p))
    # `vanilla_story_assumptions` is retained only for callers that passed it
    # in older v0.8 builds; it no longer affects item ownership.
    (stats['stone_flags'], stats['medallion_flags'], stats['stone_sources'],
     stats['medallion_sources']) = decode_quest_ownership(p)
    stats['equipped_sword'] = p.string64(EQUIPMENT['sword'])
    stats['equipped_shield'] = p.string64(EQUIPMENT['shield'])
    stats['second_equipped_sword'] = p.string64(EQUIPMENT['second_sword'])
    stats['second_equipped_shield'] = p.string64(EQUIPMENT['second_shield'])
    stats['equipment_flags'], stats['equipment_sources'] = decode_strength_ownership(p)
    weapon_flags, weapon_sources = decode_weapon_ownership(p)
    stats['equipment_flags'].update(weapon_flags)
    stats['equipment_sources'].update(weapon_sources)
    # Slingshot/Bow/Boomerang ownership comes from StockNum, not ammunition.
    inventory_flags, inventory_sources = decode_inventory_stock(p)
    stats['inventory_flags'].update(inventory_flags)
    stats.setdefault('inventory_sources', {}).update(inventory_sources)
    trade_flags, trade_sources = decode_trade_items(p)
    stats['song_flags'], stats['song_sources'] = decode_song_progress(p)
    # In standard progression, receiving the Ocarina of Time also teaches
    # Song of Time. The selected Ocarina item is already decoded above, so a
    # positive Item_Ocarina_Time identity is enough to light this song.
    # Do not infer it from Fairy Ocarina, SariaSong_Time, or story events.
    # This is positive-only evidence; absent/unknown Ocarina information does
    # not establish that Song of Time was never learned.
    if stats['inventory_flags'].get('ocarina_time') is True:
        stats['song_flags']['song_time'] = True
        stats['song_sources']['song_time'] = (
            f'ocarina_time_progression:0x{SELECTED_ITEMS["ocarina"]:08X}'
        )
    stats['trade_flags'] = trade_flags
    stats['trade_sources'] = trade_sources
    # Mask of Truth means the child mask trading sequence is completed. It
    # unlocks the regular mask-shop selection; ownership of those optional
    # masks still depends on their individual stock fields.
    stats['child_trade_complete'] = trade_flags.get('mask_truth') is True
    stats['mask_shop_unlocked'] = stats['child_trade_complete']
    # Availability is separate from stock: purchased/traded masks can be
    # unlocked in the shop without currently being carried. Amiibo-only
    # masks are not inferred from Mask of Truth.
    stats['mask_available_flags'] = {
        role: True for role in MASK_STOCKS
        if role not in ('mask_amiibo_0', 'mask_amiibo_1')
    } if stats['mask_shop_unlocked'] else {}
    stats['equipped_tunic'] = derived_child_tunic(stats['location'], stats['equipment_flags'])
    if stats['equipped_tunic'] is not None:
        stats['equipment_sources']['equipped_tunic'] = 'child_form_only_tunic'
    echo_gem = p.i32(ECHO_GEM_STOCK)
    if echo_gem in (0, 1):
        stats['inventory_flags']['agony_stone'] = bool(echo_gem)
        stats.setdefault('inventory_sources', {})['agony_stone'] = f'item_stock:0x{ECHO_GEM_STOCK:08X}'
    # Remake stores magic in tenths of an MP; these are the same values
    # displayed by the Switch 2 save editor's overview.
    # Some early editor-generated saves preserve unscaled magic values (24/48).
    # The reference editor detects this from the stored maximum.
    legacy_magic = p.i32(PROGRESS['magic_max']) in (24, 48)
    for key in ('magic', 'max_magic'):
        if stats[key] is not None and not legacy_magic:
            stats[key] = stats[key] / 10
    stats['magic_upgrade_level'] = magic_upgrade_level(stats['max_magic'])
    saved = None
    preview = None
    if c is not None:
        vals = [c.i32(CAPTION[x]) for x in ('year', 'month', 'day', 'hour', 'minute')]
        if all(x is not None for x in vals):
            try:
                saved = datetime(*vals).strftime('%Y-%m-%d %H:%M')
            except (ValueError, OverflowError):
                pass
        stats['autosave'] = c.i32(CAPTION['autosave']) == 1 if c.i32(CAPTION['autosave']) is not None else None
        preview = c.jpeg(CAPTION['jpeg'])
    return stats, saved, preview


def _safe_read(path: Path) -> bytes:
    if path.is_symlink() or not path.is_file():
        raise UnsupportedSave('Save file is missing or linked')
    if path.stat().st_size > MAX_SAVE_BYTES:
        raise UnsupportedSave('Save file exceeds size limit')
    return path.read_bytes()


def read_adventure_slots(user_dir: Path) -> list[AdventureSlot]:
    """Best-effort automatic summaries; invalid saves never block loading profiles."""
    base = Path(user_dir) / 'save'
    if not base.is_dir() or base.is_symlink():
        return []
    rows = []
    for account in sorted(base.iterdir()):
        if account.is_symlink() or not account.is_dir():
            continue
        for folder in sorted(account.glob('slot_*')):
            if folder.is_symlink() or not folder.is_dir() or not folder.name[5:].isdigit():
                continue
            slot = int(folder.name[5:])
            if not 0 <= slot <= 99:
                continue
            progress = folder / 'progress.sav'
            if progress.is_symlink() or not progress.is_file():
                continue
            try:
                stamp = progress.stat().st_mtime
                caption = folder / 'caption.sav'
                if caption.is_file() and not caption.is_symlink():
                    stamp = max(stamp, caption.stat().st_mtime)
                stats, saved, preview = decode_pair(_safe_read(progress), _safe_read(caption) if caption.is_file() else None)
                rows.append(AdventureSlot(account.name, slot, stamp, stats, saved, preview))
            except (OSError, ValueError, UnsupportedSave) as exc:
                rows.append(AdventureSlot(account.name, slot, 0, {}, warning=str(exc)))
    return sorted(rows, key=lambda s: s.modified, reverse=True)
