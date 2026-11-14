"""Versioned randomizer *configuration* only.

Hookshot game-side item interception is not yet implemented. These settings must
never be represented as affecting gameplay until a compatible native adapter exists.
The format is intentionally JSON to allow later C++ consumers.
"""
from __future__ import annotations
from dataclasses import dataclass, asdict
from hashlib import sha256
import json
import re
import secrets

CONFIG_VERSION = 1
ENGINE_STATUS = "configuration_only"
HEX_SEED = re.compile(r"^[0-9a-fA-F]{1,16}$")


def generate_seed() -> str:
    return f"{secrets.randbits(64):016X}"


def normalized_seed(seed: str) -> str:
    s = seed.strip()
    if not s:
        raise ValueError("Enter a seed or generate one first.")
    if HEX_SEED.fullmatch(s):
        return s.upper().zfill(16)
    if len(s) > 64 or any(ord(c) < 32 for c in s):
        raise ValueError("Seed text must be 1–64 printable characters.")
    # Consistent fixed-length representation regardless of Python hash salt.
    return sha256(s.encode("utf-8")).hexdigest()[:16].upper()


@dataclass(frozen=True)
class Settings:
    shuffle_chests: bool = True
    shuffle_npc_rewards: bool = True
    shuffle_heart_pieces: bool = True
    shuffle_songs: bool = True
    shuffle_shops: bool = False
    shuffle_skulltulas: bool = False
    # Entrances deliberately absent in initial scope.

    @classmethod
    def from_dict(cls, data: dict) -> "Settings":
        allowed = cls.__dataclass_fields__.keys()
        unknown = set(data) - set(allowed)
        if unknown:
            raise ValueError("Unknown randomizer setting(s): " + ", ".join(sorted(unknown)))
        for key, value in data.items():
            if type(value) is not bool:
                raise ValueError(f"Randomizer setting {key} must be true or false.")
        return cls(**data)


def make_manifest(seed: str, settings: Settings | None = None) -> dict:
    canonical = normalized_seed(seed)
    options = asdict(settings or Settings())
    hashed = json.dumps({"seed": canonical, "settings": options}, sort_keys=True, separators=(",", ":"))
    return {
        "schema_version": CONFIG_VERSION,
        "engine_status": ENGINE_STATUS,
        "seed": canonical,
        "settings": options,
        "settings_hash": sha256(hashed.encode("utf-8")).hexdigest()[:12].upper(),
        "gameplay_enabled": False,
        "notes": "Configuration preview only. No game item placements or hooks installed.",
    }


def validate_manifest(data: dict) -> dict:
    if data.get("schema_version") != CONFIG_VERSION:
        raise ValueError("Unsupported randomizer manifest version.")
    if data.get("gameplay_enabled") is not False:
        raise ValueError("Unsupported game-side manifest: this version does not randomize gameplay.")
    return make_manifest(data.get("seed", ""), Settings.from_dict(data.get("settings", {})))
