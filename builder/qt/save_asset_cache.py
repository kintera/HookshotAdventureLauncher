"""Adventure v0.8: local, recoverable save-icon cache from the verified manifest.

The standalone browser emits only JSON. At runtime this module decodes the
user's local game archives and publishes a private cache; no game assets ship
in the release. Failures are isolated PER ROLE so one unsupported texture
cannot discard 70+ valid icons. All published results are built in staging.
"""
from __future__ import annotations

from dataclasses import dataclass
import hashlib
from importlib import metadata
import json
import os
from pathlib import Path
import shutil
import tempfile
from typing import Callable

from game_icon_core import GameAssets, IconError, ensure_inside, load_manifest
from setup_save_icon_decoder import activate_private_directory, decoder_ready, missing_requirements

activate_private_directory()

CACHE_SCHEMA = 2


class SaveAssetGenerationError(RuntimeError):
    pass


@dataclass(frozen=True)
class CacheResult:
    ready: bool
    rebuilt: bool
    directory: Path | None
    roles: tuple[str, ...]
    error: str = ''
    failed: tuple[tuple[str, str], ...] = ()


def failure_summary(failed, *, limit=5):
    """Human-readable individual failures for Settings > Warnings & Errors."""
    if not failed:
        return ''
    lines = [f'{len(failed)} icon mapping(s) could not be loaded; those icons use fallback artwork:']
    for role, message in list(failed)[:limit]:
        lines.append(f'{role}: {message}')
    if len(failed) > limit:
        lines.append(f'...and {len(failed) - limit} others. See cache index.json for full details.')
    return '\n'.join(lines)


class SaveAssetCache:
    def __init__(self, manifest_path, romfs_root, hookshot_root, cache_root=None, *, decoder_factory=GameAssets):
        self.manifest = Path(manifest_path).expanduser().resolve()
        self.romfs = Path(romfs_root).expanduser().resolve()
        self.hookshot = Path(hookshot_root).expanduser().resolve()
        self.root = Path(cache_root or Path.home()/'.cache'/'hookshot'/'save-assets').expanduser().resolve()
        self.decoder_factory = decoder_factory
        self._result = None

    def _snapshot(self):
        if not self.manifest.is_file():
            raise SaveAssetGenerationError('Verified icon manifest not found')
        doc = load_manifest(self.manifest)
        entries = {k: v for k, v in sorted(doc['icons'].items())
                   if isinstance(v, dict) and v.get('verified') and v.get('archive') and v.get('texture')}
        if not entries:
            raise SaveAssetGenerationError('No verified source icons are mapped in the manifest')
        digest = hashlib.sha256()
        digest.update(b'hookshot-save-icons-v0.8-cache-schema-2\0')
        digest.update(str(self.romfs).encode())
        digest.update(self.manifest.read_bytes())
        # Rebuild automatically if decoder dependencies are installed/upgraded.
        for name in ('Pillow', 'zstandard', 'texture2ddecoder'):
            try:
                version = metadata.version(name)
            except metadata.PackageNotFoundError:
                version = 'missing'
            digest.update(f'{name}={version}\0'.encode())
        for role, entry in entries.items():
            # A single missing file must not block otherwise valid mappings.
            try:
                archive = ensure_inside(self.romfs, entry['archive'], required=False)
                stat = archive.stat()
                stamp = f'{stat.st_size}/{stat.st_mtime_ns}'
            except (OSError, IconError, ValueError) as exc:
                stamp = f'unavailable:{type(exc).__name__}'
            digest.update(f'{role}\0{entry["archive"]}\0{stamp}\0'.encode())
        return digest.hexdigest()[:32], entries

    def _folder(self, key):
        return self.root / key

    def _index(self, folder, requested):
        """Validate a complete build outcome, including recorded per-icon errors."""
        try:
            with (folder / 'index.json').open(encoding='utf-8') as f:
                doc = json.load(f)
            if doc.get('cache_schema') != CACHE_SCHEMA:
                return None
            requested_roles = doc.get('requested_roles')
            good = doc.get('roles')
            failures = doc.get('failures')
            if not isinstance(requested_roles, list) or set(requested_roles) != set(requested):
                return None
            if not isinstance(good, list) or not isinstance(failures, dict):
                return None
            if any(not isinstance(k, str) or not isinstance(v, str) for k,v in failures.items()):
                return None
            if len(set(good)) != len(good) or set(good) & set(failures):
                return None
            if set(good) | set(failures) != set(requested):
                return None
            if any(not (folder / (role + '.png')).is_file() or
                   (folder / (role + '.png')).is_symlink() for role in good):
                return None
            # A zero-success generation is a failed build, never activate it.
            if not good:
                return None
            return tuple(good), tuple(sorted(failures.items()))
        except (OSError, ValueError, TypeError, AttributeError):
            return None

    def _read_index(self, folder, expected_roles):
        return self._index(folder, expected_roles) is not None

    def current(self):
        try:
            key, entries = self._snapshot()
            folder = self._folder(key)
            parsed = self._index(folder, entries)
            if parsed is not None:
                roles, failed = parsed
                return CacheResult(True, False, folder, roles, failure_summary(failed), failed)
            return CacheResult(False, False, None, tuple(entries))
        except (OSError, ValueError, TypeError, IconError, SaveAssetGenerationError) as ex:
            return CacheResult(False, False, None, (), str(ex))

    def generate(self, progress: Callable[[int, int, str], None] | None = None, *, force=False):
        """Build roles independently, publish only after the whole pass finishes.

        Successful icon files are published in one directory move. Failed roles
        are recorded in its index and simply render using the existing vector
        fallback. With force=True a prior valid cache is retained if rebuild
        cannot produce any usable result; otherwise safely replaced.
        """
        staging = None
        old_backup = None
        try:
            key, entries = self._snapshot()
            final = self._folder(key)
            parsed = self._index(final, entries)
            if parsed is not None and not force:
                good, failed = parsed
                result = CacheResult(True, False, final, good, failure_summary(failed), failed)
                self._result = result
                return result
            self.root.mkdir(parents=True, exist_ok=True)
            if self.decoder_factory is GameAssets and not decoder_ready():
                missing = ', '.join(missing_requirements()) or 'ASTC decoding unavailable'
                raise SaveAssetGenerationError(
                    'Game icon ASTC decoder missing: ' + missing + '. '
                    'Open Settings > Warnings & Errors > Install / Repair Icon Decoder '
                    '(or run python3 builder/qt/setup_save_icon_decoder.py), then Rebuild Save Icons. '
                    'This installs only into builder/qt/.local-python, not system Python.')
            staging = Path(tempfile.mkdtemp(prefix='.building-', dir=self.root))
            decoder = self.decoder_factory(self.hookshot, self.romfs)
            good, failures = [], {}
            for i, (role, entry) in enumerate(entries.items(), start=1):
                try:
                    img = decoder.mapped_image(entry).convert('RGBA')
                    if not (1 <= img.width <= 8192 and 1 <= img.height <= 8192):
                        raise SaveAssetGenerationError('Invalid or oversized decoded icon dimensions')
                    img.save(staging / (role+'.png'), format='PNG')
                    good.append(role)
                except (Exception, SystemExit) as exc:
                    # The upstream dictionary reader may raise SystemExit on a
                    # missing zstd dictionary; never strand the Qt progress UI.
                    # Log the real decoder error against this mapping; continue.
                    failures[role] = f'{type(exc).__name__}: {exc}'[:800]
                if progress:
                    progress(i, len(entries), role)
            failed = tuple(sorted(failures.items()))
            if not good:
                raise SaveAssetGenerationError(failure_summary(failed) or 'No game icons could be decoded')
            (staging / 'index.json').write_text(json.dumps({
                'cache_schema': CACHE_SCHEMA,
                'source': 'local game installation; never distribute',
                'roles': good,
                'requested_roles': list(entries),
                'failures': failures,
                'manifest_fingerprint': key,
            }, indent=2, ensure_ascii=False)+'\n', encoding='utf-8')
            if final.exists():
                if parsed is None:
                    # Do not replace a directory that might contain other data.
                    raise SaveAssetGenerationError(f'Unknown/incomplete cache directory: {final}; inspect it manually')
                if not force:
                    raise SaveAssetGenerationError(f'Cache already exists: {final}')
                # Preserve the old cache until the new build is complete. Roll back
                # if the second rename fails; no game or save files are touched.
                old_backup = Path(tempfile.mkdtemp(prefix='.old-cache-', dir=self.root))
                old_backup.rmdir()
                os.replace(final, old_backup)
                try:
                    os.replace(staging, final)
                    staging = None
                except Exception:
                    os.replace(old_backup, final)
                    old_backup = None
                    raise
                shutil.rmtree(old_backup)
                old_backup = None
            else:
                os.replace(staging, final)
                staging = None
            result = CacheResult(True, True, final, tuple(good), failure_summary(failed), failed)
            self._result = result
            return result
        except Exception as ex:
            result = CacheResult(False, False, None, (), str(ex))
            self._result = result
            return result
        finally:
            if staging is not None and staging.is_dir():
                shutil.rmtree(staging, ignore_errors=True)
            if old_backup is not None and old_backup.exists():
                # A prior cache is valuable; never silently delete this backup.
                pass

    def icon_path(self, role):
        result = self.current()
        if not result.ready or role not in result.roles:
            return None
        path = result.directory / (role+'.png')
        return path if path.is_file() and not path.is_symlink() else None


def cache_icon_pixmap(cache: SaveAssetCache, role: str, fallback=None, size=None):
    """Read local generated cache through Qt, or use existing QPixmap fallback."""
    path = cache.icon_path(role)
    if path is None:
        return fallback
    try:
        from PyQt6.QtCore import Qt
        from PyQt6.QtGui import QPixmap
        px = QPixmap(str(path))
        if px.isNull():
            return fallback
        if size:
            if isinstance(size, int):
                size = (size, size)
            px = px.scaled(size[0], size[1], Qt.AspectRatioMode.KeepAspectRatio,
                           Qt.TransformationMode.SmoothTransformation)
        return px
    except (ImportError, RuntimeError, ValueError, TypeError):
        return fallback
