"""Qt6-only runtime profile for upstream Hookshot v2.0.14.

Does not modify hookshot_env.txt, the game binary, Vulkan source, or shader caches.
Uses the upstream HOS_ENV_FILE feature to apply a merged per-launch profile.
"""
from __future__ import annotations

from pathlib import Path
import hashlib
import os
import re
import subprocess

# These are presence-based in the v2.0.14 renderer: even =0 can enable them.
# Remove conflicting settings from the copied config when selecting the corresponding repair profile.
STUTTER_CONFLICTS = frozenset({
    'RX_VK_PREPARE_BACKGROUND', 'RX_VK_NO_PROGRAM_CACHE', 'RX_VK_NO_WARM',
})
TERRAIN_CONFLICTS = frozenset({
    'RX_VK_EVICT_RENDERED', 'RX_VK_EVICT_NO_FLUSH', 'RX_VK_EVICT_UNFLUSHABLE',
})
STUTTER_SETTINGS = {'RX_VK_PRECOMPILE': '1'}
TERRAIN_SETTINGS = {
    'RX_VK_VRAM_MB': '10240',
    'RX_VK_KEEP_FINALIZED_MB': '1024',
    'RX_VK_EVICT_MIN_UNUSED': '120',
}

def detect_nvidia_vram_mb() -> int | None:
    """Detect first GPU's memory for safe defaulting. No network or privileges needed."""
    try:
        result = subprocess.run(
            ['nvidia-smi', '--query-gpu=memory.total', '--format=csv,noheader,nounits'],
            capture_output=True, text=True, timeout=2, check=True)
        first = result.stdout.strip().splitlines()[0]
        amount = int(first.strip().split()[0])
        return amount if amount > 0 else None
    except (OSError, IndexError, ValueError, subprocess.SubprocessError):
        return None

def runtime_env_overrides(*, precompile: bool, terrain: bool) -> set[str]:
    """Variables to remove from the inherited environment before Hookshot starts."""
    # Remove stale inherited tuning even when a control is turned off.
    # The merged HOS_ENV_FILE below then supplies the selected values.
    return (set(STUTTER_SETTINGS) | set(TERRAIN_SETTINGS) |
            STUTTER_CONFLICTS | TERRAIN_CONFLICTS)

def make_profile_text(original: str, *, precompile: bool, terrain: bool) -> str:
    """Keep all player settings apart from settings superseded by the chosen repairs."""
    settings = {'RX_BACKEND': 'vulkan', 'RX_VK_PRECOMPILE': '1' if precompile else '0'}
    # The control is authoritative even if the user's persistent env file
    # contains an earlier experimental 10 GB profile. Disabling the checkbox
    # must really disable those overrides for a meaningful A/B test.
    conflicts = set(settings) | set(STUTTER_SETTINGS) | set(TERRAIN_SETTINGS)
    if precompile:
        settings.update(STUTTER_SETTINGS)
    conflicts |= STUTTER_CONFLICTS
    if terrain:
        settings.update(TERRAIN_SETTINGS)
    conflicts |= TERRAIN_CONFLICTS
    conflicts |= settings.keys()
    kept = []
    for raw in original.splitlines():
        line = raw.strip().lstrip('\ufeff')
        match = re.match(r'^([A-Za-z_][A-Za-z0-9_]*)=', line)
        if match and match.group(1) in conflicts:
            continue
        kept.append(raw)
    kept += ['', '# ---- PyQt6 runtime test profile (upstream sources unchanged) ----']
    kept.extend(f'{key}={value}' for key, value in settings.items())
    return '\n'.join(kept) + '\n'

def write_runtime_profile(executable: str | Path, *, precompile: bool, terrain: bool,
                          other_env_file: str | None = None,
                          cache_root: str | Path | None = None) -> Path:
    """Make a per-install cached env file without altering user config or native files."""
    executable = Path(executable).expanduser().resolve()
    if not executable.is_file():
        raise ValueError(f'Executable does not exist: {executable}')
    source = Path(other_env_file).expanduser() if other_env_file else executable.parent / 'hookshot_env.txt'
    original = source.read_text(encoding='utf-8-sig') if source.is_file() else ''
    text = make_profile_text(original, precompile=precompile, terrain=terrain)
    cache = Path(cache_root).expanduser() if cache_root else Path.home() / '.cache/hookshot-builder/qt/runtime_profiles'
    cache.mkdir(parents=True, exist_ok=True)
    key = hashlib.sha256(str(executable).encode()).hexdigest()[:16]
    target = cache / f'launch_{key}.txt'
    # Atomic replacement so a profile is always complete when a new process opens it.
    temporary = cache / f'.launch_{key}.{os.getpid()}.tmp'
    temporary.write_text(text, encoding='utf-8')
    temporary.chmod(0o600)
    temporary.replace(target)
    return target
