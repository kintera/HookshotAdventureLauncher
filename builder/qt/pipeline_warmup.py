"""Safe extended offline pipeline warm-up for Hookshot v2.0.14 and v2.1.0.

The *native* game's --prepare-shaders entrypoint performs shader translation,
pipeline prediction and recorded-pipeline precompile. This module only creates
an ephemeral, merged HOS_ENV_FILE for that subprocess; it never modifies
profile saves, engine caches, or a player's hookshot_env.txt.
"""
from __future__ import annotations

import hashlib
import os
from pathlib import Path
import re

# These are presence-based in upstream Vulkan code: X=0 still enables several.
BLOCKED_FLAGS = frozenset({
    'RX_VK_NO_PREDICT', 'RX_VK_NO_PROGRAM_CACHE', 'RX_VK_NO_STATE_LIST',
    'RX_VK_NO_WARM', 'RX_VK_PREPARE_BACKGROUND',
})

# 'used' is an intentional third value: the native code loads only compute
# programs dispatched before, NOT the 10,000+ known programs (OOM on 16 GB PCs).
PREWARM_FLAGS = {
    'RX_BACKEND': 'vulkan',
    'RX_VK_PRECOMPILE': '1',
    'RX_VK_PREPARE_AGAIN': '1',
    'RX_VK_PRECOMPILE_COMPUTES': 'used',
    'RX_VK_PRECOMPILE_STALL_S': '90',
}


def make_warmup_text(original: str, *, predict_seconds: int = 600) -> str:
    """Merge existing game settings and override ONLY pipeline preparation flags."""
    if not isinstance(predict_seconds, int) or not 30 <= predict_seconds <= 1800:
        raise ValueError('Prediction budget must be between 30 and 1800 seconds.')
    flags = dict(PREWARM_FLAGS)
    flags['RX_VK_PREDICT_S'] = str(predict_seconds)
    conflicts = set(flags) | BLOCKED_FLAGS
    existing = []
    for raw in original.splitlines():
        match = re.match(r'^\s*([A-Za-z_][A-Za-z0-9_]*)\s*=', raw.lstrip('\ufeff'))
        if match and match.group(1) in conflicts:
            continue
        existing.append(raw)
    existing.append('')
    existing.append('# Hookshot extended warm-up: temporary tool-only settings')
    existing.extend(f'{k}={v}' for k, v in flags.items())
    return '\n'.join(existing) + '\n'


def write_warmup_env(executable: Path | str, *, other_env_file: str | None = None,
                     cache_root: Path | str | None = None, predict_seconds: int = 600) -> Path:
    """Create a private atomic env-file override beside no original saves/config."""
    executable = Path(executable).expanduser().resolve()
    if not executable.is_file():
        raise ValueError('Hookshot executable does not exist: ' + str(executable))
    source = Path(other_env_file).expanduser() if other_env_file else executable.parent / 'hookshot_env.txt'
    original = source.read_text(encoding='utf-8-sig') if source.is_file() else ''
    content = make_warmup_text(original, predict_seconds=predict_seconds)
    cache = (Path(cache_root).expanduser() if cache_root else
             Path.home() / '.cache/hookshot-builder/qt/warmup_profiles')
    cache.mkdir(parents=True, exist_ok=True)
    key = hashlib.sha256(str(executable).encode('utf-8')).hexdigest()[:16]
    target = cache / f'warmup_{key}.txt'
    tmp = cache / f'.warmup_{key}.{os.getpid()}.tmp'
    tmp.write_text(content, encoding='utf-8')
    tmp.chmod(0o600)
    tmp.replace(target)
    return target


def state_counts(install_dir: Path | str) -> dict[str, int | None]:
    """Read record counts without modifying pipeline-state files or requiring Vulkan."""
    root = Path(install_dir)
    result = {}
    for name, key in [('hookshot_pipeline_states.bin', 'learned'),
                      ('hookshot_pipeline_states_shipped.bin', 'shipped')]:
        path = root / name
        try:
            with path.open('rb') as f:
                header = f.read(12)
            size = path.stat().st_size
            versions = {b'HKSTATE1': 528, b'HKSTATE2': 552}
            record_size = versions.get(header[:8]) if len(header) == 12 else None
            declared = int.from_bytes(header[8:12], 'little') if record_size else None
            if not record_size or declared != record_size or size < 12 or (size - 12) % record_size:
                result[key] = None
            else:
                result[key] = (size - 12) // record_size
        except OSError:
            result[key] = None
    return result
