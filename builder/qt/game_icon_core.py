"""Hookshot icon manifest authoring and in-memory BNTX decoding.

Reads local extracted RomFS via nx2/prompt_assets.py. Only the JSON manifest is
persisted by the authoring tool; no game images are written to disk.
"""
from __future__ import annotations

import copy
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import re
import sys
import tempfile
import threading
from dataclasses import dataclass
from collections import OrderedDict

SCHEMA_VERSION = 1
GAME_VERSION = "2.1.0"
ROLES = {
    "Health": ["heart_full", "heart_empty", "heart_quarter", "heart_half", "heart_three_quarters", "heart_double_defense", "heart_container", "heart_piece"],
    "Currency": ["rupee_green", "rupee_blue", "rupee_red", "rupee_purple", "rupee_gold"],
    "Quest": ["stone_kokiri", "stone_goron", "stone_zora", "medallion_light", "medallion_forest", "medallion_fire", "medallion_water", "medallion_shadow", "medallion_spirit", "gold_skulltula"],
    "Inventory": ["bottle_empty", "bottle_fairy", "bottle_milk", "bottle_milk_half", "bottle_letter", "bottle_red_potion", "bottle_green_potion", "bottle_blue_potion", "bottle_bugs", "bottle_fish", "bottle_blue_fire", "bottle_poe", "bottle_big_poe", "ocarina_fairy", "ocarina_time", "bomb", "bombchu", "bow", "hookshot", "longshot", "boomerang", "slingshot", "deku_sticks", "deku_nuts", "deku_seeds", "magic_bean", "business_scrub_seed", "lens_of_truth", "agony_stone", "fire_arrow", "ice_arrow", "light_arrow", "dins_fire", "farores_wind", "nayrus_love"],
    "Equipment": ["sword_kokiri", "sword_master", "sword_biggoron", "shield_deku", "shield_hylian", "shield_mirror", "tunic_kokiri", "tunic_goron", "tunic_zora", "boots_iron", "boots_hover", "bracelet_goron", "gauntlets_silver", "gauntlets_gold", "hammer", "scale_silver", "scale_gold", "wallet_child", "wallet_adult", "wallet_giant"],
    "Trade Quests": ["gerudo_pass", "trade_weird_egg", "trade_wake_up_cucco", "trade_zelda_letter", "trade_pocket_egg", "trade_pocket_cucco", "trade_cojiro", "trade_odd_mushroom", "trade_odd_poultice", "trade_poachers_saw", "trade_broken_goron_sword", "trade_prescription", "trade_eyeball_frog", "trade_eye_drops", "trade_claim_check"],
    "Masks": ["mask_keaton", "mask_skull", "mask_spooky", "mask_bunny", "mask_truth", "mask_goron", "mask_zora", "mask_gerudo", "mask_amiibo_0", "mask_amiibo_1"],
    "Songs": ["song_zelda_lullaby", "song_epona", "song_saria", "song_sun", "song_time", "song_storms", "song_scarecrow", "song_forest_minuet", "song_fire_bolero", "song_water_serenade", "song_shadow_nocturne", "song_spirit_requiem", "song_light_prelude"],
    # Five independently decoded equipment capacities with three artwork slots
    # each. These are optional mapping placeholders, NOT newly claimed game
    # textures. The old 88 verified mappings remain intact; no ROM art ships.
    "Bag Upgrades": [f"bag_{kind}_{tier}" for kind in
                     ("sticks", "nuts", "seeds", "quiver", "bombs")
                     for tier in (1, 2, 3)],
}
# Double Defense uses a genuine game heart-outline texture. Keep a distinct
# role so the released launcher can composite the user-verified outline over
# the existing full/partial heart in memory. Never synthesize a white stroke
# instead of reading an available mapped BNTX texture. The exact archive and
# internal texture name still require visual verification on the user's RomFS.
# Silver Rupee is not a currency save icon; the unspecified partial heart role
# was replaced by 1/4, 1/2 and 3/4 roles. Kokiri Boots are default equipment.
# Loading/saving a legacy manifest never drops user-authored custom mappings.
DEFAULT_EQUIPMENT_NO_ICON = frozenset({'boots_kokiri'})
LEGACY_INACTIVE_ROLES = frozenset({'boots_kokiri', 'rupee_silver',
                                   'heart_partial'})
ROLE_SET = {x for roles in ROLES.values() for x in roles}
# The generic `skulltula` is not a distinct game icon role.  Old manifests
# might contain a stale mapping under that exact name.  Retire only that role;
# the verified `gold_skulltula` icon and `mask_skull` must always be retained.
RETIRED_ICON_ROLES = frozenset({"skulltula"})

def strip_retired_icons(doc):
    for role in RETIRED_ICON_ROLES:
        doc.get("icons", {}).pop(role, None)
    return doc
ROLE_RE = re.compile(r"^[a-z][a-z0-9_]{0,79}$")
ARCHIVE_RE = re.compile(r"\.bntx(?:\.zs)?$", re.I)
TEXTURE_MAX_PIXELS = 64_000_000

class IconError(ValueError):
    pass


def default_manifest():
    return {"schema_version": SCHEMA_VERSION, "game_version": GAME_VERSION,
            "icons": {role: {"archive": None, "texture": None, "crop": None,
                             "verified": False} for role in sorted(ROLE_SET)}}


def validate_crop(crop, size):
    """Return (x,y,w,h); crop is in source pixel coordinates, never GUI coordinates."""
    if crop is None:
        return None
    if not isinstance(crop, (list, tuple)) or len(crop) != 4 or any(type(v) is not int for v in crop):
        raise IconError("Crop must be four integer coordinates [x, y, width, height].")
    x, y, w, h = crop
    sw, sh = size
    if min(x, y) < 0 or min(w, h) <= 0 or x + w > sw or y + h > sh:
        raise IconError(f"Crop {crop} exceeds texture bounds {sw}x{sh}.")
    return x, y, w, h


def safe_rel(path):
    if not isinstance(path, str) or not path or "\\" in path:
        raise IconError("Path must be a nonempty POSIX relative path.")
    p = Path(path)
    if p.is_absolute() or any(x in ("", ".", "..") for x in p.parts) or p.drive:
        raise IconError(f"Unsafe relative path: {path!r}")
    return path


def validate_manifest(doc):
    if not isinstance(doc, dict) or doc.get("schema_version") != SCHEMA_VERSION or not isinstance(doc.get("icons"), dict):
        raise IconError("Manifest requires schema_version=1 and an icons object.")
    if not isinstance(doc.get("game_version"), str):
        raise IconError("game_version must be a string.")
    for role, entry in doc["icons"].items():
        if not isinstance(role, str) or not ROLE_RE.fullmatch(role) or not isinstance(entry, dict):
            raise IconError(f"Invalid role or mapping: {role!r}")
        archive, name, crop = (entry.get(k) for k in ("archive", "texture", "crop"))
        if (archive is None) != (name is None):
            raise IconError(f"{role}: archive and texture must both be set or both null.")
        if archive is None:
            if crop is not None:
                raise IconError(f"{role}: unmapped icons cannot have crop.")
        else:
            safe_rel(archive)
            if not ARCHIVE_RE.search(archive) or not isinstance(name, str) or not name.strip():
                raise IconError(f"{role}: invalid archive or texture.")
            if crop is not None and (not isinstance(crop, list) or len(crop) != 4 or any(type(v) is not int for v in crop)):
                raise IconError(f"{role}: crop must be [x,y,w,h].")
            if crop is not None and (min(crop) < 0 or crop[2] <= 0 or crop[3] <= 0):
                raise IconError(f"{role}: invalid crop dimensions.")
            # Legacy `output` is intentionally ignored: no image asset is exported.
        if "verified" in entry and type(entry["verified"]) is not bool:
            raise IconError(f"{role}: verified must be Boolean.")
        if "alpha_mode" in entry and entry["alpha_mode"] not in ("auto", "opaque", "mask"):
            raise IconError(f"{role}: invalid alpha_mode.")
    return doc


def load_manifest(path):
    path = Path(path)
    if not path.exists():
        return default_manifest()
    with path.open("r", encoding="utf-8") as f:
        doc = json.load(f)
    validate_manifest(doc)
    strip_legacy_outputs(doc)
    strip_retired_icons(doc)
    # Older manifests predate some of the save-screen roles. Add only missing
    # placeholders in memory; never replace an existing mapping/custom field.
    for role, empty in default_manifest()["icons"].items():
        doc["icons"].setdefault(role, empty)
    return doc


def atomic_bytes(path, content):
    path = Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    fd, temp = tempfile.mkstemp(prefix=".hookshot-", suffix=".tmp", dir=str(path.parent))
    try:
        with os.fdopen(fd, "wb") as f:
            f.write(content)
            f.flush()
            os.fsync(f.fileno())
        os.replace(temp, path)
        try:
            dfd = os.open(str(path.parent), os.O_RDONLY)
            try:
                os.fsync(dfd)
            finally:
                os.close(dfd)
        except OSError:
            pass
    finally:
        if os.path.exists(temp):
            os.unlink(temp)


def strip_legacy_outputs(doc):
    """Discard obsolete PNG output paths; preserve all other custom fields.

    Old browser JSONs are valid import inputs. This is an in-memory migration;
    only explicit Save JSON / Save Mapping writes the normalized manifest.
    """
    for entry in doc.get("icons", {}).values():
        if isinstance(entry, dict):
            entry.pop("output", None)
    return doc


def save_manifest(path, doc):
    validate_manifest(doc)
    normalized = strip_retired_icons(strip_legacy_outputs(copy.deepcopy(doc)))
    atomic_bytes(path, (json.dumps(normalized, indent=2, ensure_ascii=False, sort_keys=True) + "\n").encode("utf-8"))


def assign(doc, role, archive, texture, crop=None, verified=False, notes="", alpha_mode="auto", size=None, overwrite=False):
    if not ROLE_RE.fullmatch(role):
        raise IconError("Custom role must be lowercase_with_underscores (max 80 chars).")
    safe_rel(archive)
    if not ARCHIVE_RE.search(archive) or not isinstance(texture, str) or not texture.strip():
        raise IconError("Invalid source archive or texture name.")
    if size is not None:
        crop = validate_crop(crop, size)
    if crop is not None:
        crop = list(crop)
    validate_manifest(doc)
    previous = doc["icons"].get(role)
    if previous and previous.get("archive") and not overwrite and (
            previous.get("archive"), previous.get("texture"), previous.get("crop")) != (archive, texture, crop):
        raise IconError(f"Role {role} is mapped already. Confirm replacement before overwriting.")
    entry = copy.deepcopy(previous) if previous else {}
    entry.pop("output", None)
    entry.update({"archive": archive, "texture": texture, "crop": crop,
                  "verified": bool(verified), "alpha_mode": alpha_mode, "notes": notes})
    doc["icons"][role] = entry
    validate_manifest(doc)
    return entry


def unassign(doc, role):
    old = doc["icons"].get(role, {})
    entry = {k: copy.deepcopy(v) for k, v in old.items() if k not in
             ("archive", "texture", "crop", "output", "verified", "alpha_mode", "notes")}
    entry.update({"archive": None, "texture": None, "crop": None, "verified": False})
    doc["icons"][role] = entry


def merge_manifest(current, incoming, replace=False):
    validate_manifest(current)
    validate_manifest(incoming)
    combined = copy.deepcopy(incoming) if replace else copy.deepcopy(current)
    if not replace:
        for k, v in incoming.items():
            if k != "icons" and k not in ("schema_version", "game_version") and k not in combined:
                combined[k] = copy.deepcopy(v)
        for role, value in incoming["icons"].items():
            old = combined["icons"].get(role)
            if old and old.get("archive") and value.get("archive") and (
                old.get("archive"), old.get("texture"), old.get("crop")) != (
                value.get("archive"), value.get("texture"), value.get("crop")):
                raise IconError(f"Conflicting role in imported manifest: {role}. Choose Replace All to override.")
            if not old or value.get("archive"):
                # Keep existing extension fields that the imported file does not know about.
                combined["icons"][role] = {**copy.deepcopy(old or {}), **copy.deepcopy(value)}
    strip_legacy_outputs(combined)
    strip_retired_icons(combined)
    return validate_manifest(combined)


def detect_romfs(source):
    """Locate the actual extracted game assets, not the repo's Python source."""
    base = Path(source).expanduser().resolve()
    choices = (base, base / "romfs", base / ".game" / "romfs", base / "game" / "romfs")
    for path in choices:
        ui = path / 'UI'
        has_icons = (ui / 'Tex' / 'Icon').is_dir() or (ui / 'Tex' / 'IconSh').is_dir()
        if (has_icons or (ui / 'Layout2Archive').is_dir()) and (path / 'Pack' / 'ZsDic.pack.zs').is_file():
            return path
    raise IconError(f"Cannot find extracted RomFS at {base}; need UI/Tex/Icon, UI/Tex/IconSh or UI/Layout2Archive and Pack/ZsDic.pack.zs.")


def _normalized_folder(part):
    return re.sub(r"[^a-z0-9]", "", part.casefold())


def is_unwanted_source(path):
    """Exclude specific content folders, NOT icons whose filename says 'Map'.

    Only a directory segment counts. `Item_Dungeon_Map.bntx.zs` inside Icon
    and `MapIconCollectSkullSign` inside the Common archive remain available.
    """
    folders = Path(path).parts[:-1]
    return any(_normalized_folder(p) in {"bossname", "map", "boss"}
               for p in folders)


def archive_family(archive):
    """Classify all local BNTX sources, keeping model/quest texture visibility."""
    folder = Path(archive).parent.as_posix()
    if folder == "UI/Tex/Icon" or folder.startswith("UI/Tex/Icon/"):
        return "Inventory icons"
    if folder == "UI/Tex/IconSh" or folder.startswith("UI/Tex/IconSh/"):
        return "IconSh (secondary)"
    if folder == "UI/Layout2Archive" or folder.startswith("UI/Layout2Archive/"):
        return "Layout textures"
    parts = [_normalized_folder(part) for part in Path(archive).parts[:-1]]
    if any(part in {"quest", "model", "models", "actor", "actors", "object", "objects", "item", "items", "equipment"}
           for part in parts):
        return "Quest / model textures"
    if str(archive).startswith("UI/"):
        return "Other UI textures"
    return "Other game textures"


def find_archives(romfs):
    """Discover every unpacked BNTX in local RomFS, except unwanted folders.

    Quest/model archives outside UI are also included. Only filenames are
    scanned; decompression and large images remain lazy and are read-only.
    Symlink directories and files are always skipped.
    """
    root = Path(romfs).resolve()
    if not root.is_dir():
        return []
    result = []
    for directory, dirs, files in os.walk(root, followlinks=False):
        parent = Path(directory)
        dirs[:] = [name for name in dirs if not (parent / name).is_symlink()
                   and not is_unwanted_source((parent / name / 'placeholder').relative_to(root))]
        for name in files:
            if not ARCHIVE_RE.search(name):
                continue
            path = parent / name
            rel = path.relative_to(root)
            if is_unwanted_source(rel) or not path.is_file() or path.is_symlink() or not path.resolve().is_relative_to(root):
                continue
            result.append(rel.as_posix())
    order = {"Inventory icons": 0, "IconSh (secondary)": 1,
             "Layout textures": 2, "Quest / model textures": 3,
             "Other UI textures": 4, "Other game textures": 5}
    return sorted(result, key=lambda name: (order.get(archive_family(name), 9), name.casefold()))


def ensure_inside(root, rel, required=False):
    safe_rel(rel)
    base = Path(root).resolve()
    path = (base / rel).resolve()
    if not path.is_relative_to(base) or (required and not path.is_file()):
        raise IconError(f"Missing or unsafe file: {rel}")
    return path


def image_rgba(im, alpha_mode="auto"):
    """Preserve RGBA; BC4 may be white alpha mask or opaque grayscale on choice."""
    from PIL import Image
    if alpha_mode not in ("auto", "opaque", "mask"):
        raise IconError("Unsupported alpha mode")
    if im.mode == "L":
        if alpha_mode == "mask":
            out = Image.new("RGBA", im.size, (255, 255, 255, 0))
            out.putalpha(im)
            return out
        return im.convert("RGBA")
    return im.convert("RGBA")


def crop_image(image, crop):
    rect = validate_crop(crop, image.size)
    if rect is None:
        return image.copy()
    x, y, w, h = rect
    return image.crop((x, y, x+w, y+h))


def image_pixels(im):
    """RGBA pixels for displaying in memory via QImage; never creates PNGs."""
    rgba = im.convert("RGBA")
    return rgba.width, rgba.height, rgba.tobytes()


@dataclass(frozen=True)
class TextureInfo:
    archive: str
    name: str
    width: int
    height: int
    fmt: int

    @property
    def category(self):
        n = (self.name + " " + Path(self.archive).stem).lower()
        if any(k in n for k in ("heart", "life", "hp_")): return "Health"
        if "rupee" in n and "silver" in n: return "Quest"
        if "rupee" in n or "wallet" in n: return "Currency"
        if any(k in n for k in ("medal", "stone", "skull", "quest", "collect", "spiritual")): return "Quest"
        if "mask" in n: return "Masks"
        if any(k in n for k in ("sword", "shield", "tunic", "boots", "equip", "gear", "armor", "menubtn")): return "Equipment"
        if any(k in n for k in ("bottle", "inventory", "item", "ocarina", "menu", "datafile")): return "Inventory"
        return "Other"


class GameAssets:
    """Read-only source backed by Hookshot's own decoder; cache compressed archives, not full PIL images."""
    def __init__(self, hookshot_root, romfs_root, cache_dir=None):
        self.hookshot_root = Path(hookshot_root).expanduser().resolve()
        self.romfs = detect_romfs(romfs_root)
        self.cache = Path(cache_dir or Path.home()/".cache"/"hookshot-icon-browser")
        self._lock = threading.RLock()
        self._cache = OrderedDict()     # {archive: (bytes, {name: tuple...})}; max 2 archives
        self._dictionary = None
        self._decoder = None
        self._thumbs = OrderedDict()    # in-memory LRU, no artwork files on disk

    def decoder(self):
        if self._decoder is None:
            path = self.hookshot_root / "nx2" / "prompt_assets.py"
            if not path.is_file():
                raise IconError(f"Hookshot decoder missing: {path}")
            nx = str(path.parent)
            if nx not in sys.path:
                sys.path.insert(0, nx)
            spec = importlib.util.spec_from_file_location("hookshot_local_prompt_assets", path)
            module = importlib.util.module_from_spec(spec)
            try:
                spec.loader.exec_module(module)
            except (ImportError, ModuleNotFoundError) as ex:
                raise IconError(f"Hookshot texture decoder dependencies missing: {ex}. See builder/qt/runtime_requirements.txt") from ex
            self._decoder = module
        return self._decoder

    def archives(self):
        return find_archives(self.romfs)

    def _get(self, archive):
        with self._lock:
            if archive in self._cache:
                self._cache.move_to_end(archive)
                return self._cache[archive]
            d = self.decoder()
            path = ensure_inside(self.romfs, archive, required=True)
            if archive.lower().endswith(".zs"):
                if self._dictionary is None:
                    self._dictionary = d.zs_dictionary(str(self.romfs))
                content = d.read_zs(str(self.romfs), archive, self._dictionary)
            else:
                content = path.read_bytes()
            if content[:4] != b"BNTX":
                raise IconError(f"{archive}: decompressed file is not BNTX.")
            try:
                index = d.bntx_textures(content)
            except Exception as exc:
                raise IconError(f"BNTX index failed for {archive}: {exc}") from exc
            self._cache[archive] = (content, index)
            if len(self._cache) > 2:
                self._cache.popitem(last=False)
            return content, index

    def list_textures(self, archive):
        _, textures = self._get(archive)
        return [TextureInfo(archive, name, t[2], t[3], t[1]) for name, t in sorted(textures.items(), key=lambda kv: kv[0].lower())]

    def read_image(self, archive, texture):
        with self._lock:
            content, textures = self._get(archive)
            if texture not in textures:
                raise IconError(f"Missing texture {texture!r} in {archive!r}")
            t = textures[texture]
            if t[2] < 1 or t[3] < 1 or t[2] * t[3] > TEXTURE_MAX_PIXELS:
                raise IconError(f"Texture dimensions unsupported: {t[2]}x{t[3]}")
            if t[1] >> 8 not in (0x1d, 0x2d):
                raise IconError(f"Unsupported BNTX format: 0x{t[1]:04x} ({texture})")
            try:
                im = self.decoder().decode(content, t)
            except Exception as ex:
                raise IconError(f"Cannot decode {texture}, format=0x{t[1]:04x}: {ex}") from ex
            return im

    def thumbnail(self, info, max_size=(118, 82)):
        """Lazy RGBA thumbnail in RAM only; returns (width, height, pixel_bytes)."""
        from PIL import Image
        src = ensure_inside(self.romfs, info.archive, required=True)
        stat = src.stat()
        key = (info.archive, info.name, tuple(max_size), stat.st_size, stat.st_mtime_ns)
        with self._lock:
            if key in self._thumbs:
                self._thumbs.move_to_end(key)
                return self._thumbs[key]
            im = image_rgba(self.read_image(info.archive, info.name))
            im.thumbnail(max_size, Image.Resampling.LANCZOS)
            raw = image_pixels(im)
            self._thumbs[key] = raw
            while len(self._thumbs) > 256:
                self._thumbs.popitem(last=False)
            return raw

    def mapped_image(self, entry):
        """Resolve one JSON mapping to an RGBA PIL image in memory; no export."""
        archive, texture = entry.get("archive"), entry.get("texture")
        if not archive or not texture:
            raise IconError("Mapping has no source archive and internal texture name")
        im = self.read_image(archive, texture)
        return crop_image(image_rgba(im, entry.get("alpha_mode", "auto")), entry.get("crop"))
