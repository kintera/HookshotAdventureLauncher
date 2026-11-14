"""Pure-Python installation and update decisions for Hookshot's Qt builder.

Never infer that an arbitrary existing installation is current: a GUI build
manifest is the evidence for source/build configuration equivalence. The
original make_game.py remains responsible for extraction, version validation,
compilation, and installation.
"""
from __future__ import annotations

from dataclasses import dataclass
import hashlib
import json
from pathlib import Path
import sys

MANIFEST = '.hookshot_qt_build.json'
MANIFEST_FORMAT = 1


@dataclass(frozen=True)
class GameInstall:
    root: Path
    executable: Path | None
    image: Path | None
    shaders: Path | None
    renderer: Path | None
    retained_files: bool

    @property
    def playable(self) -> bool:
        return self.executable is not None and self.image is not None and (
            self.renderer is not None if sys.platform.startswith('linux') else True
        )

    @property
    def present(self) -> bool:
        return self.executable is not None


def inspect_game(folder: str | Path) -> GameInstall:
    root = Path(folder).expanduser()
    exe_name = 'hookshot.exe' if sys.platform == 'win32' else 'hookshot'
    exe = next((p for p in (root / 'bin' / exe_name, root / exe_name) if p.is_file()), None)
    renderer_name = 'hookshot_vk.so' if sys.platform.startswith('linux') else 'hookshot_vk.dll'
    # The native host loads its renderer from beside *the selected executable*.
    # The GUI previously accepted a renderer at root/ while the executable was
    # in bin/, causing a false 'ready to launch' and a black screen.
    renderer = (exe.parent / renderer_name) if exe is not None and (exe.parent / renderer_name).is_file() else None
    image = root / 'image.bin'
    shaders = root / 'hookshot_shaders.bin'
    return GameInstall(root, exe, image if image.is_file() else None,
                       shaders if shaders.is_file() else None, renderer,
                       (root / '.hookshot_exefs').is_dir() and (root / 'romfs').is_dir())


def file_stamp(path: str | Path) -> dict | None:
    p = Path(path).expanduser()
    if not p.is_file():
        return None
    stat = p.stat()
    return {'name': p.name, 'size': stat.st_size, 'mtime_ns': stat.st_mtime_ns}


def folder_stamp(path: str | Path) -> dict | None:
    p = Path(path).expanduser()
    if not p.is_dir():
        return None
    return {'path': str(p.resolve())}


def source_stamp(format_name: str, nup: str, romfs: str, exefs: str) -> dict | None:
    if format_name == 'nup':
        stamp = file_stamp(nup) if nup.strip() else None
        return {'format': 'nup', 'file': stamp} if stamp else None
    if not (romfs.strip() and exefs.strip()):
        return None
    r, e = folder_stamp(romfs), folder_stamp(exefs)
    main = file_stamp(Path(exefs).expanduser() / 'main')
    if not (r and e and main):
        return None
    return {'format': 'extracted', 'romfs': r, 'exefs': e, 'main': main}


def code_stamp(root: Path) -> str:
    """Stable source fingerprint. Frontend-only changes must not force a game rebuild.

    We deliberately use file content, not source checkout mtimes. This detects
    updates to native game code and shaders, without requiring git metadata.
    """
    selected = []
    for directory, extensions in (
        ('nx2', {'.py'}), ('host', {'.c', '.cpp', '.h'}),
        ('gpu', {'.c', '.cpp', '.h', '.hpp'}),
    ):
        selected.extend(p for p in (root / directory).rglob('*')
                        if p.is_file() and p.suffix in extensions)
    digest = hashlib.sha256()
    for p in sorted(selected):
        rel = p.relative_to(root).as_posix()
        digest.update(rel.encode('utf-8') + b'\0')
        with p.open('rb') as handle:
            for chunk in iter(lambda: handle.read(512 * 1024), b''):
                digest.update(chunk)
    return digest.hexdigest()


def load_manifest(root: Path) -> dict | None:
    try:
        data = json.loads((root / MANIFEST).read_text(encoding='utf-8'))
    except (OSError, json.JSONDecodeError, UnicodeDecodeError):
        return None
    return data if isinstance(data, dict) and data.get('format_version') == MANIFEST_FORMAT else None


def write_manifest(root: Path, source: dict | None, language: str, code: str) -> None:
    data = {'format_version': MANIFEST_FORMAT, 'source': source,
            'language': language, 'code_stamp': code}
    root.mkdir(parents=True, exist_ok=True)
    tmp = root / (MANIFEST + '.new')
    tmp.write_text(json.dumps(data, indent=2, sort_keys=True) + '\n', encoding='utf-8')
    tmp.replace(root / MANIFEST)


def build_state(install: GameInstall, source: dict | None, language: str,
                code: str, reinstall: bool = False, update_requested: bool = False) -> tuple[str, str]:
    """Return (mode, human explanation): launch, build, or needs_source.

    An existing install with no selected source can always be *offered* for
    launch. When its recorded code stamp differs and source copies exist, offer
    the real --update path. Unknown provenance is never called up-to-date.
    """
    if reinstall or update_requested:
        if source or install.retained_files:
            return 'build', 'Reinstall/update requested; the existing game will be preserved until the build succeeds.'
        return 'needs_source', 'Select game source files to reinstall the game.'
    if not install.playable:
        if source or install.retained_files:
            return 'build', 'Game installation is incomplete or not yet installed.'
        return 'needs_source', 'Choose your game source files to create a playable installation.'
    old = load_manifest(install.root)
    if old:
        if source and source != old.get('source'):
            return 'build', 'Selected game source has changed since the last completed GUI build.'
        if code != old.get('code_stamp'):
            if source or install.retained_files:
                return 'build', 'Native game/renderer source changed; an update is available.'
            return 'needs_source', 'Builder code changed, but no retained game files are available for an update.'
        if language != old.get('language'):
            if source or install.retained_files:
                return 'build', 'Game language selection has changed.'
            return 'needs_source', 'Language differs; select game files or restore the previous language.'
        return 'launch', 'Installed game matches the recorded source and native build configuration.'
    if source:
        return 'build', 'Game installed, but this source has not been verified against a prior GUI build.'
    return 'launch', 'Existing playable game detected. No new game source selected; update status is not yet verified.'
