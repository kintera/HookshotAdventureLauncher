"""Verified game icons from a private local cache, with vector fallbacks.

The authoring browser and user game artwork never ship with release builds.
Only verified manifest mappings decoded at launcher startup are used.
"""
from pathlib import Path

from PyQt6.QtCore import QRectF
from PyQt6.QtGui import QPixmap

# The active directory is set ONLY after the complete verified cache is built.
# No unverified legacy PNG exports are used, even if left by older installations.
_active_cache = None
_cache = {}
ALIASES = {
    'bottle': 'bottle_empty',
    'skulltula': 'gold_skulltula',
    'sword_big': 'sword_biggoron',
    'fairy_bottle': 'bottle_fairy',
    'wallet_kokiri': 'wallet_child',
}


def activate_game_cache(directory):
    """Atomically switch the artwork source after generation succeeds."""
    global _active_cache
    folder = Path(directory).resolve() if directory else None
    if folder is not None and not (folder / 'index.json').is_file():
        raise ValueError('Cannot activate incomplete icon cache')
    _active_cache = folder
    _cache.clear()


def icon_pixmap(name: str):
    if _active_cache is None or not isinstance(name, str):
        return None
    role = ALIASES.get(name, name)
    target = (_active_cache / (role + '.png')).resolve()
    if target.parent != _active_cache or not target.is_file() or target.is_symlink():
        return None
    try:
        stat = target.stat()
        if stat.st_size > 32 * 1024 * 1024:
            return None
        ident = (str(target), stat.st_size, stat.st_mtime_ns)
    except OSError:
        return None
    previous = _cache.get(role)
    if previous and previous[0] == ident:
        return previous[1]
    image = QPixmap(str(target))
    if image.isNull() or image.width() > 8192 or image.height() > 8192:
        return None
    _cache[role] = (ident, image)
    return image


def paint_icon(painter, name, x, y, size, opacity=1.0):
    """Return True when a transparent PNG asset was rendered."""
    image = icon_pixmap(name)
    if image is None:
        return False
    painter.save()
    painter.setOpacity(max(0, min(1, opacity)))
    painter.drawPixmap(QRectF(x, y, size, size), image, QRectF(image.rect()))
    painter.restore()
    return True


def item_sprite(name):
    """Resolve an equipped item ID to known asset IDs without inventing ownership."""
    if not isinstance(name, str):
        return None
    name = name.lower()
    if 'master' in name and 'sword' in name:
        return 'sword_master'
    if ('biggoron' in name or 'giant' in name) and 'sword' in name:
        return 'sword_big'
    if ('kokiri' in name or 'child' in name) and 'sword' in name:
        return 'sword_kokiri'
    if 'mirror' in name and 'shield' in name:
        return 'shield_mirror'
    if 'hylian' in name and 'shield' in name:
        return 'shield_hylian'
    if 'deku' in name and 'shield' in name:
        return 'shield_deku'
    if 'sword' in name:
        return 'equipment_sword'
    if 'shield' in name:
        return 'equipment_shield'
    return None
