"""Human-readable Adventure v0.8 save labels.

Pure formatting only: caller supplies decoded save data.  This module never
reads save files or guesses Randomizer status from a profile's name.
"""
from __future__ import annotations

import re
from collections.abc import Mapping

# Ocarina of Time game scene aliases, for display only; do not change save IDs.
_SCENES = {
    'zorariver': 'Zora River',
    'zorasriver': 'Zora River',
    'zorasdomain': "Zora's Domain",
    'zoradomain': "Zora's Domain",
    'zorafountain': "Zora's Fountain",
    'hyrulefield': 'Hyrule Field',
    'hyrulecastle': 'Hyrule Castle',
    'hyrulecastletown': 'Hyrule Castle Town',
    'hyrulemarket': 'Hyrule Market',
    'market': 'Market',
    'kakarikovillage': 'Kakariko Village',
    'kokiriforest': 'Kokiri Forest',
    'lostwoods': 'Lost Woods',
    'sacredforestmeadow': 'Sacred Forest Meadow',
    'deku tree': 'Deku Tree',
    'degutree': 'Deku Tree',
    'thedeku tree': 'The Deku Tree',
    'greatdekutree': 'Great Deku Tree',
    'dekutree': 'Deku Tree',
    'dodongoscavern': "Dodongo's Cavern",
    'jabujabusbelly': "Jabu-Jabu's Belly",
    'deathmountaintrail': 'Death Mountain Trail',
    'deathmountaincrater': 'Death Mountain Crater',
    'goroncity': 'Goron City',
    'lonlonranch': 'Lon Lon Ranch',
    'lakehylia': 'Lake Hylia',
    'gerudovalley': 'Gerudo Valley',
    'gerudofortress': 'Gerudo Fortress',
    'hauntedwasteland': 'Haunted Wasteland',
    'desertcolossus': 'Desert Colossus',
    'templeoftime': 'Temple of Time',
    'foresttemple': 'Forest Temple',
    'firetemple': 'Fire Temple',
    'watertemple': 'Water Temple',
    'shadowtemple': 'Shadow Temple',
    'spirittemple': 'Spirit Temple',
    'bottomofthewell': 'Bottom of the Well',
    'icecavern': 'Ice Cavern',
    'ganonscastle': "Ganon's Castle",
    'ganonstower': "Ganon's Tower",
    'graveyard': 'Graveyard',
}

_ITEM_NAMES = {
    'sword_kokiri': 'Kokiri Sword',
    'sword_master': 'Master Sword',
    'sword_biggoron': 'Biggoron Sword',
    'shield_deku': 'Deku Shield',
    'shield_hylian': 'Hylian Shield',
    'shield_mirror': 'Mirror Shield',
    'weapon_sword_kokiri': 'Kokiri Sword',
    'weapon_sword_master': 'Master Sword',
    'weapon_sword_biggoron': 'Biggoron Sword',
    'weapon_shield_deku': 'Deku Shield',
    'weapon_shield_hylian': 'Hylian Shield',
    'weapon_shield_mirror': 'Mirror Shield',
    'armor_shield_hylia': 'Hylian Shield',
    'armor_shield_hylian': 'Hylian Shield',
    'weapon_shield_hylia': 'Hylian Shield',
    'weapon_hammer': 'Megaton Hammer',
    'hammer': 'Megaton Hammer',
    'megaton_hammer': 'Megaton Hammer',
    'weapon_hammer_megaton': 'Megaton Hammer',
    'armor_adult_normal': 'Kokiri Tunic',
    'obj_armor_child_normal': 'Kokiri Tunic',
    'armor_adult_goron': 'Goron Tunic',
    'armor_adult_zora': 'Zora Tunic',
    'tunic_kokiri': 'Kokiri Tunic',
    'tunic_goron': 'Goron Tunic',
    'tunic_zora': 'Zora Tunic',
}


def _text(value):
    return str(value or '').strip()


def _scene_key(value):
    return re.sub(r'[^a-z0-9]', '', value.casefold())


def readable_scene(location):
    """Expand game identifiers like 'ZoraRiver' or 'Zora_River' safely."""
    if location is None:
        return 'Unknown Location'
    value = _text(location)
    if not value or value.casefold() in ('unknown', 'none', 'null'):
        return 'Unknown Location'
    # Scene_... and ..._Scene are presentation wrappers, not game locations.
    value = re.sub(r'^scene[_\s-]+', '', value, flags=re.I)
    value = re.sub(r'[_\s-]+scene$', '', value, flags=re.I)
    compact = _scene_key(value)
    if compact in _SCENES:
        return _SCENES[compact]
    # Camel-case split including ABCCase, then underscores/hyphens, retaining digits.
    value = re.sub(r'([A-Z]+)([A-Z][a-z])', r'\1 \2', value)
    value = re.sub(r'([a-z0-9])([A-Z])', r'\1 \2', value)
    value = re.sub(r'[_\-]+', ' ', value)
    return ' '.join(value.split()) or 'Unknown Location'


def readable_age(age):
    """Return Child/Adult only for explicit age names, not numeric guesses."""
    if age is None or isinstance(age, bool):
        return ''
    v = _scene_key(_text(age))
    if v in ('child', 'linkchild', 'agechild', 'younglink', 'young'):
        return 'Child'
    if v in ('adult', 'linkadult', 'ageadult', 'adultlink'):
        return 'Adult'
    return ''


def format_save_location(location, age=None):
    """Format e.g. ('ZoraRiver', 'Child') -> 'Child: Zora River'."""
    if isinstance(location, Mapping):
        data = location
        if age is None:
            age = next((data.get(k) for k in ('age', 'link_age', 'player_age') if data.get(k) is not None), None)
        location = next((data.get(k) for k in ('location', 'location_name', 'scene_name', 'scene', 'area') if data.get(k) is not None), None)
    location = _text(location)
    # Some parsers encode an age in the name. Explicit age always wins.
    if not readable_age(age):
        match = re.match(r'^(Child|Adult)[_\s-]+(.+)$', location, flags=re.I)
        if match:
            age, location = match.groups()
        else:
            match = re.match(r'^(.+)[_\s-]+(Child|Adult)$', location, flags=re.I)
            if match:
                location, age = match.groups()
    area = readable_scene(location)
    label = readable_age(age)
    return f'{label}: {area}' if label else area



def save_header_location_age(location, age=None) -> tuple[str, str]:
    """Read-only labels for the selected-adventure header.

    Location and age are displayed separately; a recognized age prefix or
    suffix inside the game location is not repeated. Unrecognized values stay
    unknown rather than being guessed from the player's profile name.
    """
    if isinstance(location, Mapping):
        data = location
        if age is None:
            age = next((data.get(k) for k in ('age', 'link_age', 'player_age')
                        if data.get(k) is not None), None)
        location = next((data.get(k) for k in
                         ('location', 'location_name', 'scene_name', 'scene', 'area')
                         if data.get(k) is not None), None)
    raw = _text(location)
    embedded_age = ''
    prefix = re.match(r'^(Child|Adult)(?:\s*:\s*|[\s_-]+)(.+)$', raw, flags=re.I)
    suffix = re.match(r'^(.+?)(?:\s*:\s*|[\s_-]+)(Child|Adult)$', raw, flags=re.I)
    if prefix:
        embedded_age = readable_age(prefix.group(1))
        raw = prefix.group(2)
    elif suffix:
        embedded_age = readable_age(suffix.group(2))
        raw = suffix.group(1)
    return readable_scene(raw), (readable_age(age) or embedded_age or 'Unknown')


def readable_equipment(value):
    """Remove engine prefixes while retaining stable JSON role names."""
    raw = _text(value)
    if not raw:
        return ''
    key = re.sub(r'[^a-z0-9]+', '_', raw.casefold()).strip('_')
    if key in _ITEM_NAMES:
        return _ITEM_NAMES[key]
    stripped = re.sub(r'^(?:weapon_)?(sword|shield)_', '', key)
    if stripped in ('kokiri', 'master', 'biggoron') and 'sword' in key:
        return _ITEM_NAMES.get('sword_' + stripped, raw)
    if stripped in ('deku', 'hylian', 'hylia', 'mirror') and 'shield' in key:
        return _ITEM_NAMES.get('shield_' + ('hylian' if stripped == 'hylia' else stripped), raw)
    if 'hammer' in key and ('megaton' in key or key in ('hammer', 'weapon_hammer')):
        return 'Megaton Hammer'
    return raw.replace('_', ' ')


def save_kind(data):
    """Display the explicitly assigned save classification, never guessed metadata.

    Game-side randomization cannot be determined from a planned seed or an
    arbitrary boolean in progress.sav. The user can change this label in Manage.
    """
    label = data.get('save_label') if isinstance(data, Mapping) else None
    return 'Rando' if label == 'rando' else 'Vanilla'


def save_kind_badge_text(data):
    return {'Rando': 'Rando Save', 'Vanilla': 'Vanilla Save'}[save_kind(data)]


def make_save_kind_badge(data, parent=None):
    """Create a compact grey QLabel, to place ABOVE each Select button."""
    from PyQt6.QtCore import Qt
    from PyQt6.QtWidgets import QLabel
    label = QLabel(save_kind_badge_text(data), parent)
    label.setAlignment(Qt.AlignmentFlag.AlignCenter)
    label.setFixedHeight(22)
    label.setMinimumWidth(94)
    label.setStyleSheet('QLabel { color: #dedce2; background: #45434f; border: 1px solid #666370; border-radius: 5px; padding: 1px 8px; font-size: 11px; font-weight: 600; }')
    label.setToolTip('Save type is your explicit profile label; it does not enable randomizer gameplay.')
    return label
