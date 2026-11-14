"""Non-invasive summary of a Hookshot user directory, with optional manual display data.

Does *not* interpret proprietary Zelda game saves. Never open game save files for writing.
"""
from __future__ import annotations
from dataclasses import dataclass
from datetime import datetime
from pathlib import Path

@dataclass(frozen=True)
class DiskSummary:
    file_count: int
    bytes_used: int
    last_changed: str | None
    save_files: int


def scan_user_dir(user_dir: Path) -> DiskSummary:
    """Read-only traversal. Skip links so a profile can't trick UI into examining unrelated data."""
    total = files = saves = 0
    latest = None
    root = user_dir.resolve()
    if not root.is_dir():
        return DiskSummary(0, 0, None, 0)
    for path in root.rglob("*"):
        try:
            if path.is_symlink() or not path.is_file():
                continue
            stat = path.stat()
        except (OSError, PermissionError):
            continue
        files += 1
        total += stat.st_size
        latest = stat.st_mtime if latest is None else max(latest, stat.st_mtime)
        # Hookshot mounts game saves below its user/save directory; treat files
        # there as save-associated, without interpreting their binary contents.
        parts = [p.lower() for p in path.relative_to(root).parts]
        if "save" in parts[:-1] or any(p.startswith("save\\") for p in parts[:-1]):
            saves += 1
    return DiskSummary(files, total, datetime.fromtimestamp(latest).astimezone().strftime("%Y-%m-%d %H:%M") if latest else None, saves)


def human_size(byte_count: int) -> str:
    size = float(byte_count)
    for unit in ("B", "KiB", "MiB", "GiB"):
        if size < 1024 or unit == "GiB":
            return f"{size:.1f} {unit}" if unit != "B" else f"{int(size)} B"
        size /= 1024
    return str(byte_count)


# UI display metadata; all game-progression fields are manual until a save
# parser is validated. None means unknown, not zero or an empty game state.
DISPLAY_FIELDS = {
    "hero_name": (str, 32),
    "quest_stage": (str, 60),
    "location": (str, 90),
    "hearts": (int, 20),
    "rupees": (int, 9999),
    "deaths": (int, 99999),
    "playtime_minutes": (int, 999999),
    "gold_skulltulas": (int, 100),
    "medallions": (int, 6),
    "spiritual_stones": (int, 3),
}


def validate_display_info(data: dict) -> dict:
    unknown = set(data) - set(DISPLAY_FIELDS)
    if unknown:
        raise ValueError("Unknown adventure-info fields: " + ", ".join(sorted(unknown)))
    output = {}
    for key, value in data.items():
        expected, limit = DISPLAY_FIELDS[key]
        if value is None or value == "":
            continue
        if expected is str:
            if not isinstance(value, str) or len(value) > limit or any(ord(c) < 32 for c in value):
                raise ValueError(f"{key} must be printable text of up to {limit} characters.")
            output[key] = value.strip()
        else:
            if type(value) is not int or not (0 <= value <= limit):
                raise ValueError(f"{key} must be an integer between 0 and {limit}.")
            output[key] = value
    return output


@dataclass(frozen=True)
class GameSlot:
    index: int
    has_progress: bool
    has_caption: bool
    last_saved: str | None


def read_game_slots(user_dir: Path) -> list[GameSlot]:
    """Read the game's actual slot_NN directories without interpreting save bytes.

    Hookshot saves under user_dir/save/<uid>/slot_00/...; there may be several
    accounts. A slot is considered occupied if progress.sav is present.
    """
    root = Path(user_dir)
    result: dict[int, GameSlot] = {}
    base = root / 'save'
    if not base.is_dir():
        return []
    for account in base.iterdir():
        if not account.is_dir() or account.is_symlink():
            continue
        for slot in account.iterdir():
            if not slot.is_dir() or slot.is_symlink() or not slot.name.startswith('slot_'):
                continue
            part = slot.name[5:]
            if not part.isdecimal():
                continue
            index = int(part)
            if not 0 <= index <= 99:
                continue
            progress = slot / 'progress.sav'
            caption = slot / 'caption.sav'
            if not progress.is_file() and not caption.is_file():
                continue
            times = [p.stat().st_mtime for p in (progress, caption) if p.is_file() and not p.is_symlink()]
            latest = max(times) if times else None
            item = GameSlot(index, progress.is_file(), caption.is_file(),
                            datetime.fromtimestamp(latest).astimezone().strftime('%Y-%m-%d %H:%M') if latest else None)
            if index not in result or (result[index].last_saved or '') < (item.last_saved or ''):
                result[index] = item
    return sorted(result.values(), key=lambda item: item.index)
