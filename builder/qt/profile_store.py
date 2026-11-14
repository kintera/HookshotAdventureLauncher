"""File-safe profile registry for Hookshot.

No game-specific parsing: profile isolation relies on Hookshot's --user directory.
Keeps pre-existing save directories in their original locations.
"""
from __future__ import annotations

import json
import os
from pathlib import Path
import shutil
import sys
import tempfile
from datetime import datetime, timezone
from uuid import uuid4

try:
    from .randomizer_config import generate_seed, make_manifest, normalized_seed, Settings
    from .save_info import validate_display_info
except ImportError:
    from randomizer_config import generate_seed, make_manifest, normalized_seed, Settings
    from save_info import validate_display_info


class ProfileError(Exception):
    pass


def hookshot_is_running() -> bool:
    """Best-effort Linux safety check for a concurrent native Hookshot game.

    The game's binary name is exactly 'hookshot'. Do not mistake the Python
    launcher / builder itself for the running game. Access-denied processes are
    skipped. On non-Linux systems the frontend tracks its own child separately.
    """
    if not sys.platform.startswith("linux"):
        return False
    proc = Path('/proc')
    try:
        candidates = list(proc.iterdir())
    except OSError:
        return False
    for entry in candidates:
        if not entry.name.isdecimal():
            continue
        try:
            executable = (entry / 'exe').resolve(strict=True)
            if executable.name == 'hookshot':
                return True
        except (OSError, PermissionError, RuntimeError):
            continue
    return False


def _timestamp() -> str:
    return datetime.now(timezone.utc).isoformat(timespec="seconds")


def data_root() -> Path:
    if sys.platform == "win32":
        return Path(os.environ.get("LOCALAPPDATA", str(Path.home() / "AppData" / "Local"))) / "HookshotProfileManager"
    return Path(os.environ.get("XDG_DATA_HOME", str(Path.home() / ".local" / "share"))) / "hookshot-profile-manager"


def default_hookshot_user(game_dir: Path | None = None) -> Path:
    if sys.platform == "win32":
        if game_dir is None:
            raise ProfileError("Choose the Hookshot game directory before importing existing saves.")
        return game_dir.expanduser().resolve() / "user"
    return Path(os.environ.get("XDG_DATA_HOME", str(Path.home() / ".local" / "share"))) / "hookshot"


def register_installed_saves(store: "ProfileStore", game_dir: Path) -> list[dict]:
    """Discover both Linux save-root generations without moving or rewriting data.

    Explicit --user suppresses Hookshot's automatic first-run move from
    <game>/user to XDG, so a profile launcher must register the older folder
    separately whenever it contains saved-game data.
    """
    directory = Path(game_dir).expanduser().resolve()
    primary = default_hookshot_user(directory)
    previous = directory / "user"
    found = []
    # A distinct legacy folder should never be auto-migrated or copied.
    old_has_saves = (previous != primary and (previous / "save").is_dir()
                     and any((previous / "save").iterdir()))
    # Avoid inventing an empty XDG profile when real saves still live in the
    # old location. Both roots are registered when both contain save data.
    primary_has_saves = (primary / "save").is_dir() and any((primary / "save").iterdir())
    if primary_has_saves or not old_has_saves:
        p = store.register_legacy(primary, initialize_empty=True, display_name='Link')
        if p: found.append(p)
    if old_has_saves:
        p = store.register_legacy(previous, display_name='Link')
        if p: found.append(p)
    # Prior releases automatically generated these exact labels. Normalize
    # them once, without touching arbitrary profile names entered by users.
    for p in found:
        if p['name'] in ('Original Adventure (existing saves)', 'Link (Existing save)') and store._contains_game_save(p):
            store.rename(p['id'], 'Link')
    return found


def _inside(child: Path, parent: Path) -> bool:
    try:
        child.resolve().relative_to(parent.resolve())
        return True
    except ValueError:
        return False


class ProfileStore:
    def __init__(self, root: Path | None = None):
        self.root = (root or data_root()).expanduser().resolve()
        self.root.mkdir(parents=True, exist_ok=True)
        self.profiles_path = self.root / "profiles"
        self.archive_path = self.root / "archive"
        self.trash_path = self.root / "trash"
        self.backup_path = self.root / "backups"
        self.metadata_path = self.root / "metadata"
        self.index_path = self.root / "profiles.json"
        self.profiles_path.mkdir(exist_ok=True)
        self.metadata_path.mkdir(exist_ok=True)
        if self.index_path.exists():
            self.db = json.loads(self.index_path.read_text(encoding="utf-8"))
            if self.db.get("version") != 1 or not isinstance(self.db.get("profiles"), list):
                raise ProfileError("Unsupported profile registry format; no changes were made.")
        else:
            self.db = {"version": 1, "game_dir": "", "profiles": []}
            self._save()

    def _write_json(self, destination: Path, value: dict):
        """Atomic metadata write; no save-game file is ever modified by this code."""
        destination.parent.mkdir(parents=True, exist_ok=True)
        fd, tmp_name = tempfile.mkstemp(prefix="metadata-", suffix=".tmp", dir=destination.parent)
        try:
            with os.fdopen(fd, "w", encoding="utf-8") as fh:
                json.dump(value, fh, indent=2, ensure_ascii=False)
                fh.write("\n")
                fh.flush()
                os.fsync(fh.fileno())
            os.replace(tmp_name, destination)
        finally:
            if os.path.exists(tmp_name):
                os.unlink(tmp_name)

    def _metadata_file(self, profile_id: str, filename: str) -> Path:
        # Only registered identifiers, never user-provided directory segments.
        self.get(profile_id)
        return self.metadata_path / profile_id / filename

    def set_save_label(self, profile_id: str, label: str):
        """Persist a user-selected display label without rewriting game saves."""
        if label not in ('vanilla', 'rando'):
            raise ProfileError('Save label must be Vanilla or Randomized.')
        profile = self.get(profile_id)
        profile['save_label'] = label
        self._save()

    def get_info(self, profile_id: str) -> dict:
        path = self._metadata_file(profile_id, "adventure_info.json")
        if not path.exists():
            return {}
        data = json.loads(path.read_text(encoding="utf-8"))
        if not isinstance(data, dict):
            raise ProfileError("Adventure info has an invalid format.")
        return validate_display_info(data)

    def set_info(self, profile_id: str, info: dict):
        clean = validate_display_info(info)
        self._write_json(self._metadata_file(profile_id, "adventure_info.json"), clean)

    def get_randomizer(self, profile_id: str) -> dict:
        profile = self.get(profile_id)
        if profile["kind"] != "randomizer-planned":
            raise ProfileError("This profile isn't configured for randomization.")
        path = self._metadata_file(profile_id, "randomizer.json")
        if path.exists():
            try:
                from .randomizer_config import validate_manifest
            except ImportError:
                from randomizer_config import validate_manifest
            return validate_manifest(json.loads(path.read_text(encoding="utf-8")))
        return make_manifest(profile.get("seed") or generate_seed())

    def set_randomizer(self, profile_id: str, seed: str, settings: Settings):
        profile = self.get(profile_id)
        if profile["kind"] != "randomizer-planned":
            raise ProfileError("This profile isn't configured for randomization.")
        config = make_manifest(seed, settings)
        self._write_json(self._metadata_file(profile_id, "randomizer.json"), config)
        profile["seed"] = config["seed"]
        self._save()
        return config

    def _save(self):
        self.root.mkdir(exist_ok=True, parents=True)
        payload = json.dumps(self.db, indent=2, ensure_ascii=False) + "\n"
        fd, tmp_name = tempfile.mkstemp(prefix="profiles-", suffix=".tmp", dir=self.root)
        try:
            with os.fdopen(fd, "w", encoding="utf-8") as fh:
                fh.write(payload)
                fh.flush()
                os.fsync(fh.fileno())
            os.replace(tmp_name, self.index_path)
        finally:
            if os.path.exists(tmp_name):
                os.unlink(tmp_name)

    def game_dir(self) -> Path | None:
        value = self.db.get("game_dir", "")
        return Path(value) if value else None

    def set_game_dir(self, directory: Path):
        directory = directory.expanduser().resolve()
        game_name = "hookshot.exe" if sys.platform == "win32" else "hookshot"
        if not any(path.is_file() for path in (directory / "bin" / game_name, directory / game_name)):
            raise ProfileError("Selected directory must contain hookshot or bin/hookshot (hookshot.exe on Windows).")
        if not (directory / "image.bin").is_file():
            raise ProfileError("Selected directory has no image.bin; choose the built Hookshot game folder.")
        self.db["game_dir"] = str(directory)
        self._save()
        return directory

    def list_profiles(self) -> list[dict]:
        return list(self.db["profiles"])

    @staticmethod
    def _contains_game_save(profile: dict) -> bool:
        """Only recognize actual progress.sav files, not empty directories."""
        base = Path(profile['user_dir']) / 'save'
        if not base.is_dir() or base.is_symlink():
            return False
        try:
            for account in base.iterdir():
                if account.is_symlink() or not account.is_dir():
                    continue
                for slot in account.iterdir():
                    if (slot.is_dir() and not slot.is_symlink() and slot.name.startswith('slot_')
                            and (slot / 'progress.sav').is_file()
                            and not (slot / 'progress.sav').is_symlink()):
                        return True
        except OSError:
            return False
        return False

    def launcher_slots(self) -> list[dict | None]:
        """Three shortcuts. Explicit pins take priority; the rest auto-fill.

        Never relocate or copy a save merely to change a launcher shortcut.
        Legacy empty default roots do not fill a blank launcher slot.
        """
        profiles = self.list_profiles()
        by_id = {p['id']: p for p in profiles}
        pinned = self.db.get('launcher_slots', [None, None, None])
        if not isinstance(pinned, list) or len(pinned) != 3:
            pinned = [None, None, None]
        slots = [None, None, None]
        used = set()
        for i, pid in enumerate(pinned):
            if pid and pid in by_id and pid not in used:
                slots[i] = by_id[pid]
                used.add(pid)
        available = [p for p in profiles if p['id'] not in used and
                     (not p.get('external') or self._contains_game_save(p))]
        # Existing saved progress gets the first open slot, then empty user-made profiles.
        available.sort(key=lambda p: (not self._contains_game_save(p),
                                      not p.get('external', False), p.get('created_at', '')))
        for i, value in enumerate(slots):
            if value is None and available:
                slots[i] = available.pop(0)
        return slots

    def pin_launcher_slot(self, profile_id: str, index: int):
        """Assign a profile to a specific launcher box; never duplicate its pin."""
        self.get(profile_id)
        if type(index) is not int or index not in (0, 1, 2):
            raise ProfileError('Launcher slot must be 1, 2, or 3.')
        pins = self.db.get('launcher_slots', [None, None, None])
        if not isinstance(pins, list) or len(pins) != 3:
            pins = [None, None, None]
        pins = [None if pid == profile_id else pid for pid in pins]
        pins[index] = profile_id
        self.db['launcher_slots'] = pins
        self._save()

    def clear_launcher_slot(self, index: int):
        if type(index) is not int or index not in (0, 1, 2):
            raise ProfileError('Launcher slot must be 1, 2, or 3.')
        pins = self.db.get('launcher_slots', [None, None, None])
        if not isinstance(pins, list) or len(pins) != 3:
            pins = [None, None, None]
        pins[index] = None
        self.db['launcher_slots'] = pins
        self._save()

    def get(self, profile_id: str) -> dict:
        for profile in self.db["profiles"]:
            if profile["id"] == profile_id:
                return profile
        raise ProfileError("Profile not found.")

    def register_legacy(self, legacy_dir: Path, *, initialize_empty: bool = False,
                        display_name: str = "Original Adventure (existing saves)") -> dict | None:
        """Register the default save root in place. No existing save bytes are touched.

        initialize_empty only creates the empty directory for a first-ever game launch.
        """
        legacy_dir = legacy_dir.expanduser().resolve()
        if not legacy_dir.is_dir():
            if not initialize_empty or legacy_dir.exists():
                return None
            legacy_dir.mkdir(parents=True, exist_ok=False)
        for item in self.db["profiles"]:
            if Path(item["user_dir"]).resolve() == legacy_dir:
                return item
        profile = {
            "id": uuid4().hex, "name": display_name,
            "kind": "vanilla", "seed": "", "user_dir": str(legacy_dir),
            "external": True, "save_label": "vanilla", "created_at": _timestamp(), "last_played": "",
        }
        self.db["profiles"].insert(0, profile)
        self._save()
        return profile

    def _make_new(self, name: str, kind: str, seed: str, copy_from: Path | None) -> dict:
        name = name.strip()
        if not name:
            raise ProfileError("Enter a profile name.")
        if len(name) > 80:
            raise ProfileError("Profile name is too long (maximum 80 characters).")
        if kind not in {"vanilla", "randomizer-planned"}:
            raise ProfileError("Unsupported profile type.")
        # Validate all user inputs before creating directories or changing the registry.
        canonical_seed = normalized_seed(seed or generate_seed()) if kind == "randomizer-planned" else ""
        profile_id = uuid4().hex
        user_dir = self.profiles_path / profile_id / "user"
        try:
            if copy_from is None:
                user_dir.mkdir(parents=True, exist_ok=False)
            else:
                src = copy_from.expanduser().resolve()
                if not src.is_dir():
                    raise ProfileError("Source user directory does not exist.")
                # Do not recursively copy the manager into its own managed directory.
                if _inside(user_dir, src):
                    raise ProfileError("Source includes the destination profile tree; refusing recursive import.")
                if src.is_symlink():
                    raise ProfileError("Cannot import a symlink as a save directory.")
                if any(p.is_symlink() for p in src.rglob("*")):
                    raise ProfileError("Source user directory contains symlinks; cannot safely isolate copies.")
                shutil.copytree(src, user_dir, symlinks=False)
        except Exception:
            # Do not leave an unregistered partial profile after a failed copy.
            if user_dir.parent.is_dir():
                shutil.rmtree(user_dir.parent)
            raise
        profile = {
            "id": profile_id, "name": name, "kind": kind,
            "seed": canonical_seed,
            "user_dir": str(user_dir), "external": False,
            "save_label": "rando" if kind == "randomizer-planned" else "vanilla",
            "created_at": _timestamp(), "last_played": "",
        }
        self.db["profiles"].append(profile)
        self._save()
        if kind == "randomizer-planned":
            self.set_randomizer(profile_id, canonical_seed, Settings())
        return profile

    def create(self, name: str, kind: str = "vanilla", seed: str = "") -> dict:
        return self._make_new(name, kind, seed, None)

    def duplicate(self, profile_id: str, name: str) -> dict:
        self._require_game_closed()
        origin = self.get(profile_id)
        new = self._make_new(name, origin["kind"], origin.get("seed", ""), Path(origin["user_dir"]))
        self.set_info(new["id"], self.get_info(profile_id))
        # Cloning a user-labelled Randomized save must preserve its label,
        # even when the source profile has no planned randomizer seed.
        self.set_save_label(new['id'], origin.get('save_label', new['save_label']))
        if origin["kind"] == "randomizer-planned":
            manifest = self.get_randomizer(profile_id)
            self.set_randomizer(new["id"], manifest["seed"], Settings.from_dict(manifest["settings"]))
        return new

    def import_user_dir(self, directory: Path, name: str) -> dict:
        self._require_game_closed()
        return self._make_new(name, "vanilla", "", directory)

    def rename(self, profile_id: str, name: str):
        name = name.strip()
        if not name or len(name) > 80:
            raise ProfileError("Enter a name of 1 to 80 characters.")
        self.get(profile_id)["name"] = name
        self._save()

    def mark_played(self, profile_id: str):
        self.get(profile_id)["last_played"] = _timestamp()
        self._save()

    def _require_game_closed(self):
        if hookshot_is_running():
            raise ProfileError("Close all running Hookshot game windows before copying, archiving or backing up saves.")

    def backup(self, profile_id: str) -> Path:
        self._require_game_closed()
        profile = self.get(profile_id)
        src = Path(profile["user_dir"])
        if not src.is_dir():
            raise ProfileError("Cannot back up: user directory is missing.")
        self.backup_path.mkdir(exist_ok=True)
        target = self.backup_path / (profile["id"] + "-" + datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%SZ") + "-" + uuid4().hex[:6])
        shutil.copytree(src, target / "user", symlinks=True)
        meta = self.metadata_path / profile_id
        if meta.is_dir():
            shutil.copytree(meta, target / "metadata")
        self._write_json(target / "profile.json", profile)
        return target

    def restore_backup_as_new(self, backup_dir: Path, name: str) -> dict:
        """Restore a backup as a NEW profile only; never overwrite an existing profile."""
        self._require_game_closed()
        backup_dir = backup_dir.expanduser().resolve()
        if not _inside(backup_dir, self.backup_path) or backup_dir == self.backup_path:
            raise ProfileError("Choose a backup folder inside this manager's backup directory.")
        manifest_path = backup_dir / "profile.json"
        user = backup_dir / "user"
        if not manifest_path.is_file() or not user.is_dir():
            raise ProfileError("Backup is incomplete (missing profile.json or user folder).")
        old = json.loads(manifest_path.read_text(encoding="utf-8"))
        kind = old.get("kind", "vanilla")
        if kind not in {"vanilla", "randomizer-planned"}:
            raise ProfileError("Backup profile has an unsupported game mode.")
        # Validate metadata before creating a new profile.
        infofile = backup_dir / "metadata" / "adventure_info.json"
        info = validate_display_info(json.loads(infofile.read_text(encoding="utf-8"))) if infofile.exists() else {}
        configfile = backup_dir / "metadata" / "randomizer.json"
        config = None
        if kind == "randomizer-planned" and configfile.exists():
            try:
                from .randomizer_config import validate_manifest
            except ImportError:
                from randomizer_config import validate_manifest
            config = validate_manifest(json.loads(configfile.read_text(encoding="utf-8")))
        restored = self._make_new(name, kind, old.get("seed", ""), user)
        self.set_save_label(restored['id'], old.get('save_label', restored['save_label']))
        self.set_info(restored["id"], info)
        if config:
            self.set_randomizer(restored["id"], config["seed"], Settings.from_dict(config["settings"]))
        return restored

    def delete_save(self, profile_id: str) -> Path:
        """Remove a managed save from the launcher, retaining a recovery copy.

        This intentionally does not delete bytes permanently. Only a managed,
        non-external profile may be removed. No path outside profiles/<id>
        is ever moved, and the running game is a hard blocker.
        """
        self._require_game_closed()
        profile = self.get(profile_id)
        if profile.get('external'):
            raise ProfileError('Original/external saves cannot be deleted by Hookshot.')
        managed_root = self.profiles_path / profile['id']
        user_dir = Path(profile['user_dir'])
        if (managed_root.is_symlink() or user_dir.is_symlink()
                or user_dir != managed_root / 'user'
                or not _inside(managed_root, self.profiles_path)
                or not managed_root.is_dir()):
            raise ProfileError('Unsafe or missing managed save path; refusing deletion.')
        self.trash_path.mkdir(parents=True, exist_ok=True)
        destination = self.trash_path / (profile['id'] + '-' +
            datetime.now(timezone.utc).strftime('%Y%m%dT%H%M%SZ') + '-' + uuid4().hex[:6])
        # Save a complete profile description before we change the registry.
        destination.mkdir()
        previous_db = json.loads(json.dumps(self.db))
        try:
            self._write_json(destination / 'profile.json', profile)
            managed_root.rename(destination / 'profile')
            metadata = self.metadata_path / profile_id
            if metadata.is_dir() and not metadata.is_symlink():
                metadata.rename(destination / 'metadata')
            self.db['profiles'] = [p for p in self.db['profiles'] if p['id'] != profile_id]
            pins = self.db.get('launcher_slots')
            if isinstance(pins, list) and len(pins) == 3:
                self.db['launcher_slots'] = [None if p == profile_id else p for p in pins]
            self._save()
        except Exception:
            # Keep the user data available at its original managed path on a
            # failed registry write or filesystem operation.
            self.db = previous_db
            if (destination / 'profile').exists() and not managed_root.exists():
                (destination / 'profile').rename(managed_root)
            if (destination / 'metadata').exists():
                self.metadata_path.mkdir(exist_ok=True)
                (destination / 'metadata').rename(self.metadata_path / profile_id)
            raise
        return destination

    def archive(self, profile_id: str) -> Path:
        self._require_game_closed()
        profile = self.get(profile_id)
        if profile["external"]:
            raise ProfileError("The original/external profile cannot be archived. Its saves remain in place.")
        src = Path(profile["user_dir"])
        managed_root = self.profiles_path / profile["id"]
        if not _inside(src, managed_root) or src != managed_root / "user":
            raise ProfileError("Unsafe path: refusing to archive data outside the managed profiles folder.")
        self.archive_path.mkdir(exist_ok=True)
        target = self.archive_path / (profile["id"] + "-" + datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%SZ") + "-" + uuid4().hex[:6])
        if managed_root.is_dir():
            managed_root.rename(target)
        else:
            raise ProfileError("Profile directory is missing; no changes made.")
        metadata = self.metadata_path / profile_id
        if metadata.is_dir():
            shutil.copytree(metadata, target / "metadata")
            shutil.rmtree(metadata)
        (target / "profile.json").write_text(json.dumps(profile, indent=2), encoding="utf-8")
        self.db["profiles"] = [p for p in self.db["profiles"] if p["id"] != profile_id]
        self._save()
        return target

    def game_command(self, profile_id: str) -> tuple[list[str], Path]:
        game_dir = self.game_dir()
        if game_dir is None:
            raise ProfileError("Choose your built Hookshot game folder first.")
        game_name = "hookshot.exe" if sys.platform == "win32" else "hookshot"
        binary = next((p for p in (game_dir / "bin" / game_name, game_dir / game_name) if p.is_file()), None)
        if binary is None or not (game_dir / "image.bin").is_file():
            raise ProfileError("Hookshot executable or image.bin not found in the selected folder.")
        user_dir = Path(self.get(profile_id)["user_dir"])
        if not user_dir.is_dir():
            raise ProfileError("Profile user-data folder does not exist.")
        # Hookshot finds image.bin and romfs beside its executable only when
        # invoked without an explicit image argument. The supported bin/hookshot
        # layout keeps the game assets in the parent installation directory:
        # without both explicit args, a bin/ launch silently misses the game.
        command = [str(binary)]
        if binary.parent != game_dir and not (binary.parent / "image.bin").is_file():
            romfs = game_dir / "romfs"
            if not romfs.is_dir():
                raise ProfileError("Game uses bin/hookshot but romfs is missing from the installation root.")
            command += [str(game_dir / "image.bin"), "--romfs", str(romfs)]
        command += ["--user", str(user_dir)]
        return command, game_dir
