"""Display-aware compact Qt window sizing used by the Hookshot GUI.

Qt's availableGeometry() is in logical pixels, so these dimensions work with
Steam Deck display scaling as well as standard desktop DPI. No platform forcing,
minimum window scaling or global application font adjustment is required.
"""
from __future__ import annotations


def bounded_size(available_width: int, available_height: int,
                 preferred=(1000, 640), minimum=(750, 475),
                 reserved=(36, 54)) -> tuple[int, int]:
    """Fit available logical display space, preferring 16:10-ish dimensions.

    The minimum itself is also bounded by the display so small screens never
    end up with an uncloseable dialog that extends beyond availableGeometry().
    """
    aw = max(480, int(available_width) - reserved[0])
    ah = max(330, int(available_height) - reserved[1])
    width = min(aw, max(minimum[0], preferred[0]))
    height = min(ah, max(minimum[1], preferred[1]))
    return width, height


def fit_window(window, preferred=(1000, 640), minimum=(750, 475)):
    """Apply consistent initial size; do not resize after users move the window."""
    from PyQt6.QtWidgets import QApplication

    screen = window.screen() or QApplication.primaryScreen()
    if screen is None:
        window.setMinimumSize(*minimum)
        window.resize(*preferred)
        return
    area = screen.availableGeometry()
    width, height = bounded_size(area.width(), area.height(), preferred, minimum)
    # For DPI-scaled small screens do not enforce a larger-than-screen minimum.
    window.setMinimumSize(min(minimum[0], width), min(minimum[1], height))
    window.resize(width, height)
