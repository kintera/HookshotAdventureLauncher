"""Shared selectable Hookshot UI themes.

The choice is stored independently of adventure profiles and game saves.  This
module deliberately keeps Qt imports inside functions so the palette and path
checks can be covered by headless tests.
"""
from __future__ import annotations

from pathlib import Path

THEMES = (
    ('goron', 'Darunia', 'hookshot_goron.qss'),
    ('gannon', 'Gannon', 'hookshot_gannon.qss'),
    ('zelda', 'Link', 'hookshot_zelda_vaporwave.qss'),
    ('purple', 'Royal', 'hookshot_theme.qss'),
    ('blue_crystal', 'Ruto', 'hookshot_n64_blue_crystal.qss'),
    ('zelda_gold', 'Tri-force', 'hookshot_zelda_gold.qss'),
    ('zelda_pearl', 'Zelda', 'hookshot_zelda_pearl.qss'),
)
DEFAULT_THEME = 'purple'


def normalized_theme(value: object) -> str:
    valid = {key for key, _label, _file in THEMES}
    return str(value) if str(value) in valid else DEFAULT_THEME


def theme_labels() -> tuple[tuple[str, str], ...]:
    return tuple((key, label) for key, label, _file in THEMES)


# Existing palette, shared across Adventure, Settings, installer and diagnostics.
# Rendering-only extension: no separate theme state or theme-specific Qt class.
SURFACE_COLORS = {
    'purple': ('#251c32', '#403047', '#17111f', '#6c506e'),
    'zelda': ('#193831', '#294e42', '#0b1917', '#426b53'),
    'zelda_gold': ('#3c2b13', '#64481c', '#17120b', '#b38626'),
    'blue_crystal': ('#1c334d', '#314d69', '#101a30', '#5682ae'),
    'gannon': ('#35232f', '#563440', '#191018', '#8a4854'),
    'zelda_pearl': ('#30275e', '#4b4b8b', '#11152f', '#ca9a70'),
    'goron': ('#18191c', '#3d3430', '#09090c', '#e05b28'),
}


def shared_gradient_qss(theme: str) -> str:
    """Apply the Adventure-style bevel/gradient to ordinary launcher surfaces."""
    dark, light, bottom, rim = SURFACE_COLORS[normalized_theme(theme)]
    surface = ('qlineargradient(x1:0,y1:0,x2:1,y2:1,'
               f'stop:0 {light},stop:0.30 {dark},stop:1 {bottom})')
    chrome = ('qlineargradient(x1:0,y1:0,x2:1,y2:1,'
              f'stop:0 {dark},stop:0.7 {bottom},stop:1 {light})')
    if normalized_theme(theme) == 'goron':
        # Charred basalt / molten seams, not the metal-and-gold Tri-force palette.
        surface = ('qlineargradient(x1:0,y1:0,x2:1,y2:1,'
                   'stop:0 #53423a,stop:0.23 #29282a,stop:0.62 #121317,'
                   'stop:0.89 #64291d,stop:1 #d75524)')
        chrome = ('qlineargradient(x1:0,y1:0,x2:1,y2:1,'
                  'stop:0 #29292a,stop:0.56 #0e0f13,stop:0.86 #492017,'
                  'stop:1 #9b3420)')
    elif normalized_theme(theme) == 'zelda_pearl':
        # Royal twilight blue and violet; porcelain is reserved for controls.
        surface = ('qlineargradient(x1:0,y1:0,x2:1,y2:1,'
                   'stop:0 #6566a9,stop:0.27 #3c3976,stop:0.74 #272455,'
                   'stop:1 #151935)')
        chrome = ('qlineargradient(x1:0,y1:0,x2:1,y2:1,'
                  'stop:0 #35315f,stop:0.49 #16172f,stop:0.82 #22204b,'
                  'stop:1 #664379)')
    # All seven palettes share exactly the same outline geometry. This avoids
    # boxed text and oversized gilded rails in Zelda/Darunia; only hues vary.
    outline = rim
    return (f"\n/* Adventure v0.8: unified low-contrast outline geometry. */\n"
            f"QWidget#launcherRoot, QWidget#launcherSettingsPage, QWidget#launcherAdventurePage, "
            f"QWidget#launcherDetailPage {{ background: {chrome}; }}\n"
            f"QFrame#stepCard, QFrame#panel, QFrame#card, QFrame#statTile, "
            f"QFrame#statPanel, QFrame#profileCardFrame, QFrame#footer {{ "
            f"background: {surface}; border:1px solid {outline}; border-radius:9px; }}\n"
            f"QTabWidget::pane {{ background: {surface}; border:1px solid {outline}; border-radius:9px; }}\n"
            f"QLabel#sohSaveCharacter, QLabel#sohSaveLocation, QLabel#sohSaveMeta, "
            f"QLabel#readyTitle, QLabel#profileTitle, QLabel#heading, "
            f"QLabel#subheading, QLabel#stageTitle, QLabel#stepTitle, "
            f"QLabel#statCaption, QLabel#statValue, QLabel#statDetail {{ "
            f"background:transparent; border:none; }}\n"
            f"QPlainTextEdit#summaryText {{ background: {chrome}; }}\n")


def _blend_hex(a: str, b: str, mix: float) -> str:
    """Blend two existing theme colors for subdued, readable launcher chrome."""
    c1 = tuple(int(a[i:i + 2], 16) for i in (1, 3, 5))
    c2 = tuple(int(b[i:i + 2], 16) for i in (1, 3, 5))
    return '#' + ''.join(f'{round(x * (1 - mix) + y * mix):02x}' for x, y in zip(c1, c2))


def adventure_menu_qss(theme: str) -> str:
    """Raised Adventure shortcuts with palette-matched gradients for all themes.

    The game's heart, inventory, and quest artwork is painted separately and
    is intentionally unaffected by this menu-only stylesheet.
    """
    dark, light, bottom, rim = SURFACE_COLORS[normalized_theme(theme)]
    # Recessed surrounding page, convex card face: the lighter top edge and
    # darker bottom edge suggest a raised, physical surface without a frame
    # around the entire three-slot group.
    card = _blend_hex(dark, bottom, 0.30)
    card_hover = _blend_hex(card, light, 0.14)
    card_gradient = ('qlineargradient(x1:0,y1:0,x2:0,y2:1,'
                     f'stop:0 {_blend_hex(light, "#ffffff", 0.14)},'
                     f'stop:0.12 {_blend_hex(light, dark, 0.15)},'
                     f'stop:0.55 {_blend_hex(dark, light, 0.10)},'
                     f'stop:1 {_blend_hex(bottom, dark, 0.35)})')
    card_hover_gradient = ('qlineargradient(x1:0,y1:0,x2:0,y2:1,'
                           f'stop:0 {_blend_hex(light, "#ffffff", 0.23)},'
                           f'stop:0.12 {_blend_hex(light, dark, 0.05)},'
                           f'stop:0.55 {_blend_hex(dark, light, 0.22)},'
                           f'stop:1 {_blend_hex(bottom, dark, 0.48)})')
    bevel_top = _blend_hex(rim, '#ffffff', 0.28)
    bevel_left = _blend_hex(rim, light, 0.38)
    bevel_right = _blend_hex(bottom, '#000000', 0.26)
    bevel_bottom = _blend_hex(bottom, '#000000', 0.42)
    stroke = _blend_hex(rim, bottom, 0.63)
    hover_stroke = _blend_hex(rim, light, 0.25)
    tab_bg = _blend_hex(bottom, dark, 0.46)
    button = _blend_hex(dark, light, 0.28)
    button_hover = _blend_hex(dark, light, 0.48)
    accent = _blend_hex(rim, '#eee6fa', 0.34)
    # Accent stays restrained; all three shortcut cards get the same depth.
    return (f"\n/* Adventure menu refresh: v0.8 data/navigation unchanged. */\n"
            f"QWidget#adventureList {{ background:transparent; border:none; }}\n"
            f"QFrame#profileCardFrame {{ background:{card_gradient}; "
            f"border:1px solid {stroke}; border-top-color:{bevel_top}; "
            f"border-left-color:{bevel_left}; border-right-color:{bevel_right}; "
            f"border-bottom-color:{bevel_bottom}; border-radius:12px; }}\n"
            f"QFrame#profileCardFrame:hover {{ background:{card_hover_gradient}; "
            f"border-top-color:{_blend_hex(bevel_top, '#ffffff', 0.18)}; "
            f"border-left-color:{hover_stroke}; }}\n"
            f"QLabel#adventureSavePreview {{ background:{tab_bg}; color:#ada6b7; "
            f"border:1px solid {stroke}; border-radius:7px; }}\n"
            f"QLabel#adventureCardTitle, QLabel#adventureDetailTitle {{ "
            f"color:#f5f1f8; background:transparent; border:none; "
            f"font-size:15px; font-weight:700; }}\n"
            f"QLabel#adventureDetailTitle {{ font-size:19px; }}\n"
            f"QLabel#adventureIntro, QLabel#adventureEmptyHint {{ color:#b9afc7; "
            f"font-size:11px; background:transparent; border:none; }}\n"
            f"QLabel#adventureSlotLabel {{ color:{accent}; font-size:11px; "
            f"font-weight:700; background:transparent; border:none; }}\n"
            f"QLabel#heading {{ font-size:17px; font-weight:800; letter-spacing:1px; }}\n"
            f"QLabel#readyTitle {{ font-size:19px; letter-spacing:0.3px; }}\n"
            f"QLabel#brandMark {{ background:transparent; border:none; }}\n"
            f"QTabWidget#launchTabs::pane {{ background:transparent; border:none; }}\n"
            f"QTabWidget#launchTabs QTabBar::tab {{ background:{tab_bg}; "
            f"color:#c9bfd7; border:1px solid {stroke}; border-radius:7px; "
            f"padding:6px 15px; margin-right:5px; }}\n"
            f"QTabWidget#launchTabs QTabBar::tab:selected {{ color:#f5f1f8; "
            f"background:{card}; border-color:{hover_stroke}; }}\n"
            f"QTabWidget#launchTabs QTabBar::tab:hover {{ background:{card_hover}; }}\n"
            f"QPushButton#adventureSelectButton {{ color:#f8f4fc; "
            f"background:{button}; border:1px solid {hover_stroke}; "
            f"border-radius:7px; padding:6px 12px; font-weight:700; }}\n"
            f"QPushButton#adventureSelectButton:hover {{ background:{button_hover}; "
            f"border-color:{accent}; }}\n"
            f"QPushButton#adventureSelectButton:pressed {{ background:{dark}; }}\n"
            f"QPushButton#adventureSelectButton:focus {{ border:2px solid {accent}; }}\n"
            f"QPushButton#adventureSelectButton:disabled {{ color:#92899e; "
            f"background:{tab_bg}; border-color:{stroke}; }}\n"
            # Profile manager keeps the same neutral card styling without
            # changing its tabs, pagination, or profile operations.
            f"QWidget#root QFrame#panel {{ background:{card}; "
            f"border:1px solid {stroke}; border-radius:11px; }}\n"
            f"QWidget#root QPushButton#profileCard {{ text-align:left; "
            f"background:{tab_bg}; border:1px solid {stroke}; "
            f"border-radius:7px; padding:6px 10px; }}\n"
            f"QWidget#root QPushButton#profileCard:hover {{ background:{card_hover}; }}\n"
            f"QWidget#root QPushButton#profileCard[active=\"true\"] {{ "
            f"background:{card_hover}; border:1px solid {accent}; }}\n")


def raised_button_qss(theme: str) -> str:
    """Theme-derived beveled buttons for launcher, Settings, and utilities.

    The top/left highlight and lower/right shadow give slight physical lift;
    :pressed reverses that bevel and nudges the label down one pixel. This is
    appended after legacy theme QSS so all seven themes behave consistently.
    Selected profile-card state and readiness/status labels stay independent.
    """
    dark, light, bottom, rim = SURFACE_COLORS[normalized_theme(theme)]
    face_top = _blend_hex(light, '#ffffff', 0.13)
    face_mid = _blend_hex(dark, light, 0.32)
    face_low = _blend_hex(bottom, dark, 0.31)
    top_edge = _blend_hex(rim, '#ffffff', 0.44)
    left_edge = _blend_hex(rim, light, 0.46)
    right_edge = _blend_hex(bottom, '#000000', 0.52)
    low_edge = _blend_hex(bottom, '#000000', 0.67)
    hover_top = _blend_hex(face_top, '#ffffff', 0.15)
    hover_mid = _blend_hex(face_mid, light, 0.30)
    hover_low = _blend_hex(face_low, light, 0.18)
    focus_edge = _blend_hex(rim, '#ffffff', 0.61)
    primary_top = _blend_hex(rim, '#ffffff', 0.22)
    primary_mid = _blend_hex(rim, dark, 0.22)
    primary_low = _blend_hex(rim, bottom, 0.43)
    muted = _blend_hex(bottom, dark, 0.45)

    def grad(a, b, c):
        return ('qlineargradient(x1:0,y1:0,x2:0,y2:1,'
                f'stop:0 {a}, stop:0.25 {b}, stop:1 {c})')

    common = ('QPushButton, QPushButton#secondaryButton, '
              'QPushButton#adventureSelectButton, QPushButton#settingsHeaderTag')
    hover = (', '.join(f'{x}:hover' for x in common.split(', ')))
    pressed = (', '.join(f'{x}:pressed' for x in common.split(', ')))
    disabled = (', '.join(f'{x}:disabled' for x in common.split(', ')))
    primary = 'QPushButton#primaryButton, QPushButton#primary'
    primary_hover = 'QPushButton#primaryButton:hover, QPushButton#primary:hover'
    primary_press = 'QPushButton#primaryButton:pressed, QPushButton#primary:pressed'
    primary_disabled = 'QPushButton#primaryButton:disabled, QPushButton#primary:disabled'
    return ("\n/* Theme-aware 3D controls: lifted at rest, inset on press. */\n"
            f'{common} {{ background:{grad(face_top, face_mid, face_low)};'
            f'border-top:2px solid {top_edge}; border-left:2px solid {left_edge};'
            f'border-right:2px solid {right_edge}; border-bottom:3px solid {low_edge};'
            'border-radius:7px; color:#f8f5fc; padding:6px 11px;'
            'font-weight:700; }\n'
            f'{hover} {{ background:{grad(hover_top, hover_mid, hover_low)};'
            f'border-top-color:{focus_edge}; border-left-color:{top_edge}; }}\n'
            f'{pressed} {{ background:{grad(face_low, face_mid, face_top)};'
            f'border-top:3px solid {low_edge}; border-left:2px solid {right_edge};'
            f'border-right:2px solid {left_edge}; border-bottom:2px solid {top_edge};'
            'padding-top:7px; padding-bottom:5px; }\n'
            f'{primary} {{ background:{grad(primary_top, primary_mid, primary_low)};'
            f'border-top:2px solid {focus_edge}; border-left:2px solid {top_edge};'
            f'border-right:2px solid {right_edge}; border-bottom:3px solid {low_edge};'
            'border-radius:7px; color:#fffefa; padding:6px 12px; font-weight:800; }\n'
            f'{primary_hover} {{ background:{grad(_blend_hex(primary_top, "#ffffff", .16), primary_top, primary_mid)};'
            f'border-top-color:{_blend_hex(focus_edge, "#ffffff", .17)}; }}\n'
            f'{primary_press} {{ background:{grad(primary_low, primary_mid, primary_top)};'
            f'border-top:3px solid {low_edge}; border-left:2px solid {right_edge};'
            f'border-right:2px solid {top_edge}; border-bottom:2px solid {focus_edge};'
            'padding-top:7px; padding-bottom:5px; }\n'
            f'{disabled}, {primary_disabled} {{ background:{muted}; color:#9b95a5;'
            f'border:1px solid {right_edge}; padding:7px 12px; }}\n'
            # Keep profile-manager selection recognizable, but make its row
            # button convex too. Its existing [active=true] selector is kept.
            f'QWidget#root QPushButton#profileCard {{'
            f'background:{grad(face_top, face_mid, face_low)};'
            f'border-top:2px solid {top_edge}; border-left:2px solid {left_edge};'
            f'border-right:2px solid {right_edge}; border-bottom:3px solid {low_edge}; }}\n'
            f'QWidget#root QPushButton#profileCard:hover {{'
            f'background:{grad(hover_top, hover_mid, hover_low)}; }}\n'
            f'QWidget#root QPushButton#profileCard:pressed {{'
            f'background:{grad(face_low, face_mid, face_top)};'
            f'border-top:3px solid {low_edge}; border-bottom:2px solid {top_edge}; }}\n'
            f'QWidget#root QPushButton#profileCard[active="true"] {{'
            f'border:2px solid {focus_edge}; }}\n')


def adventure_tooltip_qss(theme: str) -> str:
    """Theme-aware popup chrome for every hover hint in Adventure Details.

    Keep this at the END of the app stylesheet so it supersedes the legacy
    per-theme QToolTip rules. No independent UI preference or hardcoded Royal
    palette is introduced; switching the theme re-generates the tooltip colors.
    """
    dark, light, bottom, rim = SURFACE_COLORS[normalized_theme(theme)]
    cap = _blend_hex(light, dark, 0.22)
    body = _blend_hex(dark, bottom, 0.30)
    foot = _blend_hex(bottom, dark, 0.35)
    stroke = _blend_hex(rim, '#f4f3fb', 0.29)
    ink = _blend_hex('#ffffff', light, 0.11)
    return ("\n/* Adventure Details: live theme-matched icon hover popup. */\n"
            "QToolTip { "
            "background:qlineargradient(x1:0,y1:0,x2:0,y2:1,"
            f"stop:0 {cap}, stop:0.22 {body}, stop:1 {foot}); "
            f"color:{ink}; border:1px solid {stroke}; "
            "border-radius:7px; padding:6px 9px; "
            "font-size:12px; font-weight:600; }\n")


def stylesheet_for(theme: str) -> str:
    current = normalized_theme(theme)
    filename = next(file for key, _label, file in THEMES if key == current)
    return ((Path(__file__).with_name(filename)).read_text(encoding='utf-8')
            + shared_gradient_qss(current)
            + adventure_menu_qss(current)
            + raised_button_qss(current)
            + adventure_tooltip_qss(current))


def selected_theme() -> str:
    from PyQt6.QtCore import QSettings
    return normalized_theme(QSettings('Hookshot', 'QtBuilder').value('theme', DEFAULT_THEME))


def apply_theme(theme: str | None = None, persist: bool = True) -> str:
    """Apply to the current QApplication, including open utility/dialog windows.

    The app-level stylesheet is intentional.  Per-window stylesheets would
    shadow it, causing the builder and profile manager to disagree on colors.
    """
    from PyQt6.QtCore import QSettings
    from PyQt6.QtWidgets import QApplication

    name = normalized_theme(theme if theme is not None else selected_theme())
    if persist:
        QSettings('Hookshot', 'QtBuilder').setValue('theme', name)
    app = QApplication.instance()
    if app is not None:
        app.setStyleSheet(stylesheet_for(name))
    return name


def populate_theme_combo(combo) -> None:
    """Configure a small, readable selector without causing selection events."""
    previous = combo.blockSignals(True)
    try:
        combo.clear()
        for key, label in theme_labels():
            combo.addItem(label, key)
        combo.setCurrentIndex(max(0, combo.findData(selected_theme())))
    finally:
        combo.blockSignals(previous)
    combo.setToolTip('Change the theme in both the builder and profile manager; saves are unaffected.')


def synchronize_theme_combo(combo) -> None:
    previous = combo.blockSignals(True)
    try:
        combo.setCurrentIndex(max(0, combo.findData(selected_theme())))
    finally:
        combo.blockSignals(previous)
