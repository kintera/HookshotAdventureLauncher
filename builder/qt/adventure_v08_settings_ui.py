"""Hookshot Adventure UI v0.8: theme-aware save details and header Settings UI.

This is a *reusable integration helper*, not a standalone theme selector or a
replacement for the main Qt launcher. Callers provide existing launcher widgets
and the already-selected theme. No source/saves/archives are modified.
"""
from __future__ import annotations

from dataclasses import dataclass
from pathlib import Path
from collections.abc import Mapping
import re

from adventure_save_display import save_kind_badge_text

# Fallback values only; the calling launcher should provide its live theme
# colors through theme_colors. Never set an independent theme preference.
THEME_TOKENS = {
    'purple vaporwave': ('#b58af2', '#292238', '#514266'),
    'royal': ('#b58af2', '#292238', '#514266'),
    'zelda green': ('#79bf86', '#202e27', '#47674c'),
    'link green': ('#79bf86', '#202e27', '#47674c'),
    'link': ('#79bf86', '#202e27', '#47674c'),
    'zelda gold': ('#e1c26f', '#322a1c', '#76603a'),
    'tri force gold': ('#efbd3e', '#32240f', '#a07922'),
    'tri force': ('#efbd3e', '#32240f', '#a07922'),
    'n64 blue crystal': ('#7cc8f5', '#1e3045', '#456b88'),
    'ruto': ('#7cc8f5', '#1e3045', '#456b88'),
    'gannon': ('#df7e88', '#35212c', '#794658'),
    'zelda': ('#ecb0d2', '#2c2b60', '#d8a663'),
    'goron': ('#ff8d42', '#222024', '#b8482c'),
    'darunia': ('#ff8d42', '#222024', '#b8482c'),
}


@dataclass(frozen=True)
class ThemeColors:
    accent: str
    fill: str
    border: str


def _safe_hex(value: str) -> str:
    """Avoid interpolating untrusted arbitrary QSS into the generated style."""
    if not isinstance(value, str) or not re.fullmatch(r'#[0-9a-fA-F]{6}(?:[0-9a-fA-F]{2})?', value):
        raise ValueError('Theme color must be a #RRGGBB or #RRGGBBAA hex string')
    return value


def resolve_theme_colors(theme_name=None, theme_colors=None):
    """Prefer explicit *current launcher* palette; fallback to built-in tokens."""
    if theme_colors is not None:
        if isinstance(theme_colors, ThemeColors):
            data = (theme_colors.accent, theme_colors.fill, theme_colors.border)
        elif isinstance(theme_colors, Mapping):
            data = tuple(theme_colors[key] for key in ('accent', 'fill', 'border'))
        else:
            data = tuple(theme_colors)
        if len(data) != 3:
            raise ValueError('Expected accent, fill, border')
    else:
        key = str(theme_name or 'purple vaporwave').strip().lower().replace('_', ' ').replace('-', ' ')
        key = ' '.join(key.split())
        data = THEME_TOKENS.get(key, THEME_TOKENS['purple vaporwave'])
    return ThemeColors(*(_safe_hex(c) for c in data))


def selected_save_info_qss(theme_name=None, theme_colors=None):
    """A beveled, single-piece file panel using the current seven theme colors.

    This is still the shared v0.8 theme implementation, not a new theme.
    The SoH-like inset trim and header typography are purely presentational.
    """
    t = resolve_theme_colors(theme_name, theme_colors)
    # Zelda is a deep blue-violet theme. White is reserved for controls,
    # with light text over the save panel rather than a washed-out white panel.
    location_ink = '#fff7f9'
    meta_ink = '#e5d9ed'
    # Use a gentle *surface* gradient and draw the theme color on the edge.
    # The old border->fill->border gradient produced hard rectangular color
    # bands under the labels, especially on Zelda and Darunia.
    def blend(base, tint, amount):
        a = tuple(int(base[x:x+2], 16) for x in (1, 3, 5))
        b = tuple(int(tint[x:x+2], 16) for x in (1, 3, 5))
        return '#' + ''.join(f'{round(v*(1-amount)+w*amount):02x}' for v, w in zip(a, b))

    high = blend(t.fill, t.accent, 0.24)
    middle = blend(t.fill, t.accent, 0.10)
    low = blend(t.fill, '#050509', 0.24)
    top_bevel = blend(t.border, '#ffffff', 0.36)
    left_bevel = blend(t.border, t.accent, 0.30)
    dark_bevel = blend(t.fill, '#000000', 0.55)
    # A recessed screenshot reverses the raised-card light direction:
    # dark top/left edges, lit lower/right edges, all in ONE frame.
    well_dark = blend(t.fill, '#000000', 0.68)
    well_floor = blend(t.fill, '#000000', 0.38)
    well_lip = blend(t.border, '#ffffff', 0.36)
    well_glint = blend(t.border, t.accent, 0.44)
    # Local QSS scoped to selected details: all seven themes use the same
    # convex geometry, with no changes to Adventure Select or installer cards.
    return (f'QFrame#v08SelectedSaveInfo {{'
            f'background: qlineargradient(x1:0,y1:0,x2:0,y2:1,'
            f'stop:0 {high}, stop:0.11 {middle}, stop:0.62 {t.fill}, stop:1 {low});'
            f'border: 1px solid {t.border};'
            f'border-top:2px solid {top_bevel};'
            f'border-left:2px solid {left_bevel};'
            f'border-right:2px solid {dark_bevel};'
            f'border-bottom:3px solid {dark_bevel};'
            'border-radius: 9px; padding:0px; }\n'
            'QFrame#v08SelectedSaveInfo QLabel { background: transparent; border: none; }\n'
            'QFrame#v08SelectedSaveInfo QFrame#detailPreviewShell {'
            f'background:qlineargradient(x1:0,y1:0,x2:0,y2:1,'
            f'stop:0 {well_dark}, stop:0.30 {well_floor}, stop:1 {t.fill});'
            f'border-top:3px solid {well_dark};'
            f'border-left:3px solid {well_dark};'
            f'border-right:2px solid {well_lip};'
            f'border-bottom:2px solid {well_glint};'
            'border-radius:9px; padding:0px; }\n'
            f'QFrame#v08SelectedSaveInfo QLabel#adventureSavePreview {{'
            f'background:{well_floor}; border:0px; border-radius:2px; }}\n'
            'QFrame#v08SelectedSaveInfo QLabel#v08SaveKindBadge {'
            'color:#e2e0e6; background:#46454d; border:1px solid #77747e;'
            'border-radius:5px; font-size:11px; font-weight:600;}\n'
            f'QFrame#v08SelectedSaveInfo QLabel#sohSaveCharacter {{color:{t.accent};'
            'font-size:18px; font-weight:800; }\n'
            'QFrame#v08SelectedSaveInfo QLabel#sohSaveLocation {'
            f'color:{location_ink}; font-size:15px; font-weight:700; }}\n'
            'QFrame#v08SelectedSaveInfo QLabel#sohSaveMeta {'
            f'color:{meta_ink}; font-size:11px; }}')


def style_selected_save_info(frame, theme_name=None, theme_colors=None):
    """Apply only to a supplied save-info QFrame; re-run when launcher theme changes."""
    frame.setObjectName('v08SelectedSaveInfo')
    frame.setStyleSheet(selected_save_info_qss(theme_name, theme_colors))
    return frame


def make_grey_save_tag(save_data, parent=None):
    from PyQt6.QtCore import Qt
    from PyQt6.QtWidgets import QLabel
    label = QLabel(save_kind_badge_text(save_data), parent)
    label.setObjectName('v08SaveKindBadge')
    label.setAlignment(Qt.AlignmentFlag.AlignCenter)
    label.setFixedHeight(23)
    label.setMinimumWidth(104)
    # Label stays neutral grey across ALL seven themes, per user request.
    label.setStyleSheet('QLabel#v08SaveKindBadge { color:#e2e0e6; background:#46454d;'
                        'border:1px solid #77747e; border-radius:5px; padding:1px 8px;'
                        'font-size:11px; font-weight:600; }')
    label.setToolTip('Save classification is a profile label, not proof of randomized gameplay.')
    return label


def wrap_choose_another_with_tag(choose_button, save_data, *, parent=None, toolbar_layout=None):
    """Return a compact widget with existing Choose Another button ABOVE tag.

    If toolbar_layout is provided and is a QBoxLayout already containing the
    button, the wrapper REPLACES that button at the same layout index.
    Otherwise the caller adds the returned widget to its toolbar manually.
    The actual button retains its original signal connections.
    """
    from PyQt6.QtCore import Qt
    from PyQt6.QtWidgets import QVBoxLayout, QWidget, QBoxLayout
    at = -1
    if toolbar_layout is not None:
        if not isinstance(toolbar_layout, QBoxLayout):
            raise TypeError('toolbar_layout must be a QBoxLayout')
        at = toolbar_layout.indexOf(choose_button)
        if at >= 0:
            toolbar_layout.removeWidget(choose_button)
    wrapper = QWidget(parent)
    wrapper.setObjectName('v08ChooseAnotherContainer')
    layout = QVBoxLayout(wrapper)
    layout.setContentsMargins(0, 0, 0, 0)
    layout.setSpacing(5)
    layout.addWidget(choose_button)
    label = make_grey_save_tag(save_data, wrapper)
    layout.addWidget(label, 0, Qt.AlignmentFlag.AlignHCenter)
    if toolbar_layout is not None:
        if at >= 0:
            toolbar_layout.insertWidget(at, wrapper)
        else:
            toolbar_layout.addWidget(wrapper)
    return wrapper, label


def set_tag_data(label, save_data):
    """Refresh on profile switch without rebuilding the selected adventure."""
    label.setText(save_kind_badge_text(save_data))


@dataclass(frozen=True)
class Diagnostic:
    code: str
    severity: str    # warning or error
    title: str
    explanation: str
    resolution: str


KNOWN_WARNINGS = {
    'game_update_recommended': ('warning', 'Native build or update recommended',
                                'The existing native game may not match this source checkout or configuration.',
                                'Open Settings → Reinstall, choose Review/update installation, then rebuild/update and recheck readiness.'),
    'last_operation_error': ('warning', 'Previous native operation reported an error',
                             'The last build, shader, or pipeline operation reported a failure.',
                             'Open Settings → Tweaks → Shader / Pipeline Tools or the Build details panel, review the log, correct the reported cause, then retry.'),
    'game_validation_error': ('error', 'Game verification failed',
                              'The launcher identified an installation validation problem.',
                              'Open Settings → Reinstall to review and repair the installation. Check the specific validation details before rebuilding.'),
    'romfs_missing': ('error', 'Game assets not found',
                      'The configured RomFS directory cannot be read.',
                      'In Settings, choose the correct installed Hookshot game data directory, then retry.'),
    'manifest_missing': ('warning', 'Save icon mapping not found',
                         'The verified icon_manifest.json was not located.',
                         'Install or select your verified manifest. The launcher can continue using its built-in icons.'),
    'manifest_invalid': ('warning', 'Save icon manifest is invalid',
                         'The JSON could not be loaded or its icon references are malformed.',
                         'Check the verified icon_manifest.json against your local RomFS. The separate development-only browser can correct mappings; then relaunch.'),
    'icon_cache_failure': ('warning', 'Save assets could not be created',
                           'One or more verified game textures could not be decoded. Available icons still display normally.',
                           'For a missing texture2ddecoder module, use Install / Repair Icon Decoder here (private Python folder only), then Rebuild Save Icons. Verify individual icon mappings only if they still fail.'),
    'shader_pipeline_warning': ('warning', 'Shader or pipeline preparation incomplete',
                                'Some configured shader/pipeline preparation did not complete.',
                                'Open Settings → Tweaks → Shader / Pipeline Tools, review the preparation log, and rerun the failed operation.'),
    'game_not_installed': ('error', 'Game runtime is unavailable',
                           'Hookshot cannot locate the executable or required installed data.',
                           'In Settings, repair the game installation or select the correct runtime directory.'),
}


def diagnostic_from_code(code, *, details='', severity=None):
    """Return a diagnostic with actionable resolution and optional real details.

    Never invent an underlying error; callers supply known observed codes.
    Unknown errors show a generic action to inspect logs.
    """
    if code in KNOWN_WARNINGS:
        level, title, explanation, resolution = KNOWN_WARNINGS[code]
    else:
        level, title, explanation, resolution = (
            'warning', str(code).replace('_', ' ').strip().title() or 'Unrecognized issue',
            'The launcher reported a problem without a predefined resolution.',
            'Open the build/runtime diagnostics in Settings and inspect the detailed error log.')
    level = severity or level
    if level not in ('warning', 'error'):
        raise ValueError('Diagnostic severity must be warning or error')
    if details:
        explanation += '\nDetails: ' + str(details)
    return Diagnostic(str(code), level, title, explanation, resolution)


def readiness_from_diagnostics(diags, *, base_status='ready'):
    """Do not override the launcher's red/yellow/green hardware readiness.

    Optional save-icon fallback warnings must never change an otherwise ready
    game into a red launch failure.
    """
    base = str(base_status).lower()
    if base in ('red', 'error', 'failed'):
        return 'error'
    if any(d.severity == 'error' for d in diags):
        return 'error'
    if base in ('yellow', 'warning', 'warn') or any(d.severity == 'warning' for d in diags):
        return 'warning'
    return 'ready'


class DiagnosticsPanelFactory:
    """Lazily construct a Qt Warnings & Errors panel for the Settings window."""

    @staticmethod
    def create(diagnostics=(), parent=None, *, on_retry=None):
        from PyQt6.QtWidgets import QWidget, QVBoxLayout, QLabel, QScrollArea, QPushButton, QFrame
        from PyQt6.QtCore import Qt
        holder = QWidget(parent)
        column = QVBoxLayout(holder)
        column.setContentsMargins(10, 10, 10, 10)
        column.setSpacing(8)
        heading = QLabel('Warnings & Errors')
        heading.setStyleSheet('font-size: 15px; font-weight: bold;')
        column.addWidget(heading)
        guidance = QLabel('Each item explains what happened and how to clear it. Updates when you reopen Settings.')
        guidance.setWordWrap(True)
        column.addWidget(guidance)
        scroll = QScrollArea(holder)
        scroll.setWidgetResizable(True)
        scroll.setFrameShape(QFrame.Shape.NoFrame)
        inside = QWidget()
        items = QVBoxLayout(inside)
        items.setContentsMargins(2, 2, 2, 2)
        items.setSpacing(8)
        items.addStretch()
        scroll.setWidget(inside)
        column.addWidget(scroll, 1)

        def populate(new_diagnostics):
            while items.count():
                item = items.takeAt(0)
                if item.widget():
                    item.widget().deleteLater()
            entries = list(new_diagnostics)
            if not entries:
                item = QLabel('No active warnings or errors.')
                item.setStyleSheet('color:#99c9a4;')
                items.addWidget(item)
            for d in entries:
                card = QFrame(inside)
                card.setObjectName('v08DiagnosticCard')
                color = '#e8bd60' if d.severity == 'warning' else '#e58b8b'
                card.setStyleSheet(f'QFrame#v08DiagnosticCard {{ border: 1px solid {color}; border-radius: 6px; padding: 6px; }}')
                layout = QVBoxLayout(card)
                head = QLabel(('Warning' if d.severity == 'warning' else 'Error') + ' · ' + d.title)
                head.setStyleSheet(f'font-weight: 700; color: {color};')
                expl = QLabel(d.explanation)
                expl.setWordWrap(True)
                fix = QLabel('How to clear: ' + d.resolution)
                fix.setWordWrap(True)
                layout.addWidget(head)
                layout.addWidget(expl)
                layout.addWidget(fix)
                items.addWidget(card)
            items.addStretch()

        populate(diagnostics)
        holder.refresh_diagnostics = populate
        if on_retry is not None:
            retry = QPushButton('Retry Checks', holder)
            retry.clicked.connect(on_retry)
            column.addWidget(retry, 0, Qt.AlignmentFlag.AlignRight)
        return holder


def move_settings_to_header(main_tabs, header_layout, ready_widget, *, parent=None,
                            diagnostics_provider=None, on_retry=None):
    """Move existing Shader/Pipeline and Settings tabs into one Settings dialog.

    Call ONCE after main launcher has constructed main_tabs and header_layout.
    Existing widgets/signals are reparented; they are not recreated or copied.
    Ready indicator remains at top-right and a Settings button is inserted
    directly AFTER it. No '4/4' stage count or duplicate readiness is added.

    Return (settings_button, dialog, diagnostics_panel). The calling launcher
    must call panel.refresh_diagnostics(diagnostics_provider()) when readiness
    changes, or rely on the popup's refresh when it opens.
    """
    from PyQt6.QtWidgets import QDialog, QPushButton, QTabWidget, QVBoxLayout

    if parent is None:
        parent = main_tabs.window()
    lookup = {}
    for i in range(main_tabs.count()):
        name = main_tabs.tabText(i).strip().lower()
        if name == 'settings':
            lookup['settings'] = (i, main_tabs.widget(i))
        elif 'shader' in name or 'pipeline' in name:
            lookup['pipeline'] = (i, main_tabs.widget(i))
    if 'settings' not in lookup:
        raise ValueError('Existing Settings tab not found; launcher widget must be supplied')
    # Validate the target header BEFORE moving/removing any existing widgets.
    # Otherwise a caller that supplies the wrong header would lose two tabs.
    ready_index = header_layout.indexOf(ready_widget)
    if ready_index < 0:
        raise ValueError('Readiness widget is not in supplied header layout')
    # Remove highest index first, since removeTab shifts indexes.
    for index, _widget in sorted(lookup.values(), key=lambda pair: pair[0], reverse=True):
        main_tabs.removeTab(index)
    dialog = QDialog(parent)
    dialog.setWindowTitle('Hookshot Settings')
    dialog.setMinimumSize(650, 480)
    dialog.setObjectName('v08SettingsDialog')
    main_layout = QVBoxLayout(dialog)
    settings_tabs = QTabWidget(dialog)
    settings_tabs.setObjectName('v08SettingsTabs')
    settings_tabs.addTab(lookup['settings'][1], 'General')
    if 'pipeline' in lookup:
        settings_tabs.addTab(lookup['pipeline'][1], 'Shader / Pipeline Tools')
    warnings_panel = DiagnosticsPanelFactory.create((), dialog, on_retry=on_retry)
    settings_tabs.addTab(warnings_panel, 'Warnings & Errors')
    main_layout.addWidget(settings_tabs)
    button = QPushButton('Settings', parent)
    button.setObjectName('v08SettingsHeaderButton')
    # Caller can supply theme QSS to the top-level window; inherited by button.
    # The header can have been rearranged while building the dialog: query it
    # again without risking a mutation failure halfway through installation.
    index = header_layout.indexOf(ready_widget)
    header_layout.insertWidget(index + 1, button)

    def show_settings():
        if diagnostics_provider is not None:
            warnings_panel.refresh_diagnostics(diagnostics_provider())
        dialog.setStyleSheet(parent.styleSheet() if hasattr(parent, 'styleSheet') else '')
        dialog.show()
        dialog.raise_()
        dialog.activateWindow()

    button.clicked.connect(show_settings)
    dialog._tabs = settings_tabs
    return button, dialog, warnings_panel
