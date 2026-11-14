"""GUI-only Vulkan header discovery/installation for Hookshot's unmodified Linux compiler.

The upstream native builder only compiles hookshot_vk.so if it finds Vulkan-Headers
and SPIRV-Headers via RX_CPM or its built-in cache paths. Missing headers silently
skip the renderer. This module does not modify native sources or game files.
"""
from __future__ import annotations

import argparse
import os
from pathlib import Path
import tarfile
import tempfile
import shutil
from urllib.request import urlopen

PACKAGES = (
    ('vulkanheaders', 'KhronosGroup/Vulkan-Headers', 'v1.4.345', 'include/vulkan/vulkan.h'),
    ('spirv-headers', 'KhronosGroup/SPIRV-Headers', 'vulkan-sdk-1.4.341.0', 'include/spirv/unified1/spirv.h'),
    ('vulkanmemoryallocator', 'GPUOpen-LibrariesAndSDKs/VulkanMemoryAllocator', 'v3.3.0', 'include/vk_mem_alloc.h'),
)


def valid_cache(directory: Path) -> bool:
    """Require actual header files, not just package directory names."""
    directory = Path(directory).expanduser()
    for name, _repo, _tag, rel in PACKAGES:
        parent = directory / name
        if not parent.is_dir():
            return False
        versions = [child for child in parent.iterdir() if child.is_dir() and (child / rel).is_file()]
        if not versions:
            return False
    return True


def cache_candidates(root: Path):
    root = Path(root).resolve()
    paths = [
        os.environ.get('RX_CPM'),
        root / 'cpm',
        root / 'work' / 'eg' / '.cache' / 'cpm',
        root / 'build-linux' / 'deps' / 'cpm',
        root.parent / 'build-linux' / 'deps' / 'cpm',
        Path.home() / '.cache' / 'hookshot-builder' / 'sa' / 'eg' / '.cache' / 'cpm',
        Path.home() / '.cache' / 'hookshot-builder' / 'eg' / '.cache' / 'cpm',
    ]
    # On a version switch, an older Hookshot checkout often has the only
    # initialized CPM cache. No recursive home-directory scanning.
    paths.extend(root.parent.glob('Hookshot*/work/eg/.cache/cpm'))
    seen = set()
    for path in paths:
        if path:
            path = Path(path).expanduser()
            key = str(path.resolve())
            if key not in seen:
                seen.add(key)
                yield path


def find_cache(root: Path) -> Path | None:
    return next((p for p in cache_candidates(root) if valid_cache(p)), None)


def target_cache(root: Path) -> Path:
    return Path(root).resolve() / 'work' / 'eg' / '.cache' / 'cpm'


def _unpack_headers(archive: Path, dest: Path):
    """Extract files under a single archive root; reject links and unsafe paths."""
    dest.mkdir(parents=True, exist_ok=True)
    with tarfile.open(archive, 'r:gz') as tar:
        members = tar.getmembers()
        roots = {m.name.split('/', 1)[0] for m in members if m.name and m.name != '.'}
        if len(roots) != 1:
            raise ValueError('Unexpected archive layout')
        prefix = next(iter(roots))
        for member in members:
            if member.issym() or member.islnk() or member.isdev():
                raise ValueError('Archive contains a link or device entry')
            name = member.name
            if name == prefix:
                continue
            if not name.startswith(prefix + '/'):
                raise ValueError('Invalid archive member')
            rest = name[len(prefix) + 1:]
            rel = Path(rest)
            if rel.is_absolute() or any(part in ('', '.', '..') for part in rel.parts):
                raise ValueError('Unsafe archive path')
            path = dest / rel
            if member.isdir():
                path.mkdir(parents=True, exist_ok=True)
            elif member.isfile():
                path.parent.mkdir(parents=True, exist_ok=True)
                src = tar.extractfile(member)
                if src is not None:
                    with src, path.open('wb') as dst:
                        shutil.copyfileobj(src, dst)


def install(root: Path):
    """Install only the pinned header packages, not the large AppImage deps.sh stack."""
    dest = target_cache(root)
    dest.mkdir(parents=True, exist_ok=True)
    for i, (name, repo, tag, header) in enumerate(PACKAGES):
        target = dest / name / tag
        if (target / header).is_file():
            print(f'{name}: already present', flush=True)
        else:
            url = f'https://github.com/{repo}/archive/refs/tags/{tag}.tar.gz'
            print(f'Downloading {name} ({tag}) from official upstream…', flush=True)
            with tempfile.TemporaryDirectory(prefix='hookshot-headers-', dir=str(dest)) as t:
                stage = Path(t)
                archive = stage / 'archive.tar.gz'
                with urlopen(url, timeout=45) as resp, archive.open('wb') as out:
                    size = 0
                    while True:
                        block = resp.read(256 * 1024)
                        if not block:
                            break
                        size += len(block)
                        if size > 120 * 1024 * 1024:
                            raise ValueError('Header archive exceeds 120 MiB limit')
                        out.write(block)
                files = stage / 'files'
                _unpack_headers(archive, files)
                if not (files / header).is_file():
                    raise ValueError(f'Archive does not contain {header}')
                target.parent.mkdir(parents=True, exist_ok=True)
                if target.exists():
                    shutil.rmtree(target)
                files.replace(target)
            print(f'Installed {name}: {target}', flush=True)
        print(f'@@progress {(i + 1) / len(PACKAGES):.3f}', flush=True)
    if not valid_cache(dest):
        raise RuntimeError('Header installation is incomplete')
    print('Vulkan header cache ready: ' + str(dest), flush=True)
    print('@@done ' + str(dest), flush=True)


if __name__ == '__main__':
    ap = argparse.ArgumentParser()
    ap.add_argument('--install', action='store_true', required=True)
    ap.add_argument('--root', type=Path, required=True)
    args = ap.parse_args()
    try:
        install(args.root)
    except Exception as exc:
        print('@@error Vulkan dependency setup failed: ' + str(exc), flush=True)
        raise SystemExit(1)
