"""Pure read-only installation readiness assessment for the Adventure homepage.

Red: a required native runtime or asset is missing, or validation failed.
Yellow: launchable installation with a non-blocking optimization warning.
Green: core files and shader archive present (not proof of crash-free rendering).
"""
from __future__ import annotations

from dataclasses import dataclass


@dataclass(frozen=True)
class ReadyStatus:
    level: str
    label: str
    explanation: str
    can_launch: bool


def assess_readiness(install, validation_issue: str = '', preparation_warning: str = '') -> ReadyStatus:
    if validation_issue:
        return ReadyStatus('error', 'ACTION REQUIRED', validation_issue, False)
    if not install.playable:
        missing = []
        if install.executable is None:
            missing.append('game executable')
        if install.image is None:
            missing.append('game image')
        # Only a requirement on Linux; GameInstall.playable accounts for this.
        if install.renderer is None and install.executable is not None and install.image is not None:
            missing.append('renderer (if required for this platform)')
        return ReadyStatus('error', 'INSTALLATION REQUIRED',
                           'Missing ' + ', '.join(missing or ['required installation components']) + '.', False)
    warnings = []
    if install.shaders is None:
        warnings.append('shader archive is missing; compilation stalls are possible')
    if preparation_warning:
        warnings.append(preparation_warning)
    if warnings:
        return ReadyStatus('warning', 'PLAYABLE · CHECK WARNINGS', '; '.join(warnings), True)
    return ReadyStatus('success', 'READY TO PLAY',
                       'Required files detected. Graphics stability must still be verified in-game.', True)
