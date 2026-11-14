"""Typed, read-only model for Zelda-style save summaries.

No assumptions about missing fields: None means the save did not provide a
verified value, *not* uncollected. This module has no Qt dependencies.
"""
from __future__ import annotations

from dataclasses import dataclass
from math import isfinite

STONES = ('Kokiri Emerald', 'Goron Ruby', 'Zora Sapphire')
MEDALLIONS = ('Forest', 'Fire', 'Water', 'Spirit', 'Shadow', 'Light')


@dataclass(frozen=True)
class SaveVisual:
    hearts_max: float | None
    hearts_current: float | None
    double_defense: bool | None
    rupees: int | None
    rupee_capacity: int | None
    wallet_tier: str | None
    skulltulas: int | None
    bottles: int | None
    stones: tuple[bool | None, ...]
    medallions: tuple[bool | None, ...]
    equipped_sword: str | None
    equipped_shield: str | None
    magic_level: int | None = None


def valid_number(value, low, high):
    if isinstance(value, bool) or not isinstance(value, (int, float)):
        return None
    try:
        return float(value) if isfinite(value) and low <= value <= high else None
    except (OverflowError, ValueError):
        return None


def valid_int(value, low, high):
    number = valid_number(value, low, high)
    return int(number) if number is not None and int(number) == number else None


def tri_flags(value, names):
    """Only explicit per-item booleans are accepted; never infer item identities from a count."""
    if not isinstance(value, dict):
        return (None,) * len(names)
    return tuple(value.get(name) if type(value.get(name)) is bool else None for name in names)


def magic_upgrade_state(stats: dict | None) -> int | None:
    """Return 0, 1 or 2 upgrade segments, never the current-MP fraction.

    The decoder provides the authoritative upgrade tier, but legacy callers
    may supply only an already-normalized capacity (0/24/48 MP). Missing or
    invalid fields stay unknown and therefore leave the meter unfilled.
    """
    if not isinstance(stats, dict):
        return None
    tier = valid_int(stats.get('magic_upgrade_level'), 0, 2)
    if tier is not None:
        return tier
    capacity = valid_int(stats.get('max_magic'), 0, 48)
    return {0: 0, 24: 1, 48: 2}.get(capacity)


def visual_from_stats(stats: dict | None) -> SaveVisual:
    stats = stats if isinstance(stats, dict) else {}
    max_health = valid_number(stats.get('hearts'), 0, 20)
    health = valid_number(stats.get('current_hearts'), 0, 20)
    if max_health is not None and health is not None:
        health = min(health, max_health)
    wallet = stats.get('wallet')
    capacity = valid_int(stats.get('wallet_capacity'), 1, 9999)
    if wallet not in ('Kokiri', 'Adult', 'Giant'):
        wallet = None
    if wallet is None and capacity in (200, 500, 1000):
        wallet = {200: 'Kokiri', 500: 'Adult', 1000: 'Giant'}[capacity]
    defense = stats.get('double_defense')
    return SaveVisual(
        max_health, health, defense if type(defense) is bool else None,
        valid_int(stats.get('rupees'), 0, 9999), capacity, wallet,
        valid_int(stats.get('gold_skulltulas'), 0, 100),
        valid_int(stats.get('bottles'), 0, 5),
        tri_flags(stats.get('stone_flags'), STONES),
        tri_flags(stats.get('medallion_flags'), MEDALLIONS),
        stats.get('equipped_sword') if isinstance(stats.get('equipped_sword'), str) else None,
        stats.get('equipped_shield') if isinstance(stats.get('equipped_shield'), str) else None,
        magic_upgrade_state(stats),
    )


def heart_state(data: SaveVisual, index: int) -> tuple[str, float]:
    """One of 'unknown', 'uncollected', 'owned-empty', 'owned-filled' and fill fraction."""
    if not 0 <= index < 20:
        raise ValueError('Heart index must be within the 2x10 display')
    if data.hearts_max is None:
        return 'unknown', 0.0
    if index >= data.hearts_max:
        return 'uncollected', 0.0
    if data.hearts_current is None:
        return 'owned-empty', 0.0
    fraction = min(1.0, max(0.0, data.hearts_current - index))
    return ('owned-filled' if fraction > 0 else 'owned-empty'), fraction


def wallet_visual(data: SaveVisual) -> str:
    return {'Kokiri': 'green', 'Adult': 'purple', 'Giant': 'gold'}.get(data.wallet_tier, 'unknown')


def rupees_full(data: SaveVisual) -> bool:
    return (data.rupees is not None and data.rupee_capacity is not None
            and data.rupees >= data.rupee_capacity)


def heart_count_text(data: SaveVisual) -> str:
    if data.hearts_max is None:
        return '—'
    return str(int(data.hearts_max)) if float(data.hearts_max).is_integer() else f'{data.hearts_max:g}'


def rupee_text(data: SaveVisual) -> str:
    now = str(data.rupees) if data.rupees is not None else '—'
    cap = str(data.rupee_capacity) if data.rupee_capacity is not None else '—'
    return f'{now}/{cap}'
