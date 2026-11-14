"""Hookshot's guided game-builder interface (PyQt6).

Run from a source checkout: python builder/qt/hookshot_qt.py [--Reinstall]
The underlying nx2/make_game.py is unchanged.  The startup --Reinstall switch
only bypasses the GUI's existing-install shortcut; the native compiler's
incremental object cache remains available.
"""
from __future__ import annotations

import argparse
import os
import shutil
from pathlib import Path
import sys
import time

# Optional project-local decoder libraries. No virtual environment is created
# or required. This directory is not included in release distributions.
_LOCAL_DEPS = Path(__file__).resolve().parent / '.local-python'
if _LOCAL_DEPS.is_dir() and str(_LOCAL_DEPS) not in sys.path:
    sys.path.insert(0, str(_LOCAL_DEPS))

from PyQt6.QtCore import QProcess, QProcessEnvironment, QSettings, Qt, QTimer
from PyQt6.QtGui import QColor, QIcon, QPixmap
from PyQt6.QtWidgets import (
    QApplication, QCheckBox, QComboBox, QFileDialog, QFormLayout, QFrame,
    QHBoxLayout, QLabel, QLineEdit, QMainWindow, QDialog, QMessageBox, QPlainTextEdit,
    QProgressBar, QPushButton, QScrollArea, QSpinBox, QStackedWidget, QGridLayout,
    QGraphicsDropShadowEffect,
    QTabWidget, QVBoxLayout, QWidget,
)

# Works both when invoked as a file and imported as a module by tests.
try:
    from .build_flow import build_state, code_stamp, inspect_game, load_manifest, source_stamp, write_manifest
    from .build_report import BuildReport, display as stat_display
    from .vulkan_support import find_cache
    from .runtime_profile import write_runtime_profile, runtime_env_overrides, detect_nvidia_vram_mb
    from .profile_store import ProfileStore, ProfileError, default_hookshot_user, register_installed_saves, hookshot_is_running
    from .profile_gui import Window as AdventureWindow, STYLESHEET as ADVENTURE_STYLESHEET
    from .pipeline_warmup import write_warmup_env, BLOCKED_FLAGS, PREWARM_FLAGS, state_counts
    from .ui_layout import fit_window
    from .ui_theme import apply_theme, populate_theme_combo
    from .save_reader import read_adventure_slots
    from .readiness import assess_readiness
    from .zelda_summary_widget import ZeldaStatsWidget
    from .adventure_save_display import format_save_location, readable_age, save_header_location_age, save_kind_badge_text
    from .adventure_v08_settings_ui import (make_grey_save_tag, set_tag_data,
        style_selected_save_info, diagnostic_from_code, DiagnosticsPanelFactory)
    from .adventure_panel_model import safe_character_name
    from .save_asset_cache import SaveAssetCache
    from .adventure_asset_progress import ensure_save_assets
    from .ui_theme import selected_theme
    from .setup_save_icon_decoder import activate_private_directory
except ImportError:
    from build_flow import build_state, code_stamp, inspect_game, load_manifest, source_stamp, write_manifest
    from build_report import BuildReport, display as stat_display
    from vulkan_support import find_cache
    from runtime_profile import write_runtime_profile, runtime_env_overrides, detect_nvidia_vram_mb
    from profile_store import ProfileStore, ProfileError, default_hookshot_user, register_installed_saves, hookshot_is_running
    from profile_gui import Window as AdventureWindow, STYLESHEET as ADVENTURE_STYLESHEET
    from pipeline_warmup import write_warmup_env, BLOCKED_FLAGS, PREWARM_FLAGS, state_counts
    from ui_layout import fit_window
    from ui_theme import apply_theme, populate_theme_combo
    from save_reader import read_adventure_slots
    from readiness import assess_readiness
    from zelda_summary_widget import ZeldaStatsWidget
    from adventure_save_display import format_save_location, readable_age, save_header_location_age, save_kind_badge_text
    from adventure_v08_settings_ui import (make_grey_save_tag, set_tag_data,
        style_selected_save_info, diagnostic_from_code, DiagnosticsPanelFactory)
    from adventure_panel_model import safe_character_name
    from save_asset_cache import SaveAssetCache
    from adventure_asset_progress import ensure_save_assets
    from ui_theme import selected_theme
    from setup_save_icon_decoder import activate_private_directory

ROOT = Path(__file__).resolve().parents[2]
SCRIPT = ROOT / 'nx2' / 'make_game.py'
VULKAN_SETUP = Path(__file__).with_name('vulkan_support.py')
STAGES = {'read': 'Reading game files', 'lift': 'Translating game code',
          'compile': 'Compiling native game', 'finish': 'Assembling installation',
          'prepare': 'Preparing shaders and pipelines', 'shaders': 'Preparing shaders and pipelines',
          'pipeline': 'Preparing shaders and pipelines'}


def startup_reinstall(argv=None):
    parser = argparse.ArgumentParser(add_help=False)
    parser.add_argument('--Reinstall', '--reinstall', action='store_true', dest='reinstall')
    return parser.parse_known_args(argv)[0].reinstall


def default_jobs():
    return max(1, (os.cpu_count() or 1) - 2)


def tag(text, status='muted'):
    label = QLabel(text)
    label.setProperty('status', status)
    label.setObjectName('tag')
    return label


class Window(QMainWindow):
    def __init__(self, reinstall=False):
        super().__init__()
        self.setWindowTitle('Hookshot — Adventure Launcher')
        self.setWindowIcon(QIcon(str(ROOT / 'assets' / 'hookshot.png')))
        # One primary window for all four steps. Scale to the current *logical*
        # screen geometry, including Steam Deck's 1280x800 desktop / HiDPI mode.
        fit_window(self, preferred=(1000, 640), minimum=(750, 475))
        self.settings = QSettings('Hookshot', 'QtBuilder')
        self.profile_store = None
        self.adventure_window = None
        self.profile_error = ''
        try:
            self.profile_store = ProfileStore()
        except (OSError, ValueError, ProfileError) as err:
            self.profile_error = str(err)
        self.process = QProcess(self)
        self.process.setProcessChannelMode(QProcess.ProcessChannelMode.MergedChannels)
        self.process.readyReadStandardOutput.connect(self.read_output)
        self.process.finished.connect(self.finished)
        self.process.errorOccurred.connect(self.process_error)
        self.buffer = ''
        self.started = 0.0
        self.current_stage = ''
        self.changed = 0
        self.compiled = 0
        self.eta = None
        self.last_error = ''
        self.task = 'idle'
        self.cancel_requested = False
        self.reinstall_from_cli = reinstall
        self.update_requested = False
        self.install_requested = False
        self.build_completed = False
        self.preparation_message = ''
        self.report = BuildReport()
        self.dependency_cache = None
        self.after_dependency_setup = 'build'
        self.extraction_message = 'Waiting for source verification'
        self.install_before_build = None
        self.validation_issue = ''
        self.operation_source = None
        self.operation_language = None
        self.operation_code = None
        self.operation_action = ''
        self.code_version = None
        self.phase = 'builder'
        self.sequence_step = 'source'
        self.switching_dialog = False
        self.active_dialog = self  # Message boxes and profile callbacks have a stable owner.
        self.dialogs = {}   # Legacy member retained for third-party UI integrations.
        self._exiting = False
        self.state_mode = 'needs_source'
        self.state_reason = ''
        self._save_asset_cache = None
        self._save_asset_issue = ''
        self._save_assets_started = False
        self._decoder_setup_process = None
        self._readiness_explanation = ''
        self.setup_ui()
        self.setup_sequence_dialogs()
        self.clock = QTimer(self)
        self.clock.timeout.connect(self.tick)
        self.clock.start(1000)
        # Let the window appear before detecting an already installed game.
        QTimer.singleShot(0, self.initial_check)

    def setup_ui(self):
        body = QWidget()
        body.setObjectName('launcherRoot')
        self.setCentralWidget(body)
        outer = QVBoxLayout(body)
        outer.setContentsMargins(14, 10, 14, 9)
        outer.setSpacing(7)

        header = QHBoxLayout()
        logo = QLabel()
        logo.setObjectName('brandMark')
        logo.setFixedSize(38, 38)
        logo.setAlignment(Qt.AlignmentFlag.AlignCenter)
        pix = QPixmap(str(ROOT / 'assets' / 'hookshot.png'))
        if not pix.isNull():
            logo.setPixmap(pix.scaled(34, 34, Qt.AspectRatioMode.KeepAspectRatio,
                                      Qt.TransformationMode.SmoothTransformation))
        header.addWidget(logo)
        titles = QVBoxLayout()
        titles.setSpacing(0)
        main = QLabel('HOOKSHOT')
        main.setObjectName('heading')
        titles.addWidget(main)
        sub = QLabel('Adventure launcher  /  Game management')
        sub.setObjectName('subheading')
        titles.addWidget(sub)
        header.addLayout(titles, 1)
        self.step_status = QLabel('01 / 03  ·  GAME FILES')
        self.step_status.setObjectName('stepStatus')
        header.addWidget(self.step_status)
        self.step_status.hide()  # installer-only; never show a 4/4 on Adventure Select
        # The Settings control sits immediately LEFT of the only readiness
        # indicator. Both use identical theme-defined pill geometry.
        self.settings_header_button = QPushButton('Settings')
        self.settings_header_button.setObjectName('settingsHeaderTag')
        self.settings_header_button.setToolTip('Appearance, reinstall, launch tweaks, builder and pipeline tools, and diagnostics')
        self.settings_header_button.setFixedSize(112, 30)
        self.settings_header_button.clicked.connect(self.toggle_header_settings)
        header.addWidget(self.settings_header_button)
        self.install_tag = tag('Checking installation…')
        self.install_tag.setAlignment(Qt.AlignmentFlag.AlignCenter)
        self.install_tag.setFixedSize(152, 30)
        header.addWidget(self.install_tag)
        self.install_tag.setCursor(Qt.CursorShape.PointingHandCursor)
        self.install_tag.mousePressEvent = lambda event: self.open_warnings_settings()
        outer.addLayout(header)

        self.tabs = QTabWidget()
        self.tabs.setDocumentMode(True)
        outer.addWidget(self.tabs, 1)
        build_page = QWidget()
        self.tabs.addTab(build_page, 'Adventures')
        page_layout = QVBoxLayout(build_page)
        page_layout.setContentsMargins(2, 7, 2, 2)
        page_layout.setSpacing(7)
        self.wizard_stack = QStackedWidget()
        page_layout.addWidget(self.wizard_stack, 1)

        self.make_builder_page()
        self.make_launch_page()
        self.make_adventure_detail_page()

        footer = QFrame()
        footer.setObjectName('footer')
        bottom = QVBoxLayout(footer)
        bottom.setContentsMargins(10, 7, 10, 7)
        bottom.setSpacing(5)
        strip = QHBoxLayout()
        strip.addWidget(QLabel('OVERALL PROGRESS'))
        self.overall_caption = QLabel('Ready · stage-based estimate')
        self.overall_caption.setObjectName('muted')
        strip.addStretch()
        strip.addWidget(self.overall_caption)
        bottom.addLayout(strip)
        self.overall_bar = QProgressBar()
        self.overall_bar.setRange(0, 100)
        self.overall_bar.setValue(0)
        self.overall_bar.setTextVisible(True)
        bottom.addWidget(self.overall_bar)
        actions = QHBoxLayout()
        self.footer_notice = QLabel('Choose game source files, or use an existing installation.')
        self.footer_notice.setWordWrap(True)
        self.footer_notice.setObjectName('muted')
        actions.addWidget(self.footer_notice, 1)
        self.cancel_button = QPushButton('Cancel')
        self.cancel_button.setObjectName('secondaryButton')
        self.cancel_button.clicked.connect(self.cancel)
        self.primary_button = QPushButton('OK · Import & Build')
        self.primary_button.setObjectName('primaryButton')
        self.primary_button.clicked.connect(self.primary_action)
        actions.addWidget(self.cancel_button)
        actions.addWidget(self.primary_button)
        bottom.addLayout(actions)
        page_layout.addWidget(footer)
        self.footer = footer

        self.setup_shader_tools()
        # Launcher and tool preferences have a single home in Settings; the
        # adventure manager has no duplicate theme or runtime controls.
        self.setup_settings_tab()
        # Header navigation replaces the former top-level Shader/Pipeline and Settings tabs.
        self.tabs.tabBar().hide()
        self.tabs.currentChanged.connect(self._header_page_changed)
        apply_theme(persist=False)
        self.statusBar().showMessage('Ready')
        self.refresh_state()

    def make_builder_page(self):
        scroll = QScrollArea()
        scroll.setWidgetResizable(True)
        scroll.setFrameShape(QFrame.Shape.NoFrame)
        content = QWidget()
        layout = QVBoxLayout(content)
        layout.setContentsMargins(3, 1, 3, 3)
        layout.setSpacing(7)
        scroll.setWidget(content)
        self.wizard_stack.addWidget(scroll)

        source, s = self.make_step('01', 'Game source', 'SELECT SOURCE', 'The prompts below follow your selected format.')
        form = QFormLayout()
        form.setLabelAlignment(Qt.AlignmentFlag.AlignRight)
        self.kind = QComboBox()
        self.kind.addItem('NUP package (recommended)', 'nup')
        self.kind.addItem('Extracted romfs + exefs directories', 'extracted')
        self.kind.currentIndexChanged.connect(self.source_changed)
        form.addRow('Source format', self.kind)
        s.addLayout(form)
        self.nup_group = QWidget()
        nup_form = QFormLayout(self.nup_group)
        nup_form.setContentsMargins(0, 0, 0, 0)
        self.nup = self.path_field(nup_form, 'NUP file', 'nup', False,
                                   'Nintendo update package (*.nup);;All files (*)')
        s.addWidget(self.nup_group)
        self.extracted_group = QWidget()
        extract_form = QFormLayout(self.extracted_group)
        extract_form.setContentsMargins(0, 0, 0, 0)
        self.romfs = self.path_field(extract_form, 'romfs directory', 'romfs', True)
        self.exefs = self.path_field(extract_form, 'exefs directory', 'exefs', True)
        s.addWidget(self.extracted_group)
        self.install_message = QLabel('Checking for an existing installation…')
        self.install_message.setWordWrap(True)
        self.install_message.setObjectName('hint')
        s.addWidget(self.install_message)
        extraction_row = QHBoxLayout()
        extraction_row.addWidget(QLabel('EXTRACTION STATUS'))
        self.extraction_state = QLabel('Not started')
        self.extraction_state.setObjectName('muted')
        extraction_row.addStretch()
        extraction_row.addWidget(self.extraction_state)
        s.addLayout(extraction_row)
        self.extraction_bar = QProgressBar()
        self.extraction_bar.setRange(0, 100)
        self.extraction_bar.setValue(0)
        self.extraction_bar.setFormat('Waiting for extraction')
        s.addWidget(self.extraction_bar)
        self.extraction_hint = QLabel('The source importer reports processed files here.')
        self.extraction_hint.setObjectName('muted')
        s.addWidget(self.extraction_hint)
        self.play_existing_button = QPushButton('Play existing installation without rebuilding')
        self.play_existing_button.setObjectName('secondaryButton')
        self.play_existing_button.clicked.connect(lambda: self.show_launch_summary(existing=True))
        s.addWidget(self.play_existing_button, alignment=Qt.AlignmentFlag.AlignLeft)
        self.source_repair_button = QPushButton('Repair existing Vulkan renderer (no full game build)')
        self.source_repair_button.setObjectName('secondaryButton')
        self.source_repair_button.clicked.connect(self.repair_renderer)
        self.source_repair_button.setVisible(False)
        s.addWidget(self.source_repair_button, alignment=Qt.AlignmentFlag.AlignLeft)
        layout.addWidget(source)
        self.source_card = source

        options, o = self.make_step('02', 'Build configuration', 'SYSTEM SETTINGS',
                                     'Configure the native build before continuing.')
        opts = QFormLayout()
        self.out = self.path_field(opts, 'Install directory', 'out', True)
        if not self.out.text().strip():
            self.out.setText(str(Path.home() / 'Games' / 'Hookshot'))
        self.language = QComboBox()
        for name, code in [('English (US)', 'en-US'), ('Japanese', 'ja-JP'), ('English (UK)', 'en-GB')]:
            self.language.addItem(name, code)
        idx = self.language.findData(self.settings.value('language', 'en-US'))
        if idx >= 0:
            self.language.setCurrentIndex(idx)
        self.language.currentIndexChanged.connect(self.refresh_state)
        opts.addRow('Game language', self.language)
        jobs_panel = QWidget()
        jobs_row = QHBoxLayout(jobs_panel)
        jobs_row.setContentsMargins(0, 0, 0, 0)
        self.jobs = QSpinBox()
        self.jobs.setRange(1, max(1, os.cpu_count() or 1))
        # Old Qt builder versions stored their 12-thread default as a setting.
        # Do not mistake that legacy default for a deliberate user override.
        saved = (self.settings.value('jobs', None) if
                 self.settings.value('jobs_user_override', False, type=bool) else None)
        try:
            selected = int(saved) if saved is not None else default_jobs()
        except (ValueError, TypeError):
            selected = default_jobs()
        self.jobs.setValue(max(1, min(self.jobs.maximum(), selected)))
        self.jobs.valueChanged.connect(lambda _: self.settings.setValue('jobs_user_override', True))
        jobs_row.addWidget(self.jobs)
        self.job_hint = QLabel(f'{os.cpu_count() or 1} logical threads · recommended {default_jobs()} (2 reserved)')
        self.job_hint.setObjectName('muted')
        jobs_row.addWidget(self.job_hint, 1)
        opts.addRow('Parallel build jobs', jobs_panel)
        o.addLayout(opts)
        self.keep_build_files = QCheckBox('Retain compiler objects for faster updates')
        self.keep_build_files.setChecked(self.settings.value('keep_build_files', True, type=bool))
        self.auto_prepare = QCheckBox('Prepare shaders and pipelines during the native build')
        self.auto_prepare.setChecked(self.settings.value('auto_prepare', True, type=bool))
        self.auto_prepare.toggled.connect(lambda yes: self.settings.setValue('auto_prepare', yes))
        self.reinstall = QCheckBox('Reinstall even when an existing game matches')
        self.reinstall.setChecked(self.reinstall_from_cli)
        self.reinstall.toggled.connect(self.reinstall_changed)
        # Reinstall is initiated only from Settings -> Reinstall.
        hint = QLabel('Build options are also available under Settings → Tweaks → Builder')
        hint.setObjectName('muted')
        o.addWidget(hint)
        self.update_button = QPushButton('Update installed game from retained files')
        self.update_button.setObjectName('secondaryButton')
        self.update_button.clicked.connect(self.request_update)
        o.addWidget(self.update_button, alignment=Qt.AlignmentFlag.AlignLeft)
        layout.addWidget(options)
        self.config_card = options

        build, b = self.make_step('03', 'Build status', 'LIVE OUTPUT',
                                  'Extraction, translation, compilation and installation progress.')
        self.stage = QLabel('Ready to build')
        self.stage.setObjectName('stageTitle')
        b.addWidget(self.stage)
        self.bar = QProgressBar()
        self.bar.setRange(0, 100)
        self.bar.setValue(0)
        b.addWidget(self.bar)
        self.details = QLabel('No build running')
        self.details.setObjectName('muted')
        b.addWidget(self.details)
        self.build_extraction_status = QLabel('EXTRACTION · Waiting for build')
        self.build_extraction_status.setObjectName('muted')
        b.addWidget(self.build_extraction_status)
        self.build_extraction_bar = QProgressBar()
        self.build_extraction_bar.setRange(0, 100)
        self.build_extraction_bar.setValue(0)
        self.build_extraction_bar.setFormat('Extraction not started')
        b.addWidget(self.build_extraction_bar)
        self.log = QPlainTextEdit()
        self.log.setReadOnly(True)
        self.log.setMinimumHeight(120)
        self.log.setPlaceholderText('Import, lift and compiler output appears here…')
        b.addWidget(self.log)
        self.repair_button = QPushButton('Rebuild Vulkan renderer only (no game retranslation)')
        self.repair_button.setObjectName('secondaryButton')
        self.repair_button.setVisible(sys.platform.startswith('linux'))
        self.repair_button.clicked.connect(self.repair_renderer)
        b.addWidget(self.repair_button, alignment=Qt.AlignmentFlag.AlignLeft)
        layout.addWidget(build)
        self.build_card = build
        layout.addStretch(0)
        self.kind.setCurrentIndex(0)  # NUP is always the default format.
        self.source_changed()

    def make_step(self, number, title, eyebrow, description):
        card = QFrame()
        card.setObjectName('stepCard')
        vertical = QVBoxLayout(card)
        vertical.setContentsMargins(12, 10, 12, 10)
        vertical.setSpacing(7)
        header = QHBoxLayout()
        index = QLabel(number)
        index.setObjectName('stepNumber')
        index.setFixedSize(32, 32)
        index.setAlignment(Qt.AlignmentFlag.AlignCenter)
        header.addWidget(index)
        labels = QVBoxLayout()
        labels.setSpacing(1)
        label = QLabel(title)
        label.setObjectName('stepTitle')
        labels.addWidget(label)
        desc = QLabel(description)
        desc.setObjectName('muted')
        desc.setWordWrap(True)
        labels.addWidget(desc)
        header.addLayout(labels, 1)
        caption = tag(eyebrow)
        header.addWidget(caption)
        vertical.addLayout(header)
        divider = QFrame()
        divider.setFrameShape(QFrame.Shape.HLine)
        divider.setObjectName('rule')
        vertical.addWidget(divider)
        return card, vertical

    def make_launch_page(self):
        """Compact 3-tab launch page: adventure first; diagnostics stay available."""
        page = QWidget()
        layout = QVBoxLayout(page)
        layout.setContentsMargins(6, 4, 6, 4)
        layout.setSpacing(4)
        self.launch_tabs = QTabWidget()
        self.launch_tabs.setObjectName('launchTabs')
        self.launch_tabs.setDocumentMode(True)
        layout.addWidget(self.launch_tabs, 1)

        adventure = QWidget()
        adventure.setObjectName('launcherAdventurePage')
        av_layout = QVBoxLayout(adventure)
        av_layout.setContentsMargins(12, 10, 12, 10)
        av_layout.setSpacing(10)
        heading = QHBoxLayout()
        heading.setSpacing(12)
        heading_text = QVBoxLayout()
        heading_text.setSpacing(3)
        hero = QLabel('CHOOSE YOUR ADVENTURE')
        hero.setObjectName('readyTitle')
        heading_text.addWidget(hero)
        intro = QLabel('Three shortcuts. Select a save to review progress before launching.')
        intro.setObjectName('adventureIntro')
        intro.setWordWrap(True)
        heading_text.addWidget(intro)
        heading.addLayout(heading_text, 1)
        self.profile_manage_button = QPushButton('Manage profiles')
        self.profile_manage_button.setObjectName('secondaryButton')
        self.profile_manage_button.setMinimumWidth(125)
        self.profile_manage_button.clicked.connect(self.open_adventure_profiles)
        heading.addWidget(self.profile_manage_button, 0, Qt.AlignmentFlag.AlignTop)
        av_layout.addLayout(heading)
        # Three fixed profile shortcuts, not Nintendo's internal game save slots.
        # The rows only open details; Launch Game remains on the detail page.
        # Avoid the former double-border wrapper around all three cards.
        adventure_card = QWidget()
        adventure_card.setObjectName('adventureList')
        adventure_layout = QVBoxLayout(adventure_card)
        # Space between the three floating cards prevents drop shadows from
        # merging with one another or clipping against the list's edges.
        adventure_layout.setContentsMargins(12, 8, 12, 10)
        adventure_layout.setSpacing(12)
        # Steam Deck baseline: a maximized desktop window must not stretch
        # these three fixed-height previews into enormous empty cards.
        adventure_card.setMaximumWidth(1180)
        self.launcher_boxes = []
        self.launcher_type_badges = []
        self.launcher_previews = []
        self.launcher_icon_summaries = []
        for i in range(3):
            card = QFrame()
            card.setObjectName('profileCardFrame')
            card.setFixedHeight(120)
            # QSS handles the colored convex gradient and bevel. Qt stylesheets
            # have no box-shadow property, so real depth requires a graphics
            # effect owned by each card (never by the surrounding list).
            shadow = QGraphicsDropShadowEffect(card)
            shadow.setBlurRadius(22)
            shadow.setOffset(0, 5)
            shadow.setColor(QColor(0, 0, 0, 135))
            card.setGraphicsEffect(shadow)
            row = QHBoxLayout(card)
            row.setContentsMargins(12, 7, 12, 7)
            row.setSpacing(12)
            preview = QLabel('No save image')
            preview.setObjectName('adventureSavePreview')
            preview.setAlignment(Qt.AlignmentFlag.AlignCenter)
            preview.setFixedSize(156, 88)
            row.addWidget(preview, 0)
            text = QVBoxLayout()
            text.setSpacing(2)
            title_row = QHBoxLayout()
            title_row.setSpacing(8)
            slot_label = QLabel(f'{i + 1:02d}')
            slot_label.setObjectName('adventureSlotLabel')
            slot_label.setFixedWidth(25)
            slot_label.setAlignment(Qt.AlignmentFlag.AlignCenter)
            title_row.addWidget(slot_label)
            title = QLabel('New Adventure')
            title.setObjectName('adventureCardTitle')
            title.setTextFormat(Qt.TextFormat.PlainText)
            title.setMinimumWidth(0)
            title_row.addWidget(title, 1)
            text.addLayout(title_row)
            info = QLabel('')  # compatibility with historical launcher box tuples
            info.setObjectName('adventureEmptyHint')
            info.hide()  # shown only for empty shortcuts, not duplicated over save icons
            summary = ZeldaStatsWidget(compact=True)
            summary.setObjectName('adventureIconSummary')
            text.addWidget(info)
            text.addWidget(summary)
            row.addLayout(text, 1)
            select = QPushButton('Create')
            select.setObjectName('adventureSelectButton')
            select.setMinimumWidth(104)
            select.setFixedHeight(34)
            select.clicked.connect(lambda checked=False, index=i: self.activate_launcher_box(index))
            button_column = QVBoxLayout()
            button_column.setContentsMargins(0, 0, 0, 0)
            button_column.setSpacing(7)
            badge = make_grey_save_tag({}, card)
            badge.hide()
            button_column.addWidget(badge)
            button_column.addWidget(select)
            button_column.addStretch(1)
            row.addLayout(button_column)
            self.launcher_type_badges.append(badge)
            adventure_layout.addWidget(card, 0)
            self.launcher_previews.append(preview)
            self.launcher_boxes.append((card, title, info, select))
            self.launcher_icon_summaries.append(summary)
        adventure_layout.addStretch(1)
        av_layout.addWidget(adventure_card, 0, Qt.AlignmentFlag.AlignTop | Qt.AlignmentFlag.AlignHCenter)
        # Retain the existing internal QComboBox as the launch-command selection
        # model, but never render it. Every action still uses its selected id.
        self.profile_combo = QComboBox()
        self.profile_combo.currentIndexChanged.connect(self.profile_choice_changed)
        self.profile_hint = QLabel('')
        self.active_save_root = QLabel('')
        self.active_slot_info = QLabel('')
        self.ready_intro = QLabel('')
        self.new_profile_button = QPushButton('')
        self.new_profile_button.clicked.connect(self.create_adventure_profile)
        av_layout.addStretch()
        self.launch_tabs.addTab(adventure, 'Adventure')

        runtime = QWidget()
        runtime_layout = QVBoxLayout(runtime)
        runtime_layout.setContentsMargins(10, 8, 10, 8)
        runtime_layout.setSpacing(7)
        runtime_layout.addWidget(QLabel('VULKAN LAUNCH PROFILE'))
        self.launch_precompile = QCheckBox('Precompile known shader pipelines before gameplay')
        self.launch_precompile.setChecked(self.settings.value('runtime_precompile', True, type=bool))
        self.launch_precompile.toggled.connect(lambda enabled: self.settings.setValue('runtime_precompile', enabled))
        self.launch_precompile.setToolTip('RX_VK_PRECOMPILE=1. May reduce first-use pipeline stalls.')
        vram_mb = detect_nvidia_vram_mb()
        self.launch_texture_headroom = QCheckBox('Terrain texture protection (experimental 10 GB budget)')
        self.launch_texture_headroom.setChecked(self.settings.value('runtime_terrain', vram_mb is not None and vram_mb >= 20 * 1024, type=bool))
        self.launch_texture_headroom.toggled.connect(lambda enabled: self.settings.setValue('runtime_terrain', enabled))
        self.launch_texture_headroom.setToolTip('For GPUs with 20 GB or more VRAM, e.g. RTX 4090. Experimental: not a complete terrain-texture fix.')
        self.launch_runtime_hint = QLabel('Shader pipeline and terrain preferences are managed centrally in Settings → Tweaks → Game Launch. They apply to launches through this builder only.')
        self.launch_runtime_hint.setObjectName('muted')
        self.launch_runtime_hint.setWordWrap(True)
        runtime_layout.addWidget(self.launch_runtime_hint)
        configure = QPushButton('Edit launch settings…')
        configure.setObjectName('secondaryButton')
        configure.clicked.connect(self.open_launch_settings)
        runtime_layout.addWidget(configure, alignment=Qt.AlignmentFlag.AlignLeft)
        runtime_layout.addStretch()
        # The launch controls are on Settings → Tweaks → Game Launch, not duplicated here.
        self.runtime_information_page = runtime

        diagnostics = QWidget()
        diag_layout = QVBoxLayout(diagnostics)
        diag_layout.setContentsMargins(10, 9, 10, 10)
        diag_layout.setSpacing(8)
        stat_grid = QGridLayout()
        stat_grid.setSpacing(6)
        self.launch_metrics = {}
        fields = (('game', 'GAME EXECUTABLE'), ('renderer', 'VULKAN VIDEO'),
                  ('shaders', 'SHADER ARCHIVE'), ('preparation', 'PIPELINE PREP'),
                  ('changed', 'MODULES REBUILT'), ('time', 'BUILD DURATION'))
        for pos, (key, title) in enumerate(fields):
            tile = QFrame()
            tile.setObjectName('statTile')
            cell = QVBoxLayout(tile)
            cell.setContentsMargins(8, 6, 8, 6)
            cell.setSpacing(2)
            caption = QLabel(title)
            caption.setObjectName('statCaption')
            value = QLabel('Not reported')
            value.setObjectName('statValue')
            value.setWordWrap(True)
            detail = QLabel('')
            detail.setObjectName('statDetail')
            detail.setWordWrap(True)
            cell.addWidget(caption)
            cell.addWidget(value)
            cell.addWidget(detail)
            stat_grid.addWidget(tile, pos // 2, pos % 2)
            self.launch_metrics[key] = (value, detail)
        diag_layout.addLayout(stat_grid)
        self.summary = QPlainTextEdit()
        self.summary.setObjectName('summaryText')
        self.summary.setReadOnly(True)
        self.summary.setMinimumHeight(65)
        diag_layout.addWidget(self.summary, 1)
        actions = QHBoxLayout()
        review = QPushButton('Reinstall / repair settings')
        review.setObjectName('secondaryButton')
        review.clicked.connect(self.open_reinstall_settings)
        actions.addWidget(review)
        self.launch_repair_button = QPushButton('Repair Vulkan renderer')
        self.launch_repair_button.setObjectName('secondaryButton')
        self.launch_repair_button.setVisible(sys.platform.startswith('linux'))
        self.launch_repair_button.clicked.connect(self.repair_renderer)
        actions.addWidget(self.launch_repair_button)
        diag_layout.addLayout(actions)
        self.launch_tabs.addTab(diagnostics, 'Build details')
        self.wizard_stack.addWidget(page)
        self.launch_page = page

    def make_adventure_detail_page(self):
        """SoH-inspired save panel; navigation and Launch remain outside."""
        page = QWidget()
        page.setObjectName('launcherDetailPage')
        layout = QVBoxLayout(page)
        layout.setContentsMargins(8, 5, 8, 5)
        layout.setSpacing(6)

        navigation = QHBoxLayout()
        self.detail_name = QLabel('Adventure')
        self.detail_name.setObjectName('adventureDetailTitle')
        navigation.addWidget(self.detail_name, 1)
        another = QPushButton('Choose Another Adventure')
        another.setObjectName('secondaryButton')
        another.clicked.connect(lambda: self.show_sequence('launch'))
        navigation.addWidget(another)
        layout.addLayout(navigation)

        # One themed save-info frame. The original SoH file screen shows
        # life/quest on the left and the item matrix on the right; our game
        # screenshot and identity are added to a compact header inside it.
        self.detail_save_info_frame = QFrame(page)
        style_selected_save_info(self.detail_save_info_frame, self._selected_theme_label())
        save_layout = QVBoxLayout(self.detail_save_info_frame)
        # 10 px interior gutter makes the recessed quest/inventory tray almost
        # edge-to-edge, even at a 1000x640 Steam Deck-sized window.
        save_layout.setContentsMargins(10, 10, 10, 10)
        save_layout.setSpacing(7)
        card_shadow = QGraphicsDropShadowEffect(self.detail_save_info_frame)
        card_shadow.setBlurRadius(22)
        card_shadow.setOffset(0, 5)
        card_shadow.setColor(QColor(0, 0, 0, 155))
        self.detail_save_info_frame.setGraphicsEffect(card_shadow)

        identity = QHBoxLayout()
        identity.setSpacing(12)
        # One recessed screenshot well, not nested frames. The dark upper/left
        # rim and lighter lower/right rim are painted on this single shell;
        # the image itself has no bevel or shadow.
        self.detail_preview_shell = QFrame(self.detail_save_info_frame)
        self.detail_preview_shell.setObjectName('detailPreviewShell')
        bezel = QVBoxLayout(self.detail_preview_shell)
        bezel.setContentsMargins(9, 9, 9, 11)
        bezel.setSpacing(0)
        self.detail_preview = QLabel('No save image')
        self.detail_preview.setObjectName('adventureSavePreview')
        self.detail_preview.setAlignment(Qt.AlignmentFlag.AlignCenter)
        self.detail_preview.setFixedSize(186, 105)
        bezel.addWidget(self.detail_preview, 0, Qt.AlignmentFlag.AlignCenter)
        self.detail_preview_shell.setFixedSize(210, 130)
        # Never attach an outward shadow: this is a sunken surface.
        identity.addWidget(self.detail_preview_shell, 0, Qt.AlignmentFlag.AlignTop)

        info = QVBoxLayout()
        info.setContentsMargins(0, 0, 0, 0)
        info.setSpacing(2)
        head = QHBoxLayout()
        head.setSpacing(5)
        self.detail_location = QLabel('Location: Unknown')
        self.detail_location.setObjectName('sohSaveLocation')
        head.addWidget(self.detail_location, 1)
        self.detail_kind_badge = make_grey_save_tag({}, self.detail_save_info_frame)
        head.addWidget(self.detail_kind_badge, 0, Qt.AlignmentFlag.AlignRight)
        info.addLayout(head)
        self.detail_age = QLabel('Age: Unknown')
        self.detail_age.setObjectName('sohSaveMeta')
        info.addWidget(self.detail_age)
        self.detail_save = QLabel('Save Date: Not yet saved')
        self.detail_save.setWordWrap(True)
        self.detail_save.setObjectName('sohSaveMeta')
        info.addWidget(self.detail_save)
        info.addStretch(1)
        identity.addLayout(info, 1)
        save_layout.addLayout(identity)

        self.detail_visual = ZeldaStatsWidget(compact=False)
        self.detail_visual.setObjectName('adventureIconSummaryExpanded')
        save_layout.addWidget(self.detail_visual, 1)
        layout.addWidget(self.detail_save_info_frame, 1)

        actions = QHBoxLayout()
        self.detail_launch = QPushButton('Launch Game')
        self.detail_launch.setObjectName('primaryButton')
        self.detail_launch.clicked.connect(lambda checked=False: self.launch_game(profile_id=self.detail_profile_id))
        actions.addStretch(1)
        actions.addWidget(self.detail_launch)
        layout.addLayout(actions)
        self.wizard_stack.addWidget(page)
        self.adventure_detail_page = page
        self.detail_profile_id = None

    def resizeEvent(self, event):
        """Keep the screenshot and icon canvas visible on compact displays."""
        super().resizeEvent(event)
        if not hasattr(self, 'detail_preview'):
            return
        size = (186, 105) if self.height() >= 590 else (158, 89)
        if (self.detail_preview.width(), self.detail_preview.height()) != size:
            self.detail_preview.setFixedSize(*size)
            # Single shell; reserve the same 26px horizontal/31px vertical
            # surround when resizing for smaller Steam Deck windows.
            self.detail_preview_shell.setFixedSize(size[0] + 24, size[1] + 25)
            self.set_save_preview(self.detail_preview, self.detail_preview.property('source_jpeg'))

    @staticmethod
    def set_save_preview(label, preview_jpeg):
        """Present a caption.sav screenshot without creating or changing save data."""
        label.setProperty('source_jpeg', preview_jpeg)
        pix = QPixmap()
        if preview_jpeg and pix.loadFromData(preview_jpeg, 'JPEG'):
            label.setPixmap(pix.scaled(label.size(), Qt.AspectRatioMode.KeepAspectRatio,
                                        Qt.TransformationMode.SmoothTransformation))
            label.setText('')
            label.setToolTip('Screenshot embedded in the latest recognized save')
        else:
            label.clear()
            label.setText('No save image')
            label.setToolTip('No valid screenshot found in the latest recognized save')

    @staticmethod
    def adventure_title(profile_name, location, age=None):
        """Readable age and scene, without exposing internal save IDs."""
        name = str(profile_name).strip() or 'Link'
        return f'{name} - {format_save_location(location, age)}' if location else name

    @staticmethod
    def _save_metadata(stats, profile):
        """Use the user's explicit profile classification, not guessed save flags.

        A randomizer-planned seed does not enable randomized game rewards.
        The badge denotes how the save is labeled in the manager.
        """
        data = dict(stats or {})
        declared = profile.get('save_label')
        if declared in ('rando', 'vanilla'):
            data['save_label'] = declared
        return data

    @staticmethod
    def _selected_theme_label():
        return {'purple':'royal', 'zelda':'link',
                'zelda_gold':'tri force', 'blue_crystal':'ruto',
                'gannon':'gannon', 'zelda_pearl':'zelda', 'goron':'darunia'}.get(selected_theme(), 'royal')

    def show_adventure_details(self, profile_id):
        """Select explicitly, show save details, and never launch just from choosing."""
        if not self.profile_store:
            QMessageBox.warning(self, 'Profiles unavailable', self.profile_error)
            return
        try:
            profile = self.profile_store.get(profile_id)
        except (ValueError, ProfileError, OSError) as err:
            QMessageBox.warning(self, 'Adventure unavailable', str(err))
            return
        self.set_adventure_profile(profile_id)
        self.detail_profile_id = profile_id
        self.detail_name.setText(str(profile.get('name') or '').strip() or 'Link')
        slots = read_adventure_slots(Path(profile['user_dir']))
        valid = [row for row in slots if not row.warning]
        current = max(valid, key=lambda row: row.modified, default=None)
        stats = current.statistics if current else {}
        self.detail_visual.set_stats(stats)
        scene, age_label = save_header_location_age(stats.get('location'), stats.get('age'))
        self.detail_location.setText('Location: ' + scene)
        self.detail_age.setText('Age: ' + age_label)
        set_tag_data(self.detail_kind_badge, self._save_metadata(stats, profile))
        self.set_save_preview(self.detail_preview, current.preview_jpeg if current else None)
        if current and current.saved_in_game:
            self.detail_save.setText('Save Date: ' + current.saved_in_game)
            self.detail_save.setToolTip('Timestamp recorded by the game save')
        elif current:
            self.detail_save.setText('Save Date: Unknown')
            self.detail_save.setToolTip('Game save timestamp unavailable; file modified: ' +
                                        time.strftime('%Y-%m-%d %H:%M', time.localtime(current.modified)))
        else:
            self.detail_save.setText('Save Date: Not yet saved')
            self.detail_save.setToolTip('No decoded game progress yet')
        self.update_readiness()
        self.show_sequence('details')

    def update_readiness(self, warn=''):
        """Only ONE readiness badge appears, in the upper-right header.

        The installation wizard may show an installer step number, but the
        Adventures pages never display 4/4 or a second Ready to Play label.
        """
        if not warn and self.state_mode == 'build' and self.phase == 'launch':
            warn = 'Native build/update is recommended: ' + self.state_reason
        if self._save_asset_issue:
            warn = (warn + '; ' if warn else '') + 'save icons unavailable; original artwork is active'
        status = assess_readiness(inspect_game(self.out.text().strip()),
                                  self.validation_issue, warn)
        self.install_tag.setText(status.label)
        self.install_tag.setProperty('status', status.level)
        self.install_tag.setToolTip(status.explanation + '\nOpen Settings → Warnings & Errors for details.')
        self._readiness_explanation = status.explanation
        if hasattr(self, 'warnings_panel'):
            self.warnings_panel.refresh_diagnostics(self.current_diagnostics())
        self.install_tag.style().unpolish(self.install_tag)
        self.install_tag.style().polish(self.install_tag)
        self.step_status.setVisible(self.sequence_step in ('source', 'config', 'build'))
        self.detail_launch.setEnabled(status.can_launch)
        return status

    def setup_sequence_dialogs(self):
        """Convert legacy step cards into pages of ONE persistent builder window.

        The old UI used four separate native dialogs. Closing any but the first
        reopened Step 1. No top-level step dialogs are created anymore. All
        navigation happens inside the existing QStackedWidget, which also
        retains persistent QProcess, footer and profile selection state.
        """
        old_page = self.wizard_stack.widget(0)
        self.dialog_content_widgets = {'launch': self.launch_page, 'details': self.adventure_detail_page}
        for step, card in (('source', self.source_card),
                           ('config', self.config_card),
                           ('build', self.build_card)):
            old_parent = card.parentWidget()
            if old_parent is not None and old_parent.layout() is not None:
                old_parent.layout().removeWidget(card)
            page = QWidget()
            page.setObjectName('stepPage')
            vertical = QVBoxLayout(page)
            vertical.setContentsMargins(3, 3, 3, 3)
            vertical.setSpacing(5)
            card.setParent(page)
            vertical.addWidget(card)
            card.show()
            if step == 'build':
                # Log editor has its own viewport; the rest fits without a scrollbar.
                vertical.setStretch(0, 1)
            else:
                vertical.addStretch(1)
            self.wizard_stack.addWidget(page)
            self.dialog_content_widgets[step] = page
        self.wizard_stack.removeWidget(old_page)
        old_page.setParent(None)
        old_page.deleteLater()
        self.show_sequence('source')

    def start(self):
        """Show the single primary builder, not another step dialog."""
        super().show()

    def show(self):
        """Both CLI and programmatic launchers display one window."""
        self.start()

    def change_theme(self, _index):
        """Switch builder, dialogs, and profile manager without restarting."""
        apply_theme(self.theme_combo.currentData())
        if hasattr(self, 'detail_save_info_frame'):
            style_selected_save_info(self.detail_save_info_frame, self._selected_theme_label())
        self.detail_visual.refresh_hover_theme()
        for widget in self.launcher_icon_summaries:
            widget.update()

    def show_tools(self):
        """Shader tools share the main window so they cannot orphan the wizard."""
        self.tabs.setCurrentWidget(self.settings_page)
        self.settings_sections.setCurrentWidget(self.tweaks_page)
        self.tweaks_sections.setCurrentWidget(self.tool_page)
        super().show()
        self.raise_()
        self.activateWindow()

    def show_sequence(self, step):
        """One window. Installation pages appear only inside a requested install."""
        if step not in self.dialog_content_widgets:
            raise ValueError('Unknown Hookshot step: ' + str(step))
        self.wizard_stack.setCurrentWidget(self.dialog_content_widgets[step])
        self.tabs.setCurrentIndex(0)
        self.sequence_step = step
        installing = step in ('source', 'config', 'build')
        self.footer.setVisible(installing)
        self.tabs.setTabText(0, 'Install Hookshot' if installing else 'Adventures')
        self.step_status.setVisible(installing)
        if installing:
            self.step_status.setProperty('status', 'neutral')
            self.step_status.style().unpolish(self.step_status)
            self.step_status.style().polish(self.step_status)
            self.refresh_state()
            labels = {'source': '01 / 03  ·  GAME FILES',
                      'config': '02 / 03  ·  BUILD SETTINGS',
                      'build': '03 / 03  ·  INSTALLING'}
            self.step_status.setText(labels[step])
        else:
            self.update_readiness()
        self.update_step_buttons()

    def update_step_buttons(self):
        if self.busy():
            return
        if self.sequence_step in ('launch', 'details'):
            self.primary_button.setEnabled(False)
            self.cancel_button.setEnabled(False)
        elif self.sequence_step == 'source':
            self.primary_button.setText('Continue · Build Configuration')
            self.cancel_button.setText('Exit installation')
            self.primary_button.setEnabled(self.state_mode != 'needs_source')
            self.cancel_button.setEnabled(True)
        elif self.sequence_step == 'config':
            self.primary_button.setText('Start installation')
            self.cancel_button.setText('Previous step')
            self.primary_button.setEnabled(self.state_mode != 'needs_source')
            self.cancel_button.setEnabled(True)
        else:
            self.primary_button.setText('Retry installation')
            self.cancel_button.setText('Previous step')
            self.primary_button.setEnabled(self.state_mode == 'build')
            self.cancel_button.setEnabled(True)

    def path_field(self, form, title, key, directory, file_filter='All files (*)'):
        row = QWidget()
        h = QHBoxLayout(row)
        h.setContentsMargins(0, 0, 0, 0)
        h.setSpacing(8)
        field = QLineEdit(str(self.settings.value(key, '')))
        field.setPlaceholderText('Choose a directory…' if directory else 'Choose a game package…')
        field.setAcceptDrops(False)
        field.textChanged.connect(self.refresh_state)
        button = QPushButton('Browse…')
        button.setObjectName('secondaryButton')
        def browse():
            base = field.text().strip() or str(Path.home())
            if directory:
                result = QFileDialog.getExistingDirectory(self, title, base)
            else:
                result, _ = QFileDialog.getOpenFileName(self, title, base, file_filter)
            if result:
                field.setText(result)
        button.clicked.connect(browse)
        h.addWidget(field, 1)
        h.addWidget(button)
        form.addRow(title, row)
        return field

    def source_changed(self):
        nup_mode = self.kind.currentData() == 'nup'
        self.nup_group.setVisible(nup_mode)
        self.extracted_group.setVisible(not nup_mode)
        self.extraction_hint.setText(
            'NUP selected: the native importer extracts during step 03; extraction progress is displayed there.' if nup_mode else
            'Extracted folders: the native builder reuses your selected romfs/exefs directories.'
        )
        self.refresh_state()

    def reinstall_changed(self, checked):
        if checked:
            self.update_requested = False
        self.refresh_state()

    def request_update(self):
        self.update_requested = True
        self.reinstall.setChecked(False)
        self.return_to_builder()
        self.refresh_state()
        self.stage.setText('Update from retained files selected')

    def initial_check(self):
        if self.busy():
            return
        self.refresh_state()
        install = inspect_game(self.out.text().strip())
        if install.playable and not self.reinstall.isChecked():
            # A working older native game must not be labelled as a verified
            # 2.1.0 build just because the 2.1.0 GUI is installed.
            warning = self.state_reason if self.state_mode == 'build' else ''
            self.show_launch_summary(existing=True, warn=warning)
            # Delay expensive icon initialization until the main window is visible.
            if not self._save_assets_started:
                self._save_assets_started = True
                QTimer.singleShot(0, self.prepare_adventure_save_assets)
        else:
            self.show_sequence('source')

    def current_source(self):
        return source_stamp(self.kind.currentData(), self.nup.text(), self.romfs.text(), self.exefs.text())

    def refresh_state(self, *_):
        # UI construction connects textChanged before all controls exist.
        if not all(hasattr(self, n) for n in ('out', 'language', 'nup', 'romfs', 'exefs', 'reinstall',
                                              'install_message', 'install_tag', 'update_button', 'primary_button')):
            return
        install = inspect_game(self.out.text().strip())
        if self.code_version is None:
            self.code_version = code_stamp(ROOT)
        mode, reason = build_state(install, self.current_source(), self.language.currentData(),
                                   self.code_version, self.reinstall.isChecked(), self.update_requested)
        # Do not treat a broken/unavailable user-entered path as 'no source'.
        if not self.out.text().strip():
            mode, reason = 'needs_source', 'Select an installation directory.'
        elif self.kind.currentData() == 'nup' and self.nup.text().strip() and not self.current_source():
            mode, reason = 'needs_source', 'The selected NUP package cannot be read.'
        elif (self.kind.currentData() == 'extracted' and
              (self.romfs.text().strip() or self.exefs.text().strip()) and not self.current_source()):
            mode, reason = 'needs_source', 'Select valid romfs and exefs folders, including exefs/main.'
        self.state_mode, self.state_reason = mode, reason
        if self.sequence_step in ('launch', 'details') and hasattr(self, 'detail_launch'):
            self.update_readiness()
        if install.playable and self.sequence_step in ('source', 'config', 'build'):
            self.install_tag.setText('GAME INSTALLED')
            self.install_tag.setProperty('status', 'success')
        elif install.present and self.sequence_step in ('source', 'config', 'build'):
            self.install_tag.setText('INCOMPLETE INSTALL')
            self.install_tag.setProperty('status', 'warning')
        elif self.sequence_step in ('source', 'config', 'build'):
            self.install_tag.setText('NOT INSTALLED')
            self.install_tag.setProperty('status', 'muted')
        self.install_tag.style().unpolish(self.install_tag)
        self.install_tag.style().polish(self.install_tag)
        self.install_message.setText(reason)
        self.update_button.setVisible(install.retained_files and install.present)
        self.play_existing_button.setVisible(install.playable)
        self.source_repair_button.setVisible(sys.platform.startswith('linux') and install.present)
        if not self.busy() and self.phase == 'builder':
            if mode == 'launch':
                self.primary_button.setText('OK · Continue to launch')
                self.primary_button.setEnabled(True)
            else:
                self.primary_button.setText('OK · Build & continue')
                self.primary_button.setEnabled(mode != 'needs_source')
            self.footer_notice.setText('An existing game will remain playable during incremental updates.'
                                       if install.present else 'Select your game package to begin.')
            self.update_step_buttons()

    def return_to_builder(self):
        self.phase = 'builder'
        self.refresh_state()
        self.show_sequence('source')

    def set_launch_metric(self, key, value, detail=''):
        label, sub = self.launch_metrics[key]
        label.setText(str(value))
        sub.setText(str(detail))

    def populate_launch_metrics(self, install, existing=False, warn=''):
        report = self.report
        self.set_launch_metric('game', 'READY' if install.executable else 'MISSING',
                               str(install.executable) if install.executable else 'No executable found')
        self.set_launch_metric('renderer',
            'STALE / UNVERIFIED' if self.validation_issue else 'FILE PRESENT' if install.renderer else 'MISSING',
            self.validation_issue if self.validation_issue else
            'Located beside game executable; runtime not yet verified' if install.renderer else
            'hookshot_vk.so must be installed beside the executable')
        self.set_launch_metric('shaders', 'PRESENT' if install.shaders else 'MISSING',
                               (f'{install.shaders.stat().st_size / (1024 * 1024):.1f} MiB' if install.shaders else
                                'Shader archive was not generated'))
        self.set_launch_metric('preparation',
            ('EXISTING / NOT RUN' if existing else
             'NOT RUN (REPAIR)' if self.operation_action.startswith('Vulkan renderer-only repair') else
             'SKIPPED / WARNING' if self.preparation_message.startswith('the shaders were not prepared now') else
             'DISABLED' if not self.auto_prepare.isChecked() else
             'REQUESTED BY BUILD'),
            ('No build was run' if existing else self.preparation_message or 'Native preparation outcome was not separately reported'))
        self.set_launch_metric('changed', 'Not rebuilt' if existing else stat_display(report.rebuilt_files),
                               (f'{stat_display(report.compiled_files)} modules in build plan' if not existing else
                                'Existing installation reused'))
        seconds = report.total_build_seconds
        if seconds is None and not existing and self.started:
            seconds = int(max(0, time.monotonic() - self.started))
        self.set_launch_metric('time', 'Not rebuilt' if existing else
                               f'{seconds // 60}m {seconds % 60:02d}s' if seconds is not None else 'Not reported',
                               'Last build' if not existing else 'No elapsed build time')
        # Readiness is shown only in the top-right header.
        self.update_readiness(warn)

    def show_launch_summary(self, existing=False, warn=''):
        install = inspect_game(self.out.text().strip())
        self.phase = 'launch'
        self.show_sequence('launch')
        self.launch_tabs.setCurrentIndex(0)
        self.ready_intro.setText('Installation incomplete — repair the missing files before launching.'
                                 if not install.playable or self.validation_issue else
                                 'Existing installation detected — ready to launch.' if existing else
                                 'Build workflow completed — ready to launch.')
        self.populate_launch_metrics(install, existing=existing, warn=warn)
        self.refresh_adventure_profiles(install.root if install.playable else None)
        if install.playable and not self._save_assets_started:
            self._save_assets_started = True
            QTimer.singleShot(0, self.prepare_adventure_save_assets)
        lines = [f'Install directory: {install.root}',
                 f'Executable: {install.executable}',
                 f'Vulkan renderer: {"Present" if install.renderer else "Not detected"}',
                 f'Game shader archive: {"Present" if install.shaders else "Missing (may stutter while compiling)"}',
                 f'Source: {self.kind.currentText()}',
                 f'Build threads: {self.jobs.value()} / {os.cpu_count() or 1}',
                 f'Language: {self.language.currentData()}',
                 f'Translated functions: {stat_display(self.report.functions) if not existing else "Not rebuilt"}',
                 f'Translated instructions: {stat_display(self.report.instructions) if not existing else "Not rebuilt"}',
                 f'Generated code files: {stat_display(self.report.lifted_files) if not existing else "Not rebuilt"}',
                 f'Compilation units: {stat_display(self.report.compiled_files) if not existing else "Not rebuilt"}',
                 f'Pipeline states: {stat_display(self.report.pipeline_count) if not existing else "Not reported by this session"}',
                 f'Vulkan backend linked this session: {"Yes" if self.report.renderer_linked else "Not reported"}',
                 'Video output: Not automatically verified; use the game runtime log if the window is black.']
        if existing:
            lines += ['', 'No files were overwritten or rebuilt.', self.state_reason]
            if self.state_reason.find('not yet verified') >= 0:
                lines.append('An update has not been ruled out: choose Review to rebuild or select new source.')
        else:
            lines += ['', 'Build/install: completed successfully.',
                      f'Build action: {self.operation_action}',
                      f'Native modules rebuilt this run: {self.compiled} / {self.changed or "unknown"}',
                      'Game extraction: ' + self.extraction_message,
                      'Shader/pipeline preparation: ' + (self.preparation_message or 'Not requested')]
        if warn:
            lines += ['', 'Warning: ' + warn]
        self.summary.setPlainText('\n'.join(lines))
        # No launch button in the installer footer or on the three-profile menu.
        self.footer_notice.setText('Review the missing components and repair Vulkan before launching.'
                                   if not install.playable or self.validation_issue else
                                   'Ready. Review the summary, then launch Hookshot.')
        ready = install.playable and not self.validation_issue
        self.overall_caption.setText('Install incomplete' if not ready else 'Ready to launch')
        self.overall_bar.setValue(100 if ready else 95)
        self.statusBar().showMessage('Install incomplete' if not ready else 'Ready to launch')
        self.update_readiness(warn)
        self.update_step_buttons()

    def primary_action(self):
        if self.busy():
            return
        if self.sequence_step in ('launch', 'details'):
            return  # Launch is only available after an adventure was selected.
        self.refresh_state()
        if self.sequence_step == 'source':
            if self.state_mode == 'launch' and not self.reinstall.isChecked() and not self.install_requested:
                self.show_launch_summary(existing=True)
            elif self.state_mode == 'needs_source':
                QMessageBox.warning(self.active_dialog, 'Game source required', self.state_reason)
            else:
                self.show_sequence('config')
        elif self.sequence_step == 'config':
            if self.state_mode == 'needs_source':
                QMessageBox.warning(self.active_dialog, 'Game source required', self.state_reason)
                return
            if self.state_mode == 'launch' and not self.reinstall.isChecked():
                self.show_launch_summary(existing=True)
                return
            self.show_sequence('build')
            self.build()
        elif self.sequence_step == 'build':
            self.build()

    def game_executable(self):
        return inspect_game(self.out.text().strip()).executable

    def busy(self):
        return self.process.state() != QProcess.ProcessState.NotRunning or self.task != 'idle'

    def set_busy(self, busy):
        self.primary_button.setEnabled(not busy)
        self.cancel_button.setEnabled(True)
        self.cancel_button.setText('Cancel operation' if busy else
                                   'Exit installation' if self.sequence_step == 'source' else 'Previous step')
        for name in ('prepare_button', 'force_prepare_button', 'inspect_button', 'merge_button',
                     'repair_button', 'launch_repair_button', 'source_repair_button'):
            if hasattr(self, name):
                getattr(self, name).setEnabled(not busy)
        if hasattr(self, 'tool_cancel_button'):
            self.tool_cancel_button.setEnabled(busy)
        for name in ('kind', 'nup', 'romfs', 'exefs', 'out', 'language', 'jobs',
                     'keep_build_files', 'reinstall', 'update_button'):
            if hasattr(self, name):
                getattr(self, name).setEnabled(not busy)
        if not busy:
            self.update_step_buttons()

    def start_vulkan_header_setup(self, resume='build'):
        answer = QMessageBox.question(
            self.active_dialog, 'Vulkan renderer dependencies missing',
            'The Vulkan and SPIR-V header packages are missing from every known Hookshot cache.\n\n'
            'If the build continues now, Hookshot can finish without hookshot_vk.so, resulting in a black screen.\n\n'
            'Download the three upstream-pinned header packages into this checkout now? '
            '(No system packages or native source code will be changed.)',
            QMessageBox.StandardButton.Yes | QMessageBox.StandardButton.No,
            QMessageBox.StandardButton.Yes)
        if answer != QMessageBox.StandardButton.Yes:
            self.stage.setText('Build blocked: Vulkan headers missing')
            self.log.appendPlainText('Build not started: missing Vulkan dependency cache. No installed game files were changed.')
            return
        self.after_dependency_setup = resume
        self.task = 'deps'
        self.buffer = ''
        self.last_error = ''
        self.cancel_requested = False
        self.stage.setText('Installing pinned Vulkan headers…')
        self.log.appendPlainText('Preparing Vulkan headers before native compilation.')
        self.bar.setRange(0, 100)
        self.bar.setValue(0)
        self.set_busy(True)
        self.process.setWorkingDirectory(str(ROOT))
        self.process.setProcessEnvironment(QProcessEnvironment.systemEnvironment())
        self.process.start(sys.executable, ['-u', str(VULKAN_SETUP), '--install', '--root', str(ROOT)])

    def repair_renderer(self):
        """Use Hookshot's native RX_VK_ONLY path; no game lift and no native edits."""
        if not sys.platform.startswith('linux'):
            return
        if self.busy():
            return
        game = inspect_game(self.out.text().strip())
        if game.executable is None:
            QMessageBox.warning(self.active_dialog, 'Game executable required',
                                'Build Hookshot first. The renderer repair only compiles the Vulkan backend.')
            return
        prebuilt = os.environ.get('HOOKSHOT_VK_PREBUILT', '')
        self.dependency_cache = find_cache(ROOT)
        if self.dependency_cache is None and not (prebuilt and Path(prebuilt).is_file()):
            self.show_sequence('build')
            self.start_vulkan_header_setup(resume='repair')
            return
        self.show_sequence('build')
        self.task = 'repair_vk'
        self.buffer = ''
        self.last_error = ''
        self.cancel_requested = False
        self.report = BuildReport()
        self.validation_issue = ''
        self.started = time.monotonic()
        self.stage.setText('Rebuilding Vulkan renderer only…')
        self.bar.setRange(0, 0)
        self.log.appendPlainText('Renderer-only rebuild (RX_VK_ONLY=1). Game translation is reused / skipped.')
        self.overall_caption.setText('Repairing Vulkan renderer')
        self.overall_bar.setValue(0)
        env = QProcessEnvironment.systemEnvironment()
        env.insert('RX_VK_ONLY', '1')
        if self.dependency_cache is not None:
            env.insert('RX_CPM', str(self.dependency_cache))
            self.log.appendPlainText(f'Using Vulkan headers: {self.dependency_cache}')
        # Use the same compiler work cache as the official native game builder.
        self.renderer_work = Path(os.environ.get('XDG_CACHE_HOME') or Path.home() / '.cache') / 'hookshot-builder'
        env.insert('OOT_WORK', str(self.renderer_work))
        self.process.setProcessEnvironment(env)
        self.process.setWorkingDirectory(str(ROOT))
        self.set_busy(True)
        self.process.start(sys.executable, ['-I', str(ROOT / 'nx2' / 'build.py'),
                                            '--standalone', '--jobs', str(self.jobs.value())])

    def install_repaired_renderer(self):
        """Copy a newly compiled release-matched renderer next to the actual executable."""
        install = inspect_game(self.out.text().strip())
        if not install.executable:
            raise RuntimeError('Installed game executable disappeared during repair')
        source = self.renderer_work / 'sa' / 'build' / 'hookshot_vk.so'
        if not source.is_file() or source.stat().st_size < 1024 * 1024:
            raise RuntimeError(f'Compiled Vulkan renderer was not created: {source}')
        if not self.report.renderer_linked and not os.environ.get('HOOKSHOT_VK_PREBUILT'):
            raise RuntimeError('Build did not report linking a renderer; refusing to install stale cached output')
        dst = install.executable.parent / 'hookshot_vk.so'
        backup = dst.with_suffix('.so.pre-repair')
        if dst.exists():
            shutil.copy2(dst, backup)
        staged = dst.with_suffix('.so.new')
        shutil.copy2(source, staged)
        staged.replace(dst)
        self.log.appendPlainText(f'Installed renderer: {dst} ({dst.stat().st_size / (1024 * 1024):.1f} MiB)')
        if backup.exists():
            self.log.appendPlainText(f'Previous renderer backup: {backup}')
        self.validation_issue = ''
        self.stage.setText('Vulkan renderer repaired; verify game video output')
        self.bar.setRange(0, 100)
        self.bar.setValue(100)

    def build(self):
        if self.sequence_step != 'build':
            self.show_sequence('build')
        if not SCRIPT.is_file():
            QMessageBox.critical(self, 'Missing build system', f'Cannot find {SCRIPT}')
            return
        install = inspect_game(self.out.text().strip())
        src = self.current_source()
        if self.kind.currentData() == 'nup':
            entered = self.nup.text().strip()
            if entered and not Path(entered).is_file():
                QMessageBox.warning(self, 'Invalid source', 'The selected NUP file does not exist.')
                return
            if entered and Path(entered).suffix.lower() != '.nup':
                QMessageBox.warning(self, 'Unexpected source type', 'Select a valid .nup file.')
                return
        else:
            entered = any(w.text().strip() for w in (self.romfs, self.exefs))
            if entered and not src:
                QMessageBox.warning(self, 'Invalid source', 'Select valid romfs and exefs directories containing exefs/main.')
                return
        if self.update_requested and install.retained_files:
            # 2.1.0's supported in-place updater keeps the game files and saves.
            args = ['--update', self.out.text().strip()]
            self.operation_action = 'Update from retained game files'
        elif src and src.get('format') == 'nup':
            args = ['--nup', str(Path(self.nup.text()).expanduser())]
            self.operation_action = 'NUP import/update'
        elif src:
            args = ['--romfs', self.romfs.text().strip(), '--exefs', self.exefs.text().strip()]
            self.operation_action = 'Extracted directories import/update'
        elif install.retained_files:
            args = ['--update', self.out.text().strip()]
            self.operation_action = 'Update from retained game files'
        else:
            QMessageBox.warning(self, 'Game source required', 'Choose a NUP file or romfs/exefs directories.')
            return
        if not self.out.text().strip():
            QMessageBox.warning(self, 'Install directory required', 'Select an installation directory.')
            return
        out = Path(self.out.text().strip()).expanduser()
        self.install_before_build = install
        self.operation_source = src if src else (load_manifest(out) or {}).get('source')
        self.operation_language = self.language.currentData()
        self.operation_code = code_stamp(ROOT)
        for key, widget in (('nup', self.nup), ('romfs', self.romfs),
                            ('exefs', self.exefs), ('out', self.out)):
            self.settings.setValue(key, widget.text())
        self.settings.setValue('language', self.language.currentData())
        self.settings.setValue('jobs', self.jobs.value())
        self.settings.setValue('keep_build_files', self.keep_build_files.isChecked())
        self.settings.setValue('auto_prepare', self.auto_prepare.isChecked())

        # The native build silently skips hookshot_vk.so when Vulkan headers are
        # absent, yet exits successfully. Never spend another entire game build
        # before verifying its source dependencies are actually available.
        if sys.platform.startswith('linux'):
            prebuilt = os.environ.get('HOOKSHOT_VK_PREBUILT', '')
            if prebuilt and not Path(prebuilt).is_file():
                QMessageBox.critical(self.active_dialog, 'Invalid Vulkan renderer override',
                    'HOOKSHOT_VK_PREBUILT points to a missing file:\n' + prebuilt)
                return
            if not prebuilt:
                self.dependency_cache = find_cache(ROOT)
                if self.dependency_cache is None:
                    self.start_vulkan_header_setup()
                    return

        self.buffer = ''
        self.last_error = ''
        self.task = 'build'
        self.cancel_requested = False
        self.started = time.monotonic()
        self.eta = None
        self.compiled = self.changed = 0
        self.current_stage = ''
        self.extraction_message = ('Using supplied extracted directories' if src and src.get('format') == 'extracted'
                                   else 'No extraction result reported')
        self.preparation_message = ''
        self.validation_issue = ''
        self.report = BuildReport()
        self.build_completed = False
        self.stage.setText('Starting game importer…')
        self.bar.setValue(0)
        self.overall_bar.setValue(0)
        self.extraction_bar.setRange(0, 100)
        self.extraction_bar.setValue(0)
        self.extraction_bar.setFormat('Waiting for extraction')
        self.extraction_state.setText('Queued')
        self.build_extraction_status.setText('EXTRACTION · Queued')
        self.build_extraction_bar.setRange(0, 100)
        self.build_extraction_bar.setValue(0)
        self.build_extraction_bar.setFormat('Waiting for extraction')
        self.overall_caption.setText('Starting game build')
        self.log.clear()
        args += ['--out', str(out), '--language', self.language.currentData(),
                 '--jobs', str(self.jobs.value())]
        # v2.1.0 runs --prepare-shaders inside make_game.py. Do not repeat it
        # as a second process after the build. The toggle controls its native flag.
        if not self.auto_prepare.isChecked():
            args.append('--no-prepare-shaders')
        env = QProcessEnvironment.systemEnvironment()
        env.insert('HOOKSHOT_KEEP_BUILD_FILES', '1' if self.keep_build_files.isChecked() else '0')
        if sys.platform.startswith('linux') and self.dependency_cache is not None:
            # RX_CPM is an upstream-supported cache override; no native-code edits.
            env.insert('RX_CPM', str(self.dependency_cache))
            self.log.appendPlainText(f'Using verified Vulkan header cache: {self.dependency_cache}')
        self.process.setProcessEnvironment(env)
        self.process.setWorkingDirectory(str(ROOT))
        self.set_busy(True)
        self.process.start(sys.executable, ['-I', str(SCRIPT), *args])
        self.statusBar().showMessage('Building game')

    def read_output(self):
        self.buffer += bytes(self.process.readAllStandardOutput()).decode('utf-8', errors='replace')
        while '\n' in self.buffer:
            line, self.buffer = self.buffer.split('\n', 1)
            self.handle_line(line.rstrip('\r'))

    def handle_line(self, line):
        if self.task == 'repair_vk':
            self.report.absorb(line)
            if line.strip():
                self.log.appendPlainText(line)
                if ' files, ' in line:
                    self.stage.setText('Compiling Vulkan renderer…')
                elif 'linked ' in line and 'hookshot_vk.so' in line:
                    self.stage.setText('Vulkan renderer linked')
            return
        if self.task == 'deps':
            if line.startswith('@@progress '):
                try:
                    self.bar.setValue(round(100 * float(line.split()[1])))
                except (ValueError, IndexError):
                    pass
            elif line.startswith('@@error '):
                self.last_error = line[8:]
                self.log.appendPlainText('ERROR: ' + self.last_error)
            elif not line.startswith('@@'):
                self.log.appendPlainText(line)
            return
        if self.task == 'build':
            self.report.absorb(line)
        if self.task != 'build':
            if line.startswith('@@stage '):
                self.tool_stage.setText(line.split(' ', 2)[-1])
            elif line.startswith('@@progress '):
                try:
                    self.tool_bar.setRange(0, 100)
                    fraction = max(0., min(1., float(line.split()[1])))
                    self.tool_bar.setValue(round(100 * fraction))
                    if self.task == 'prepare_auto':
                        self.bar.setRange(0, 100)
                        self.bar.setValue(round(fraction * 100))
                        self.overall_bar.setValue(max(self.overall_bar.value(), round(95 + 5 * fraction)))
                except (ValueError, IndexError):
                    pass
            elif line.startswith('@@error '):
                self.last_error = line[8:]
            if not line.startswith('@@') or line.startswith(('@@error ', '@@done ')):
                self.tool_log.appendPlainText(line)
                if self.task == 'prepare_auto':
                    self.log.appendPlainText(line)
            return
        # The official v2.0.14 builder reports a shader preparation failure as
        # a nonfatal plain-text line, with the work deferred to first launch.
        if line.startswith('the shaders were not prepared now'):
            self.preparation_message = line
        if line.startswith('@@extract '):
            try:
                _, completed, total = line.split()
                completed, total = int(completed), int(total)
                if total > 0:
                    percentage = min(100, round(100 * completed / total))
                    self.extraction_bar.setValue(percentage)
                    self.extraction_bar.setFormat(f'{completed:,} / {total:,} files (%p%)')
                    self.extraction_state.setText(f'{percentage}%')
                    self.build_extraction_status.setText(f'EXTRACTION · {completed:,} / {total:,} files')
                    self.build_extraction_bar.setValue(percentage)
                    self.build_extraction_bar.setFormat(f'{completed:,} / {total:,} files (%p%)')
                    self.extraction_message = f'{completed:,} / {total:,} files extracted'
                    self.current_stage = 'read'
                    self.stage.setText('Extracting game files')
                    self.bar.setValue(percentage)
                    self.update_overall(completed / total)
            except (ValueError, IndexError):
                pass
        elif 'reading it again is skipped' in line:
            self.extraction_bar.setValue(100)
            self.extraction_bar.setFormat('Reusing previously extracted game files')
            self.extraction_state.setText('Reused')
            self.build_extraction_status.setText('EXTRACTION · Reused retained files')
            self.build_extraction_bar.setValue(100)
            self.build_extraction_bar.setFormat('Previously extracted game files reused')
            self.extraction_message = 'Reused existing extracted game data'
        if line.startswith('@@stage '):
            parts = line.split(' ', 2)
            self.current_stage = parts[1]
            self.stage.setText(STAGES.get(parts[1], parts[2] if len(parts) > 2 else parts[1]))
            self.bar.setValue(0)
            self.update_overall(0)
        elif line.startswith('@@progress '):
            try:
                fraction = max(0., min(1., float(line.split()[1])))
                self.bar.setValue(round(100 * fraction))
                self.update_overall(fraction)
            except (ValueError, IndexError):
                pass
        elif line.startswith('@@plan '):
            try:
                self.changed = int(line.split()[1])
                self.compiled = 0
            except (ValueError, IndexError):
                pass
        elif line.startswith('@@file '):
            parts = line.split()
            if len(parts) >= 4 and parts[3] in ('new', 'again'):
                self.compiled += 1
                if self.changed:
                    self.stage.setText(f'Compiling {self.compiled:,} / {self.changed:,} updated modules')
        elif line.startswith('@@eta '):
            try:
                self.eta = max(0., float(line.split()[1]))
            except (ValueError, IndexError):
                pass
        elif line.startswith('@@error '):
            self.last_error = line[8:]
        if not line.startswith('@@') or line.startswith('@@error '):
            self.log.appendPlainText(line)

    def update_overall(self, fraction):
        weights = {'read': (0, 15), 'lift': (15, 20),
                   'compile': (35, 50), 'finish': (85, 10),
                   'prepare': (95, 5), 'shaders': (95, 5),
                   'pipeline': (95, 5)}
        if self.current_stage in weights:
            base, width = weights[self.current_stage]
            self.overall_bar.setValue(max(self.overall_bar.value(), round(base + width * fraction)))
            self.overall_caption.setText(self.stage.text() + ' · estimated')

    def tick(self):
        if self.process.state() == QProcess.ProcessState.NotRunning:
            return
        elapsed = int(time.monotonic() - self.started)
        if self.task == 'build':
            remaining = f'  ·  estimated remaining {int(self.eta)}s' if self.eta is not None else ''
            self.details.setText(f'Elapsed {elapsed // 60:02d}:{elapsed % 60:02d}{remaining}')
            self.statusBar().showMessage(self.stage.text())
        else:
            self.statusBar().showMessage(f'{self.tool_stage.text()} · {elapsed // 60:02d}:{elapsed % 60:02d}')

    def process_error(self, error):
        # QProcess may emit an error without a useful process exit status.
        if self.task != 'idle':
            self.last_error = self.process.errorString()
            if self.task == 'build':
                self.log.appendPlainText('Process error: ' + self.last_error)
            else:
                self.tool_log.appendPlainText('Process error: ' + self.last_error)

    def finished(self, exit_code, exit_status):
        if self.buffer:
            self.handle_line(self.buffer)
            self.buffer = ''
        task = self.task
        self.task = 'idle'
        self.set_busy(False)
        ok = (exit_code == 0 and exit_status == QProcess.ExitStatus.NormalExit
              and not self.cancel_requested)
        if task == 'repair_vk':
            if not ok:
                self.stage.setText('Renderer-only compilation failed / cancelled')
                self.log.appendPlainText(self.last_error or f'Renderer repair exited with code {exit_code}')
                return
            try:
                self.install_repaired_renderer()
            except (OSError, RuntimeError) as exc:
                self.stage.setText('Renderer repair could not be installed')
                self.log.appendPlainText('ERROR: ' + str(exc))
                return
            self.preparation_message = 'Run shader preparation after Vulkan repair (optional)'
            self.operation_action = 'Vulkan renderer-only repair; no game retranslation'
            self.show_launch_summary(warn='Renderer repaired; video still needs an in-game test.')
            return
        if task == 'deps':
            if ok and find_cache(ROOT) is not None:
                self.dependency_cache = find_cache(ROOT)
                self.log.appendPlainText(f'Vulkan dependencies ready: {self.dependency_cache}')
                if self.after_dependency_setup == 'repair':
                    self.repair_renderer()
                else:
                    self.build()  # Continue the same user-requested build.
            else:
                self.stage.setText('Vulkan dependency setup failed / cancelled')
                self.log.appendPlainText(self.last_error or
                    'Vulkan headers remain unavailable. No native build was started.')
                self.update_step_buttons()
            return
        if task == 'build':
            if not ok:
                self.stage.setText('Build failed or cancelled')
                self.statusBar().showMessage(f'Build stopped (exit {exit_code})')
                if self.last_error:
                    self.log.appendPlainText('ERROR: ' + self.last_error)
                self.refresh_state()
                return
            self.build_completed = True
            self.code_version = self.operation_code
            # The native --update path intentionally retains the installed
            # language; honor an explicit change made in this GUI afterward.
            if self.operation_action == 'Update from retained game files':
                self.apply_selected_language()
            self.stage.setText('Build completed successfully')
            self.bar.setValue(100)
            self.overall_bar.setValue(95)
            install = inspect_game(self.out.text().strip())
            has_valid_backend = (not sys.platform.startswith('linux') or
                                 self.report.renderer_linked or
                                 bool(os.environ.get('HOOKSHOT_VK_PREBUILT')))
            if not install.playable or not has_valid_backend:
                self.validation_issue = ('The native build did not link a Vulkan renderer for this release.'
                                         if not has_valid_backend else 'Required installed game files are missing.')
                self.log.appendPlainText('ERROR: The native build finished, but the Vulkan backend was not built or installed correctly.')
                if sys.platform.startswith('linux') and install.renderer is None:
                    self.log.appendPlainText('ERROR: hookshot_vk.so is missing beside the installed executable. '
                                             'Check the Vulkan dependency cache and compiler output.')
                self.stage.setText('Build incomplete — Vulkan / game files missing')
                self.overall_caption.setText('Installation incomplete — review the build log')
                self.overall_bar.setValue(95)
                self.refresh_state()
                self.show_launch_summary(warn='Vulkan renderer was not produced by this build.')
                return
            # A manifest must never claim the build succeeded if a renderer is missing.
            try:
                write_manifest(Path(self.out.text().strip()).expanduser(),
                               self.operation_source, self.operation_language, self.operation_code)
            except OSError as ex:
                self.log.appendPlainText(f'Warning: could not save GUI build manifest: {ex}')
            self.update_requested = False
            self.reinstall.setChecked(False)
            self.refresh_state()
            # The native v2.0.14 build already ran shader/pipeline preparation
            # (or reported why it could not). Launch immediately; do not repeat it.
            if not self.preparation_message:
                self.preparation_message = ("Handled by Hookshot's native build process"
                                            if self.auto_prepare.isChecked()
                                            else 'Disabled in the build configuration')
            self.show_launch_summary()
        elif task in ('prepare', 'prepare_auto', 'prepare_extended'):
            self.tool_bar.setRange(0, 100)
            if ok:
                self.tool_bar.setValue(100)
            self.tool_stage.setText(('Extended pipeline warm-up completed' if task == 'prepare_extended' else
                                     'Shader and pipeline preparation completed') if ok else
                                    f'Preparation stopped (exit {exit_code})')
            self.preparation_message = ('Completed' if ok else f'Failed or cancelled (exit {exit_code})')
            if not ok and self.last_error:
                self.tool_log.appendPlainText('ERROR: ' + self.last_error)
            if task == 'prepare_auto':
                self.bar.setRange(0, 100)
                if ok:
                    self.bar.setValue(100)
                self.show_launch_summary(warn='' if ok else 'Shader/pipeline preparation did not finish. The game can still run.')
            # Manual preparation stays in the tools dialog; it must not
            # unexpectedly navigate the wizard to the launch prompt.
        elif task == 'pipeline':
            self.tool_bar.setRange(0, 100)
            if ok:
                self.tool_bar.setValue(100)
            self.tool_stage.setText('Pipeline tool complete' if ok else f'Pipeline tool failed (exit {exit_code})')
        self.statusBar().showMessage(self.tool_stage.text() if task != 'build' else self.stage.text())

    def apply_selected_language(self):
        language_file = Path(self.out.text().strip()).expanduser() / 'language.txt'
        try:
            lines = language_file.read_text(encoding='utf-8').splitlines(keepends=True)
            for i, line in enumerate(lines):
                stripped = line.strip()
                if stripped and not stripped.startswith('#'):
                    if stripped != self.operation_language:
                        lines[i] = self.operation_language + '\n'
                        language_file.write_text(''.join(lines), encoding='utf-8')
                        self.log.appendPlainText('Set installed language: ' + self.operation_language)
                    break
        except OSError as ex:
            self.log.appendPlainText(f'Warning: could not update language.txt: {ex}')

    def refresh_adventure_profiles(self, install_root=None):
        if not self.profile_store:
            self.profile_hint.setText('Profile manager unavailable: ' + self.profile_error)
            self.profile_combo.setEnabled(False)
            self.profile_manage_button.setEnabled(False)
            return
        try:
            if install_root is not None:
                self.profile_store.set_game_dir(Path(install_root))
                register_installed_saves(self.profile_store, Path(install_root))
            chosen = str(self.settings.value('adventure_profile', '')) or self.profile_combo.currentData()
            self.profile_combo.blockSignals(True)
            self.profile_combo.clear()
            for p in self.profile_store.list_profiles():
                self.profile_combo.addItem(p['name'] + ('  [Randomizer planned]' if p['kind'] == 'randomizer-planned' else ''), p['id'])
            idx = self.profile_combo.findData(chosen)
            if idx >= 0:
                self.profile_combo.setCurrentIndex(idx)
            self.profile_combo.blockSignals(False)
            self.profile_hint.setText('Each adventure uses its own save directory. Start the game from this launcher to use it.')
            self.profile_combo.setEnabled(self.profile_combo.count() > 0)
            self.profile_manage_button.setEnabled(True)
            self.update_save_root_preview()
            self.refresh_launcher_boxes()
        except (OSError, ValueError, ProfileError) as err:
            self.profile_hint.setText('Cannot initialize save profiles: ' + str(err))
            self.profile_combo.setEnabled(False)

    def profile_choice_changed(self, index):
        if index >= 0:
            self.settings.setValue('adventure_profile', str(self.profile_combo.currentData()))
            self.update_save_root_preview()
            self.refresh_launcher_boxes()

    def activate_launcher_box(self, index):
        if not self.profile_store:
            QMessageBox.warning(self, 'Profiles unavailable', self.profile_error)
            return
        profile = self.profile_store.launcher_slots()[index]
        if profile is not None:
            self.show_adventure_details(profile['id'])
        else:
            self.open_adventure_profiles()
            if self.adventure_window is not None:
                self.adventure_window.new()
                created = self.adventure_window.selected()
                if created:
                    self.profile_store.pin_launcher_slot(created['id'], index)
                    self.show_adventure_details(created['id'])
                    self.adventure_window.close()

    def refresh_launcher_boxes(self):
        if not self.profile_store or not hasattr(self, 'launcher_boxes'):
            return
        for i, ((card, title, description, select), profile) in enumerate(
                zip(self.launcher_boxes, self.profile_store.launcher_slots())):
            if profile is None:
                self.launcher_type_badges[i].hide()
                self.launcher_icon_summaries[i].hide()
                self.launcher_icon_summaries[i].set_stats({})
                title.setText('New Adventure')
                self.set_save_preview(self.launcher_previews[i], None)
                description.setText('Separate save data · Start fresh')
                description.show()
                select.setText('Create')
                select.setToolTip('Create an isolated profile in launcher slot ' + str(i + 1))
            else:
                self.launcher_type_badges[i].show()
                self.launcher_icon_summaries[i].show()
                description.hide()
                title.setText(profile['name'])
                rows = read_adventure_slots(Path(profile['user_dir']))
                current = max((r for r in rows if not r.warning), key=lambda r: r.modified, default=None)
                stats_data = current.statistics if current is not None else {}
                self.launcher_icon_summaries[i].set_stats(stats_data)
                set_tag_data(self.launcher_type_badges[i], self._save_metadata(stats_data, profile))
                self.set_save_preview(self.launcher_previews[i], current.preview_jpeg if current else None)
                title.setText(self.adventure_title(profile['name'],
                              current.statistics.get('location') if current else None,
                              current.statistics.get('age') if current else None))
                if current is not None:
                    stats = current.statistics
                    values = []
                    for key, label in [('hearts', '♥'), ('rupees', 'Rupees'),
                                       ('gold_skulltulas', 'Skulltulas')]:
                        if stats.get(key) is not None:
                            values.append(f'{label} {stats[key]}')
                    description.setText('   ·   '.join(values))
                else:
                    description.setText('Existing save' if self.profile_store._contains_game_save(profile)
                                        else 'New game · no progress yet')
                select.setText('Select')
                select.setToolTip('Open details for ' + profile['name'])
            card.setToolTip(str(profile['user_dir']) if profile else 'Create new adventure')

    def update_save_root_preview(self):
        """Explain precisely where the chosen profile writes its saves."""
        if self.profile_store is None:
            return
        try:
            pid = self.profile_combo.currentData()
            if not pid:
                self.active_save_root.setText('Save directory: none')
                self.active_slot_info.setText('Create or choose an adventure.')
                return
            profile = self.profile_store.get(pid)
            from pathlib import Path
            try:
                from .save_info import read_game_slots
            except ImportError:
                from save_info import read_game_slots
            root = Path(profile['user_dir'])
            slots = read_game_slots(root)
            self.active_save_root.setText('Game will use --user ' + str(root))
            self.active_save_root.setToolTip(str(root))
            decoded = read_adventure_slots(root)
            latest = next((row for row in decoded if not row.warning), None)
            if latest:
                parts = [f"Save {latest.slot + 1}"]
                for name, key in [('Hearts', 'hearts'), ('Rupees', 'rupees'), ('Skulltulas', 'gold_skulltulas')]:
                    if latest.statistics.get(key) is not None:
                        parts.append(f"{name}: {latest.statistics[key]}")
                self.active_slot_info.setText('  ·  '.join(parts))
                self.active_slot_info.setToolTip('Latest recognized game save')
            else:
                self.active_slot_info.setText(f"{len(slots)} occupied in-game saves · " +
                    ('Original directory' if profile['external'] else 'Isolated adventure'))
        except (OSError, ProfileError) as err:
            self.active_save_root.setText('Save profile error: ' + str(err))
            self.active_slot_info.clear()

    def set_adventure_profile(self, profile_id):
        """Called by the manager whenever a profile is created or selected."""
        if not profile_id:
            return
        self.settings.setValue('adventure_profile', str(profile_id))
        self.refresh_adventure_profiles()
        index = self.profile_combo.findData(profile_id)
        if index >= 0:
            self.profile_combo.setCurrentIndex(index)
        self.update_save_root_preview()
        self.refresh_launcher_boxes()

    def create_adventure_profile(self):
        self.open_adventure_profiles()
        if self.adventure_window is not None:
            self.adventure_window.new()

    def on_adventure_window_closed(self):
        """Closing the utility window never reopens/creates a builder step."""
        if self._exiting:
            return
        self.refresh_adventure_profiles()
        if self.active_dialog is not None:
            self.active_dialog.show()
            self.active_dialog.raise_()
            self.active_dialog.activateWindow()

    def open_adventure_profiles(self):
        if not self.profile_store:
            QMessageBox.warning(self.active_dialog, 'Profiles unavailable', self.profile_error)
            return
        try:
            install = inspect_game(self.out.text().strip())
            if install.present and install.image:
                self.profile_store.set_game_dir(install.root)
                register_installed_saves(self.profile_store, install.root)
            if self.adventure_window is None:
                self.adventure_window = AdventureWindow(self.profile_store, launch_callback=self.launch_selected_adventure,
                    on_select=self.set_adventure_profile, on_close=self.on_adventure_window_closed)
                # App-level stylesheet also colors utility windows and their dialogs.
                self.adventure_window.setWindowIcon(QIcon(str(ROOT / 'assets' / 'hookshot.png')))
            self.adventure_window.refresh(self.settings.value('adventure_profile', '') or self.profile_combo.currentData())
            self.adventure_window.show()
            self.adventure_window.raise_()
            self.adventure_window.activateWindow()
        except (OSError, ValueError, ProfileError) as err:
            QMessageBox.warning(self.active_dialog, 'Profiles unavailable', str(err))

    def launch_selected_adventure(self, profile_id):
        # Integrated manager opens detail; user explicitly launches from there.
        self.show_adventure_details(profile_id)
        if self.adventure_window is not None:
            self.adventure_window.close()
        return True

    def launch_game(self, profile_id=None):
        install = inspect_game(self.out.text().strip())
        if not install.playable or self.validation_issue:
            QMessageBox.warning(self.active_dialog, 'Installation incomplete',
                'The Vulkan renderer, executable, or image is missing. Repair it before launching.')
            return
        exe = self.game_executable()
        if not exe:
            QMessageBox.warning(self, 'Missing game executable', 'No game executable was found in the install directory.')
            self.return_to_builder()
            return
        if hookshot_is_running():
            QMessageBox.warning(self.active_dialog, 'Hookshot is already running',
                'Close the current game before opening another profile. Running multiple instances could corrupt saves.')
            return False
        try:
            if self.profile_store is None:
                raise ProfileError(self.profile_error or 'Profile manager unavailable')
            self.refresh_adventure_profiles(install.root)
            active_profile_id = profile_id or self.profile_combo.currentData()
            if not active_profile_id:
                raise ProfileError('Choose an adventure profile before playing.')
            selected_profile = self.profile_store.get(active_profile_id)
            # Randomizer status is separate from save backup. Game-save backups
            # are exclusively an explicit user action in Settings/Manage.
            if selected_profile['kind'] == 'randomizer-planned':
                answer = QMessageBox.question(self.active_dialog, 'Randomizer not yet active',
                    'This profile stores seed settings, but game rewards remain vanilla. Play anyway?',
                    QMessageBox.StandardButton.Yes | QMessageBox.StandardButton.No,
                    QMessageBox.StandardButton.No)
                if answer != QMessageBox.StandardButton.Yes:
                    return False
            args, game_dir = self.profile_store.game_command(active_profile_id)
            user_root = Path(self.profile_store.get(active_profile_id)['user_dir'])
            if not user_root.is_dir():
                raise ProfileError('Selected save folder is missing; refusing to launch with another profile.')
        except (OSError, ValueError, ProfileError) as err:
            QMessageBox.warning(self.active_dialog, 'Save profile unavailable', str(err))
            return False
        proc = QProcess(self)
        env = QProcessEnvironment.systemEnvironment()
        env.insert('RX_BACKEND', 'vulkan')
        warmup = self.launch_precompile.isChecked()
        terrain = self.launch_texture_headroom.isChecked()
        # Always merge, including all-off: otherwise the previous 10 GB
        # experiments in hookshot_env.txt could remain silently enabled.
        try:
            source_env = env.value('HOS_ENV_FILE', '') or None
            profile = write_runtime_profile(exe, precompile=warmup, terrain=terrain,
                                            other_env_file=source_env)
            env.insert('HOS_ENV_FILE', str(profile))
            for key in runtime_env_overrides(precompile=warmup, terrain=terrain):
                env.remove(key)
        except (OSError, ValueError) as err:
            QMessageBox.warning(self, 'Runtime profile failed',
                f'Could not configure the launch experiment: {err}\nNo game files were changed.')
            return False
        if self.launch_frame_log.isChecked():
            try:
                diag_dir = Path.home() / '.cache' / 'hookshot-builder' / 'diagnostics'
                diag_dir.mkdir(parents=True, exist_ok=True)
                log_path = diag_dir / ('frames-' + time.strftime('%Y%m%d-%H%M%S') + '.csv')
                env.insert('RX_VK_FRAME_LOG', str(log_path))
                self.statusBar().showMessage('Frame diagnostics: ' + str(log_path))
            except OSError as err:
                QMessageBox.warning(self, 'Diagnostics unavailable', str(err))
                return False
        proc.setProcessEnvironment(env)
        proc.setWorkingDirectory(str(exe.parent))
        proc.setProgram(str(exe))
        proc.setArguments(args[1:])
        ok = proc.startDetached()
        if isinstance(ok, tuple):
            ok = ok[0]
        if not ok:
            QMessageBox.warning(self, 'Launch failed', f'Could not launch {exe}')
            return False
        else:
            self.profile_store.mark_played(active_profile_id)
            profile_desc = ('Startup pipeline precompilation' if warmup else 'Normal pipeline behavior')
            profile_desc += ' + 10 GB terrain-texture budget' if terrain else ''
            self.statusBar().showMessage('Game launched · ' + profile_desc)
            self.summary.appendPlainText('\nLaunch profile: ' + profile_desc +
                '\nAdventure: ' + self.profile_store.get(active_profile_id)['name'] +
                '\nCommand save override: --user ' + str(user_root))
            self.update_save_root_preview()
            return True

    def cancel(self):
        if self.busy():
            answer = QMessageBox.question(self, 'Cancel operation?',
                'Stop the active task? Previously installed game files will be retained.',
                QMessageBox.StandardButton.Yes | QMessageBox.StandardButton.No,
                QMessageBox.StandardButton.No)
            if answer == QMessageBox.StandardButton.Yes:
                self.cancel_requested = True
                self.process.terminate()
                QTimer.singleShot(3000, self.force_stop)
            return
        if self.sequence_step in ('launch', 'details'):
            self.show_sequence('launch')
        elif self.sequence_step == 'build':
            self.show_sequence('config')
        elif self.sequence_step == 'config':
            self.show_sequence('source')
        else:
            # Exit setup returns to the main menu, not the desktop.
            self.install_requested = False
            self.reinstall.setChecked(False)
            self.show_launch_summary(existing=True)

    def force_stop(self):
        if self.process.state() != QProcess.ProcessState.NotRunning:
            self.process.kill()

    def closeEvent(self, event):
        """Only the primary window owns application exit. Utilities do not."""
        if self.busy():
            QMessageBox.warning(self, 'Operation running',
                'Cancel the running build or pipeline task before closing Hookshot.')
            event.ignore()
            return
        self._exiting = True
        if self.adventure_window is not None:
            # Closing the manager posts its on_close callback; _exiting ensures
            # it cannot resurrect the builder after the primary window closes.
            self.adventure_window.close()
        event.accept()

    # The shader and pipeline tools are the original native functionality,
    # retained as a secondary tab rather than recreated in Python.
    @staticmethod
    def _scrollable_settings_page(content, name):
        """Keep every setting reachable on compact Steam Deck / scaled desktops."""
        area = QScrollArea()
        area.setObjectName(name)
        area.setFrameShape(QFrame.Shape.NoFrame)
        area.setWidgetResizable(True)
        area.setHorizontalScrollBarPolicy(Qt.ScrollBarPolicy.ScrollBarAlwaysOff)
        area.setWidget(content)
        return area

    def setup_settings_tab(self):
        """Central preferences and explicit manual save-backup controls."""
        page = QWidget()
        self.tabs.addTab(page, 'Settings')
        page.setObjectName('launcherSettingsPage')
        self.settings_page = page
        outer = QVBoxLayout(page)
        outer.setContentsMargins(10, 6, 10, 8)
        outer.setSpacing(5)
        self.settings_sections = QTabWidget()
        self.settings_sections.setDocumentMode(True)
        outer.addWidget(self.settings_sections, 1)
        # Persistent bottom-right navigation on every Settings subpage.
        settings_navigation = QHBoxLayout()
        settings_navigation.addStretch(1)
        self.settings_back_button = QPushButton('Back to Adventures')
        self.settings_back_button.setObjectName('secondaryButton')
        self.settings_back_button.clicked.connect(
            lambda: self.tabs.setCurrentWidget(self.tabs.widget(0)))
        settings_navigation.addWidget(self.settings_back_button)
        outer.addLayout(settings_navigation)
        # The native shader and pipeline controls, including the persistent
        # prediction budget, live together under Tweaks.
        tweaks_page = QWidget()
        self.tweaks_page = tweaks_page
        tweaks_layout = QVBoxLayout(tweaks_page)
        tweaks_layout.setContentsMargins(4, 5, 4, 4)
        tweaks_layout.setSpacing(4)
        self.tweaks_sections = QTabWidget(tweaks_page)
        self.tweaks_sections.setObjectName('launcherTweaksSections')
        self.tweaks_sections.setDocumentMode(True)
        tweaks_layout.addWidget(self.tweaks_sections, 1)

        appearance = QWidget()
        av = QVBoxLayout(appearance)
        av.setContentsMargins(11, 9, 11, 9)
        av.setSpacing(8)
        av.addWidget(QLabel('APPEARANCE · SHARED BY ALL HOOKSHOT TOOLS'))
        chooser = QHBoxLayout()
        chooser.addWidget(QLabel('Visual theme'))
        self.theme_combo = QComboBox()
        self.theme_combo.setObjectName('themeSelector')
        self.theme_combo.setMaximumWidth(300)
        populate_theme_combo(self.theme_combo)
        self.theme_combo.currentIndexChanged.connect(self.change_theme)
        chooser.addWidget(self.theme_combo)
        chooser.addStretch()
        av.addLayout(chooser)
        note = QLabel('Seven themes; changes apply to the builder and Adventure Select together. No game or save data changes.')
        note.setObjectName('muted')
        note.setWordWrap(True)
        av.addWidget(note)
        av.addStretch()
        self.settings_sections.addTab(appearance, 'Appearance')

        launches = QWidget()
        vl = QVBoxLayout(launches)
        vl.setContentsMargins(11, 9, 11, 9)
        vl.setSpacing(7)
        vl.addWidget(QLabel('DEFAULT GAME-LAUNCH BEHAVIOR'))
        vl.addWidget(self.launch_precompile)
        vl.addWidget(self.launch_texture_headroom)
        self.launch_frame_log = QCheckBox('Capture Vulkan frame timestamps (diagnostics)')
        self.launch_frame_log.setChecked(self.settings.value('launch_frame_log', False, type=bool))
        self.launch_frame_log.toggled.connect(lambda enabled: self.settings.setValue('launch_frame_log', enabled))
        self.launch_frame_log.setToolTip('Writes a CSV in ~/.cache/hookshot-builder/diagnostics. Disable after reproducing a freeze.')
        vl.addWidget(self.launch_frame_log)
        label = QLabel('For display freezes: test with terrain protection OFF, then with pipeline precompilation OFF. Change one setting per test.')
        label.setObjectName('muted')
        label.setWordWrap(True)
        vl.addWidget(label)
        vl.addSpacing(9)
        vl.addWidget(QLabel('MANUAL ADVENTURE SAVE BACKUP'))
        backup_note = QLabel('Backups are never created or requested when launching. '
                             'Use this button whenever you want a copy of the selected adventure saves.')
        backup_note.setObjectName('muted')
        backup_note.setWordWrap(True)
        vl.addWidget(backup_note)
        backup_row = QHBoxLayout()
        self.manual_save_backup_button = QPushButton('Back up selected adventure…')
        self.manual_save_backup_button.setObjectName('secondaryButton')
        self.manual_save_backup_button.clicked.connect(self.manual_backup_selected_adventure)
        backup_row.addWidget(self.manual_save_backup_button)
        backup_row.addStretch()
        vl.addLayout(backup_row)
        vl.addStretch()
        self.launch_settings_page = self._scrollable_settings_page(
            launches, 'launcherGameLaunchScroll')

        builder = QWidget()
        bl = QVBoxLayout(builder)
        bl.setContentsMargins(11, 9, 11, 9)
        bl.setSpacing(7)
        bl.addWidget(QLabel('NATIVE BUILD PREFERENCES'))
        bl.addWidget(self.keep_build_files)
        bl.addWidget(self.auto_prepare)
        builder_note = QLabel('Game language, output folder and parallel build jobs are still on the original Build Configuration page. They have not been removed.')
        builder_note.setObjectName('muted')
        builder_note.setWordWrap(True)
        bl.addWidget(builder_note)
        self.open_build_config_button = QPushButton('Open build configuration…')
        self.open_build_config_button.setObjectName('secondaryButton')
        self.open_build_config_button.setToolTip(
            'Opens the existing language, output directory and parallel-jobs controls; no build starts automatically.')
        self.open_build_config_button.clicked.connect(lambda: self.show_sequence('config'))
        bl.addWidget(self.open_build_config_button, alignment=Qt.AlignmentFlag.AlignLeft)
        bl.addStretch()
        self.builder_settings_page = self._scrollable_settings_page(
            builder, 'launcherBuilderScroll')

        reinstall_page = QWidget()
        repl = QVBoxLayout(reinstall_page)
        repl.setContentsMargins(11, 9, 11, 9)
        repl.setSpacing(10)
        repl.addWidget(QLabel('INSTALL / REINSTALL HOOKSHOT'))
        self.reinstall_status = QLabel('Your installed game is not modified until a build is started.')
        self.reinstall_status.setObjectName('muted')
        self.reinstall_status.setWordWrap(True)
        repl.addWidget(self.reinstall_status)
        repl.addWidget(QLabel('Use this page to repair the game, change source files, or restart installation.\n'
                                'Saved adventures and profile metadata are maintained separately.'))
        repl.addWidget(self.reinstall)
        buttons = QHBoxLayout()
        update = QPushButton('Review / update installation')
        update.setObjectName('secondaryButton')
        update.clicked.connect(lambda: self.begin_install(force=False))
        fresh = QPushButton('Begin reinstall')
        fresh.setObjectName('primaryButton')
        fresh.clicked.connect(lambda: self.begin_install(force=True))
        buttons.addWidget(update)
        buttons.addWidget(fresh)
        buttons.addStretch()
        repl.addLayout(buttons)
        repl.addStretch()
        # Prediction budget now lives beside Extended Warm-up inside Tweaks.
        # The old standalone Pipeline Tools tab had no other controls.
        self.settings_sections.addTab(reinstall_page, 'Reinstall')
        self.tweaks_sections.addTab(self.builder_settings_page, 'Builder')
        self.tweaks_sections.addTab(self.launch_settings_page, 'Game Launch')
        old_index = self.tabs.indexOf(self.tool_page)
        if old_index >= 0:
            self.tabs.removeTab(old_index)
        self.tweaks_sections.addTab(self.tool_page, 'Shader / Pipeline Tools')
        # Keep Game Launch as the default Tweaks subpage for header shortcuts.
        self.tweaks_sections.setCurrentWidget(self.launch_settings_page)
        self.settings_sections.addTab(self.tweaks_page, 'Tweaks')
        self.warnings_panel = DiagnosticsPanelFactory.create((), self.settings_page,
                                    on_retry=self.retry_diagnostics)
        self.settings_sections.addTab(self.warnings_panel, 'Warnings & Errors')
        self.rebuild_save_assets_button = QPushButton('Rebuild Save Icons')
        self.rebuild_save_assets_button.setObjectName('secondaryButton')
        self.rebuild_save_assets_button.setToolTip('Retry failed icon decoding from your local game files and verified JSON')
        self.rebuild_save_assets_button.clicked.connect(lambda: self.prepare_adventure_save_assets(force=True))
        self.warnings_panel.layout().addWidget(self.rebuild_save_assets_button)
        self.install_save_decoder_button = QPushButton('Install / Repair Icon Decoder')
        self.install_save_decoder_button.setObjectName('secondaryButton')
        self.install_save_decoder_button.setToolTip(
            'Install missing ASTC decoder wheels into builder/qt/.local-python only; no virtualenv or global packages')
        self.install_save_decoder_button.clicked.connect(self.install_save_icon_decoder)
        self.warnings_panel.layout().addWidget(self.install_save_decoder_button)
        self.warnings_panel.refresh_diagnostics(self.current_diagnostics())

    def install_save_icon_decoder(self):
        """Explicitly provision ONLY the private ASTC decoder on user request."""
        from PyQt6.QtWidgets import QMessageBox
        from pathlib import Path
        helper = Path(__file__).with_name('setup_save_icon_decoder.py')
        if not helper.is_file():
            QMessageBox.warning(self, 'Icon decoder unavailable',
                                'The private decoder setup script was not installed.')
            return
        if self._decoder_setup_process is not None:
            QMessageBox.information(self, 'Decoder installation running',
                                    'The previous installation has not finished.')
            return
        result = QMessageBox.question(
            self, 'Install local texture decoder',
            'Hookshot uses texture2ddecoder to read ASTC game icons.\n\n'
            'Install missing decoder wheels in builder/qt/.local-python? '
            'This downloads Python packages but creates no virtualenv and does not modify system Python.',
            QMessageBox.StandardButton.Yes | QMessageBox.StandardButton.No,
            QMessageBox.StandardButton.No)
        if result != QMessageBox.StandardButton.Yes:
            return
        process = QProcess(self)
        process.setWorkingDirectory(str(ROOT))
        process.setProcessChannelMode(QProcess.ProcessChannelMode.MergedChannels)
        self._decoder_setup_process = process
        self.install_save_decoder_button.setEnabled(False)
        log_chunks = []

        def capture():
            chunk = bytes(process.readAllStandardOutput()).decode('utf-8', errors='replace')
            log_chunks.append(chunk)
            self.statusBar().showMessage('Installing private icon decoder…')

        def finished(code, _status):
            capture()
            self._decoder_setup_process = None
            self.install_save_decoder_button.setEnabled(True)
            if code != 0:
                QMessageBox.warning(self, 'Decoder setup failed',
                    'The private icon decoder could not be installed.\n\n' +
                    ''.join(log_chunks)[-3000:])
                return
            import importlib
            importlib.invalidate_caches()
            activate_private_directory()
            self.statusBar().showMessage('Private decoder installed; rebuilding game icons…')
            self.prepare_adventure_save_assets(force=True)
            self.warnings_panel.refresh_diagnostics(self.current_diagnostics())

        process.readyReadStandardOutput.connect(capture)
        process.finished.connect(finished)
        process.start(sys.executable, [str(helper)])

    def manual_backup_selected_adventure(self):
        """Copy the selected profile's saves only when the user clicks Back up."""
        if self.profile_store is None:
            QMessageBox.warning(self, 'Backups unavailable',
                                self.profile_error or 'The adventure profile manager is unavailable.')
            return
        profile_id = self.profile_combo.currentData()
        if not profile_id:
            QMessageBox.information(self, 'Choose an adventure',
                                    'Select an adventure before creating a manual backup.')
            return
        try:
            profile = self.profile_store.get(profile_id)
            backup_path = self.profile_store.backup(profile_id)
        except (OSError, ValueError, ProfileError) as err:
            QMessageBox.warning(self, 'Save backup failed', str(err))
            return
        self.statusBar().showMessage('Manual save backup: ' + str(backup_path))
        QMessageBox.information(self, 'Save backup complete',
            f"Copied the saves for {profile['name']} to:\n{backup_path}")

    def open_warnings_settings(self):
        self.tabs.setCurrentWidget(self.settings_page)
        self.settings_sections.setCurrentIndex(self._settings_tab_index('Warnings & Errors'))
        self.warnings_panel.refresh_diagnostics(self.current_diagnostics())

    def _settings_tab_index(self, label):
        for i in range(self.settings_sections.count()):
            if self.settings_sections.tabText(i) == label:
                return i
        return 0

    def _header_page_changed(self, _index):
        # Keep the left-side header control labelled Settings; the Back button
        # is the primary way out of Settings, even on the Warnings page.
        self.settings_header_button.setText('Settings')
        active = self.tabs.currentWidget() is self.settings_page
        self.settings_header_button.setProperty('active', active)
        self.settings_header_button.style().unpolish(self.settings_header_button)
        self.settings_header_button.style().polish(self.settings_header_button)

    def toggle_header_settings(self):
        self.tabs.setCurrentWidget(self.settings_page)
        self.warnings_panel.refresh_diagnostics(self.current_diagnostics())

    def current_diagnostics(self):
        results = []
        install = inspect_game(self.out.text().strip())
        if not install.playable:
            results.append(diagnostic_from_code('game_not_installed', details=self.state_reason))
        if self.validation_issue:
            results.append(diagnostic_from_code('game_validation_error',
                details=self.validation_issue, severity='error'))
        if self.state_mode == 'build' and self.phase == 'launch':
            results.append(diagnostic_from_code('game_update_recommended',
                details=self.state_reason))
        if self.last_error:
            results.append(diagnostic_from_code('last_operation_error',
                details=self.last_error))
        if install.playable and install.shaders is None:
            results.append(diagnostic_from_code('shader_pipeline_warning'))
        if self.preparation_message and ('warning' in self.preparation_message.lower()
                or 'failed' in self.preparation_message.lower()
                or 'not prepared' in self.preparation_message.lower()):
            results.append(diagnostic_from_code('shader_pipeline_warning',
                details=self.preparation_message))
        manifest = Path(__file__).parent / 'assets' / 'game_icons' / 'icon_manifest.json'
        if not manifest.is_file():
            results.append(diagnostic_from_code('manifest_missing'))
        if self._save_asset_issue:
            results.append(diagnostic_from_code('icon_cache_failure', details=self._save_asset_issue))
        return results

    def retry_diagnostics(self):
        """Retry icon decoding only on request; otherwise simply refresh diagnostics."""
        if self._save_asset_issue:
            self.prepare_adventure_save_assets(force=True)
        self.update_readiness()
        self.warnings_panel.refresh_diagnostics(self.current_diagnostics())

    def _game_romfs_root(self):
        install = inspect_game(self.out.text().strip())
        candidates = [os.environ.get('HOOKSHOT_ROMFS', ''),
                      str(install.root / 'romfs'),
                      str(install.executable.parent / 'romfs') if install.executable else '',
                      str(Path.home() / 'Games' / 'Hookshot' / 'romfs'),
                      self.romfs.text().strip()]
        for name in candidates:
            if not name:
                continue
            path = Path(name).expanduser()
            if (path / 'UI' / 'Tex' / 'Icon').is_dir() and (path / 'Pack' / 'ZsDic.pack.zs').is_file():
                return path
            if (path / 'UI' / 'Layout2Archive').is_dir() and (path / 'Pack' / 'ZsDic.pack.zs').is_file():
                return path
        return None

    def prepare_adventure_save_assets(self, force=False):
        """Generate local game icons with a per-icon fallback and visible progress."""
        manifest = Path(__file__).parent / 'assets' / 'game_icons' / 'icon_manifest.json'
        if not manifest.is_file():
            self._save_asset_issue = 'No verified manifest is installed.'
            self.update_readiness()
            return False
        romfs = self._game_romfs_root()
        if romfs is None:
            self._save_asset_issue = 'Could not locate extracted game RomFS with icon textures and ZsDic dictionary.'
            self.update_readiness()
            return False
        cache = SaveAssetCache(manifest, romfs, ROOT)
        self._save_asset_cache = cache
        # A single faulty mapping must not hide every successfully decoded icon.
        state = cache.current()
        if force or not state.ready:
            if not ensure_save_assets(self, cache, notify_failure=False, force=force):
                self._save_asset_issue = (cache._result.error if cache._result else state.error) or 'Icon generation failed.'
                self.statusBar().showMessage('Save assets unavailable; generated artwork remains active.')
                self.update_readiness()
                return False
        try:
            try:
                from .zelda_icon_assets import activate_game_cache
            except ImportError:
                from zelda_icon_assets import activate_game_cache
            state = cache.current()
            activate_game_cache(state.directory)
            self._save_asset_issue = state.error  # Only the individually failed mappings.
            for widget in self.launcher_icon_summaries:
                widget.update()
            self.detail_visual.update()
            self.statusBar().showMessage(
                f'Game icons ready: {len(state.roles)} decoded, {len(state.failed)} using fallback')
            self.update_readiness()
            return True
        except Exception as error:
            self._save_asset_issue = str(error)
            self.update_readiness()
            return False

    def begin_install(self, force=False):
        if self.busy():
            QMessageBox.information(self, 'Build in progress', 'Finish or cancel the running operation first.')
            return
        if force:
            answer = QMessageBox.question(self, 'Restart installation?',
                'This will open the three-step installer. Your isolated save profiles will not be deleted. '
                'Game installation files may be replaced when you start the build. Continue?',
                QMessageBox.StandardButton.Yes | QMessageBox.StandardButton.No,
                QMessageBox.StandardButton.No)
            if answer != QMessageBox.StandardButton.Yes:
                return
        self.reinstall.setChecked(bool(force))
        self.update_requested = False
        self.install_requested = True
        self.refresh_state()
        self.show_sequence('source')

    def open_reinstall_settings(self):
        self.tabs.setCurrentWidget(self.settings_page)
        self.settings_sections.setCurrentIndex(self._settings_tab_index('Reinstall'))

    def open_launch_settings(self):
        self.tabs.setCurrentWidget(self.settings_page)
        self.settings_sections.setCurrentWidget(self.tweaks_page)
        self.tweaks_sections.setCurrentWidget(self.launch_settings_page)

    def setup_shader_tools(self):
        """Compact tabbed tools, always inside the primary builder window."""
        page = QWidget()
        self.tabs.addTab(page, 'Shader / Pipeline Tools')
        self.tool_page = page
        layout = QVBoxLayout(page)
        layout.setContentsMargins(10, 6, 10, 8)
        layout.setSpacing(6)
        self.tool_sections = QTabWidget()
        self.tool_sections.setObjectName('launcherPipelineSections')
        self.tool_sections.setDocumentMode(True)
        # Do not cap this to 270 logical px inside the new nested Tweaks tabs:
        # high DPI and compact windows previously clipped the action buttons.
        self.tool_sections.setMinimumHeight(170)
        layout.addWidget(self.tool_sections, 3)

        warm = QScrollArea()
        warm.setObjectName('pipelineWarmupScroll')
        warm.setWidgetResizable(True)
        warm.setFrameShape(QFrame.Shape.NoFrame)
        warm.setHorizontalScrollBarPolicy(Qt.ScrollBarPolicy.ScrollBarAlwaysOff)
        warm_content = QWidget()
        warm_layout = QVBoxLayout(warm_content)
        warm_layout.setContentsMargins(8, 7, 8, 7)
        warm_layout.setSpacing(6)
        shader_card, shader_layout = self.make_step('S', 'Shader / pipeline precompiler', 'NATIVE',
            'Compile shaders and prepare Vulkan pipelines without rebuilding the game.')
        buttons = QHBoxLayout()
        self.prepare_button = QPushButton('Prepare shaders + pipelines')
        self.prepare_button.setObjectName('primaryButton')
        self.prepare_button.clicked.connect(lambda: self.prepare_shaders(False))
        self.force_prepare_button = QPushButton('Force re-prepare')
        self.force_prepare_button.setObjectName('secondaryButton')
        self.force_prepare_button.clicked.connect(lambda: self.prepare_shaders(True))
        buttons.addWidget(self.prepare_button)
        buttons.addWidget(self.force_prepare_button)
        shader_layout.addLayout(buttons)
        self.extended_prepare_button = QPushButton('Extended warm-up: known + predicted pipelines')
        self.extended_prepare_button.setObjectName('primaryButton')
        self.extended_prepare_button.setToolTip('Native warm-up using the prediction budget below; saves are untouched.')
        self.extended_prepare_button.clicked.connect(lambda: self.prepare_shaders(extended=True))
        shader_layout.addWidget(self.extended_prepare_button)
        self.extended_prepare_note = QLabel(
            'Optional one-time pass: predicts more pipelines but cannot cover every unseen state. '
            'Previously used compute programs only; no all-compute mode.')
        self.extended_prepare_note.setObjectName('muted')
        self.extended_prepare_note.setWordWrap(True)
        shader_layout.addWidget(self.extended_prepare_note)
        # Keep prediction duration alongside the extended pipeline warm-up
        # that consumes it, rather than in a separate Settings tab.
        prediction_form = QFormLayout()
        self.predict_seconds = QSpinBox()
        self.predict_seconds.setRange(30, 1800)
        self.predict_seconds.setSuffix(' seconds')
        self.predict_seconds.setValue(int(self.settings.value('predict_seconds', 600)))
        self.predict_seconds.valueChanged.connect(
            lambda n: self.settings.setValue('predict_seconds', n))
        self.predict_seconds.setToolTip(
            'Duration used for the optional extended pipeline prediction pass.')
        prediction_form.addRow('Prediction budget', self.predict_seconds)
        shader_layout.addLayout(prediction_form)
        prediction_note = QLabel(
            'Longer prediction can improve coverage but cannot guarantee every unseen pipeline. '
            'Compute preload remains limited to previously used programs.')
        prediction_note.setObjectName('muted')
        prediction_note.setWordWrap(True)
        shader_layout.addWidget(prediction_note)
        warm_layout.addWidget(shader_card)
        warm_layout.addStretch()
        warm.setWidget(warm_content)
        self.tool_sections.addTab(warm, 'Warm-up')

        states_page = QScrollArea()
        states_page.setObjectName('pipelineStatesScroll')
        states_page.setWidgetResizable(True)
        states_page.setFrameShape(QFrame.Shape.NoFrame)
        states_page.setHorizontalScrollBarPolicy(Qt.ScrollBarPolicy.ScrollBarAlwaysOff)
        states_content = QWidget()
        states_layout = QVBoxLayout(states_content)
        states_layout.setContentsMargins(8, 7, 8, 7)
        states_layout.setSpacing(6)
        states, state_layout = self.make_step('P', 'Recorded pipeline states', 'NATIVE',
            'Inspect or merge recorded pipeline-state files. Driver caches are separate.')
        pipeline_actions = QHBoxLayout()
        self.inspect_button = QPushButton('Inspect pipeline list…')
        self.inspect_button.setObjectName('secondaryButton')
        self.inspect_button.clicked.connect(self.inspect_pipeline_list)
        self.merge_button = QPushButton('Merge pipeline lists…')
        self.merge_button.setObjectName('secondaryButton')
        self.merge_button.clicked.connect(self.merge_pipeline_lists)
        pipeline_actions.addWidget(self.inspect_button)
        pipeline_actions.addWidget(self.merge_button)
        state_layout.addLayout(pipeline_actions)
        states_layout.addWidget(states)
        states_layout.addStretch()
        states_page.setWidget(states_content)
        self.tool_sections.addTab(states_page, 'Recorded states')

        # The operation monitor stays visible whichever tools tab is selected.
        self.tool_stage = QLabel('Ready')
        self.tool_stage.setObjectName('stageTitle')
        layout.addWidget(self.tool_stage)
        self.tool_bar = QProgressBar()
        self.tool_bar.setRange(0, 100)
        self.tool_bar.setValue(0)
        layout.addWidget(self.tool_bar)
        self.tool_log = QPlainTextEdit()
        self.tool_log.setReadOnly(True)
        self.tool_log.setPlaceholderText('Shader preparation / pipeline tool output…')
        self.tool_log.setMinimumHeight(100)
        layout.addWidget(self.tool_log, 2)
        self.tool_cancel_button = QPushButton('Cancel current operation')
        self.tool_cancel_button.setObjectName('secondaryButton')
        self.tool_cancel_button.setEnabled(False)
        self.tool_cancel_button.clicked.connect(self.cancel)
        layout.addWidget(self.tool_cancel_button)

    def prepare_shaders(self, force=False, from_build=False, extended=False):
        if self.busy():
            QMessageBox.warning(self, 'Operation running', 'Finish or cancel the current task first.')
            return
        install = inspect_game(self.out.text().strip())
        if not install.executable or not install.shaders:
            QMessageBox.warning(self, 'Game files missing',
                                'Install Hookshot and generate hookshot_shaders.bin before preparation.')
            if from_build:
                self.show_launch_summary(warn='Shader archive missing; preparation could not start.')
            return
        if extended:
            counts = state_counts(install.executable.parent)
            learned = 'unknown' if counts['learned'] is None else str(counts['learned'])
            shipped = 'unknown' if counts['shipped'] is None else str(counts['shipped'])
            answer = QMessageBox.question(self, 'Extended pipeline warm-up?',
                'Prepare known and predicted Vulkan pipelines before gameplay?\n\n'
                f'Recorded state files: {learned} learned, {shipped} shipped.\n'
                f'The prediction pass can run for up to {self.predict_seconds.value()} seconds, plus shader preparation and recorded-pipeline work. '
                'This cannot guarantee 100% coverage. Your game saves and graphics settings are not changed.\n\n'
                'Proceed?',
                QMessageBox.StandardButton.Yes | QMessageBox.StandardButton.No,
                QMessageBox.StandardButton.No)
            if answer != QMessageBox.StandardButton.Yes:
                return
        elif force:
            answer = QMessageBox.question(self, 'Re-prepare shaders?',
                'Force Hookshot to prepare shaders again? Existing compatible driver caches are not deleted.',
                QMessageBox.StandardButton.Yes | QMessageBox.StandardButton.No,
                QMessageBox.StandardButton.No)
            if answer != QMessageBox.StandardButton.Yes:
                return
        env = QProcessEnvironment.systemEnvironment()
        env.insert('RX_BACKEND', 'vulkan')
        env.insert('RX_VK_PRECOMPILE', '1')
        if extended:
            try:
                # Hookshot's hookshot_env.txt is read after process launch, and
                # overwrites raw environment variables. Supply a merged HOS_ENV_FILE.
                warmup_env = write_warmup_env(install.executable,
                    other_env_file=env.value('HOS_ENV_FILE', '') or None,
                    predict_seconds=self.predict_seconds.value())
            except (OSError, ValueError) as err:
                QMessageBox.warning(self, 'Warm-up setup failed', str(err))
                return
            env.insert('HOS_ENV_FILE', str(warmup_env))
            # These are presence-based switches; remove inherited overrides too.
            for key in BLOCKED_FLAGS:
                env.remove(key)
            for key, value in PREWARM_FLAGS.items():
                env.insert(key, value)
            env.insert('RX_VK_PREDICT_S', str(self.predict_seconds.value()))
        elif force:
            env.insert('RX_VK_PREPARE_AGAIN', '1')
        else:
            env.remove('RX_VK_PREPARE_AGAIN')
        self.process.setProcessEnvironment(env)
        self.process.setWorkingDirectory(str(install.executable.parent))
        self.task = 'prepare_auto' if from_build else ('prepare_extended' if extended else 'prepare')
        self.cancel_requested = False
        self.last_error = ''
        self.buffer = ''
        self.tool_log.clear()
        self.tool_bar.setRange(0, 0)  # The native precompiler may not report numeric progress.
        self.tool_stage.setText('Preparing known and predicted pipelines…' if extended else 'Preparing shaders and Vulkan pipelines…')
        if from_build:
            self.overall_bar.setValue(95)
            self.overall_caption.setText('Shader and pipeline preparation')
            self.stage.setText('Preparing Vulkan shaders and pipelines…')
            self.bar.setRange(0, 0)
            self.log.appendPlainText('Preparing shaders/pipelines through the original --prepare-shaders mode…')
            self.tabs.setCurrentIndex(0)
        else:
            self.show_tools()
        self.set_busy(True)
        self.started = time.monotonic()
        self.process.start(str(install.executable), ['--prepare-shaders'])
        self.statusBar().showMessage('Extended pipeline warm-up running' if extended else 'Shader preparation running')

    def pipeline_task(self, paths, output=None):
        if self.busy():
            QMessageBox.warning(self, 'Operation running', 'Finish or cancel the current task first.')
            return
        script = ROOT / 'nx2' / 'pipeline_states.py'
        if not script.is_file():
            QMessageBox.critical(self, 'Pipeline tool missing', f'Cannot find {script}')
            return
        self.task = 'pipeline'
        self.cancel_requested = False
        self.last_error = ''
        self.buffer = ''
        self.tool_log.clear()
        self.tool_bar.setRange(0, 0)
        self.tool_stage.setText('Inspecting pipeline states…' if output is None else 'Merging pipeline states…')
        self.show_tools()
        self.set_busy(True)
        self.started = time.monotonic()
        self.process.setWorkingDirectory(str(ROOT))
        self.process.setProcessEnvironment(QProcessEnvironment.systemEnvironment())
        args = ['-I', str(script)] + (['--out', str(output)] if output is not None else []) + [str(p) for p in paths]
        self.process.start(sys.executable, args)

    def inspect_pipeline_list(self):
        initial = str(Path(self.out.text().strip()).expanduser() / 'hookshot_pipeline_states.bin')
        name, _ = QFileDialog.getOpenFileName(self, 'Inspect pipeline states', initial,
                                              'Pipeline states (*.bin);;All files (*)')
        if name:
            self.pipeline_task([name])

    def merge_pipeline_lists(self):
        initial = str(Path(self.out.text().strip()).expanduser())
        paths, _ = QFileDialog.getOpenFileNames(self, 'Select pipeline state lists', initial,
                                                 'Pipeline states (*.bin);;All files (*)')
        if not paths:
            return
        output, _ = QFileDialog.getSaveFileName(self, 'Save merged pipeline states',
                                                 str(ROOT / 'work' / 'pipeline_states.bin'),
                                                 'Pipeline states (*.bin);;All files (*)')
        if output:
            if Path(output).resolve() in {Path(p).resolve() for p in paths}:
                QMessageBox.warning(self, 'Invalid output', 'Output must differ from all input files.')
                return
            Path(output).parent.mkdir(parents=True, exist_ok=True)
            self.pipeline_task(paths, output)


if __name__ == '__main__':
    if sys.platform == 'win32':
        try:
            import ctypes
            ctypes.windll.shell32.SetCurrentProcessExplicitAppUserModelID('Hookshot.QtBuilder')
        except (AttributeError, OSError):
            pass
    reinstall_flag = startup_reinstall(sys.argv[1:])
    qt_args = [arg for arg in sys.argv if arg.lower() != '--reinstall']
    app = QApplication(qt_args)
    # The launcher ships its own artwork. KDE installations may lack stale
    # configured icon fallbacks (GNOME, Mint-X, elementary); prefer an actually
    # installed Breeze theme where available. This is cosmetic, not a GPU fix.
    if sys.platform.startswith('linux'):
        for theme in ('breeze', 'breeze-dark', 'hicolor'):
            if (Path('/usr/share/icons') / theme / 'index.theme').is_file():
                QIcon.setThemeName(theme)
                QIcon.setFallbackThemeName('hicolor')
                break
    # One primary window; closing it exits. Auxiliary profile windows cannot
    # independently terminate the application while the primary window lives.
    app.setQuitOnLastWindowClosed(True)
    app.setWindowIcon(QIcon(str(ROOT / 'assets' / 'hookshot.png')))
    app.setStyle('Fusion')
    window = Window(reinstall=reinstall_flag)
    window.start()
    sys.exit(app.exec())
