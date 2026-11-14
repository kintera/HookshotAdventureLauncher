#!/usr/bin/env python3
"""Set up the optional ASTC save-icon decoder privately for this Python.

No virtualenv and no writes to the system interpreter: pip installs only the
missing icon decoding libraries into builder/qt/.local-python. This is an
explicit user action, never performed silently at startup.

    python3 builder/qt/setup_save_icon_decoder.py

Requires internet access to download compatible wheels. No ROM/Game assets are
read, modified, bundled or transmitted.
"""
from __future__ import annotations

import argparse
import importlib
import importlib.util
from pathlib import Path
import subprocess
import sys

PRIVATE = Path(__file__).resolve().parent / '.local-python'
REQUIREMENTS = {
    'PIL': 'Pillow>=10.0',
    'zstandard': 'zstandard>=0.22',
    'texture2ddecoder': 'texture2ddecoder==1.0.6',
}


def activate_private_directory():
    """Include local, launcher-only packages with either launch command."""
    if PRIVATE.is_dir() and not PRIVATE.is_symlink() and str(PRIVATE) not in sys.path:
        sys.path.insert(0, str(PRIVATE))


def missing_requirements() -> list[str]:
    activate_private_directory()
    importlib.invalidate_caches()
    return [package for module, package in REQUIREMENTS.items()
            if importlib.util.find_spec(module) is None]


def decoder_ready() -> bool:
    if missing_requirements():
        return False
    try:
        import texture2ddecoder
        return callable(getattr(texture2ddecoder, 'decode_astc', None))
    except (ImportError, OSError):
        return False


def install(target: Path = PRIVATE) -> bool:
    target = target.resolve(strict=False)
    if PRIVATE.is_symlink() or (PRIVATE.exists() and not PRIVATE.is_dir()):
        raise OSError('Refusing a symlinked or non-directory private decoder target')
    needed = missing_requirements()
    if not decoder_ready() and 'texture2ddecoder==1.0.6' not in needed:
        needed.append('texture2ddecoder==1.0.6')
    if not needed:
        print('Save icon decoder already available for this Python interpreter.')
        return True
    if target != PRIVATE:
        raise ValueError('Decoder installation must stay in the launcher private directory')
    target.mkdir(parents=True, exist_ok=True)
    command = [sys.executable, '-m', 'pip', 'install', '--disable-pip-version-check',
               '--only-binary=:all:', '--upgrade', '--target', str(target), *needed]
    print('Installing private decoder libraries (no virtualenv, no system Python changes):', flush=True)
    print('  ' + ' '.join(command), flush=True)
    result = subprocess.run(command, check=False)
    importlib.invalidate_caches()
    if result.returncode:
        print('Private install failed; check pip availability and network access.', file=sys.stderr)
        return False
    if not decoder_ready():
        print('Dependencies installed, but the interpreter still cannot import ASTC decoding.', file=sys.stderr)
        return False
    print('Icon decoder ready. Restart Hookshot, then select Settings > Rebuild Save Icons.', flush=True)
    return True


def main(argv=None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--check', action='store_true', help='Only report dependencies; never install')
    args = parser.parse_args(argv)
    activate_private_directory()
    if args.check:
        print('Available' if decoder_ready() else 'Missing or unusable: ' + ', '.join(missing_requirements()))
        return 0 if decoder_ready() else 1
    try:
        return 0 if install() else 1
    except (OSError, ValueError) as exc:
        print(f'Unable to install private decoder: {exc}', file=sys.stderr)
        return 1


if __name__ == '__main__':
    raise SystemExit(main())
