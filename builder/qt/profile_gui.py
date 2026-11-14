"""Hookshot Adventure Profiles — compact tabbed PyQt6 save selector.

Supported gameplay values are parsed read-only from the Switch 2 save format,
and displayed automatically. Saved game bytes are NEVER modified.
"""
from __future__ import annotations

import argparse
import os
from datetime import datetime
from pathlib import Path
import subprocess
import sys

from PyQt6.QtCore import Qt, QTimer
from PyQt6.QtGui import QPixmap
from PyQt6.QtWidgets import (
    QApplication, QCheckBox, QComboBox, QDialog, QDialogButtonBox, QFileDialog,
    QFormLayout, QFrame, QGridLayout, QHBoxLayout, QInputDialog,
    QLabel, QLineEdit, QMainWindow, QMessageBox, QPushButton,
    QSpinBox, QTabWidget, QVBoxLayout, QWidget,
)

try:
    from .profile_store import ProfileStore, ProfileError, default_hookshot_user, register_installed_saves, hookshot_is_running
    from .randomizer_config import Settings, generate_seed
    from .save_info import human_size, scan_user_dir, read_game_slots
    from .runtime_profile import write_runtime_profile, runtime_env_overrides, detect_nvidia_vram_mb
    from .ui_layout import fit_window
    from .save_reader import read_adventure_slots
    from .ui_theme import apply_theme
    from .zelda_summary_widget import ZeldaStatsWidget
    from .adventure_panel_model import safe_character_name
except ImportError:
    from profile_store import ProfileStore, ProfileError, default_hookshot_user, register_installed_saves, hookshot_is_running
    from randomizer_config import Settings, generate_seed
    from save_info import human_size, scan_user_dir, read_game_slots
    from runtime_profile import write_runtime_profile, runtime_env_overrides, detect_nvidia_vram_mb
    from ui_layout import fit_window
    from save_reader import read_adventure_slots
    from ui_theme import apply_theme
    from zelda_summary_widget import ZeldaStatsWidget
    from adventure_panel_model import safe_character_name


# Backwards-compatible default style export; app-wide theme switching is in ui_theme.
STYLESHEET = (Path(__file__).with_name('hookshot_theme.qss')).read_text(encoding='utf-8')



def lbl(text: str, object_name: str | None = None, wrap: bool = False) -> QLabel:
    widget = QLabel(text)
    widget.setTextFormat(Qt.TextFormat.PlainText)
    if object_name:
        widget.setObjectName(object_name)
    widget.setWordWrap(wrap)
    return widget


def fmt_time(iso: str) -> str:
    if not iso:
        return "Never played from this launcher"
    try:
        return datetime.fromisoformat(iso).astimezone().strftime("%b %d, %Y  %I:%M %p")
    except ValueError:
        return "Unknown"


def stat_text(info: dict, key: str, suffix: str = "") -> str:
    value = info.get(key)
    return f"{value}{suffix}" if value is not None else "—"


class NewProfileDialog(QDialog):
    def __init__(self, parent=None):
        super().__init__(parent)
        self.setWindowTitle("New adventure")
        self.setMinimumSize(510, 310)
        outer = QVBoxLayout(self)
        outer.setContentsMargins(12, 10, 12, 10)
        outer.setSpacing(7)
        outer.addWidget(lbl("BEGIN A NEW ADVENTURE", "kicker"))
        form = QFormLayout()
        self.name = QLineEdit()
        self.name.setPlaceholderText("Link")
        self.name.setText("Link")
        form.addRow("Save profile", self.name)
        self.random = QCheckBox("Randomizer preparation (not yet playable)")
        self.random.toggled.connect(self.change_mode)
        form.addRow("Mode", self.random)
        self.seed = QLineEdit()
        self.seed.setPlaceholderText("16 hexadecimal characters or custom text")
        generate = QPushButton("Generate seed")
        generate.clicked.connect(lambda: self.seed.setText(generate_seed()))
        seedrow = QHBoxLayout()
        seedrow.addWidget(self.seed, stretch=1)
        seedrow.addWidget(generate)
        form.addRow("Seed", seedrow)
        self.seed.setEnabled(False)
        generate.setEnabled(False)
        self.generate_button = generate
        outer.addLayout(form)
        self.notice = lbl("A new isolated save directory will be created. Other profiles are untouched.", "subtle", True)
        outer.addWidget(self.notice)
        buttons = QDialogButtonBox(QDialogButtonBox.StandardButton.Ok | QDialogButtonBox.StandardButton.Cancel)
        buttons.button(QDialogButtonBox.StandardButton.Ok).setText("Create profile")
        buttons.accepted.connect(self.accept)
        buttons.rejected.connect(self.reject)
        buttons.setMinimumHeight(44)
        outer.addWidget(buttons)
        outer.addStretch(1)

    def change_mode(self, checked):
        self.seed.setEnabled(checked)
        self.generate_button.setEnabled(checked)
        if checked and not self.seed.text():
            self.seed.setText(generate_seed())
        self.name.setPlaceholderText('Randomized' if checked else 'Link')
        if self.name.text().strip() in ('Link', 'Randomized'):
            self.name.setText('Randomized' if checked else 'Link')
        self.notice.setText("Randomizer configuration will be saved, but game rewards will remain VANILLA until engine hooks are added." if checked else "A new isolated save directory will be created. Other profiles are untouched.")


class RandomizerDialog(QDialog):
    OPTIONS = [
        ("shuffle_chests", "Chest rewards"),
        ("shuffle_npc_rewards", "NPC rewards"),
        ("shuffle_heart_pieces", "Heart pieces"),
        ("shuffle_songs", "Songs"),
        ("shuffle_shops", "Shop items"),
        ("shuffle_skulltulas", "Gold Skulltula rewards"),
    ]

    def __init__(self, manifest: dict, parent=None):
        super().__init__(parent)
        self.setWindowTitle("Randomizer settings — preparation only")
        self.setMinimumWidth(460)
        outer = QVBoxLayout(self)
        outer.setContentsMargins(12, 10, 12, 10)
        outer.setSpacing(7)
        outer.addWidget(lbl("RANDOMIZER CONFIGURATION", "kicker"))
        outer.addWidget(lbl("Settings are stored per profile and will be available to the future game-side adapter.", "subtle", True))
        self.seed = QLineEdit(manifest["seed"])
        btn = QPushButton("New seed")
        btn.clicked.connect(lambda: self.seed.setText(generate_seed()))
        seedrow = QHBoxLayout()
        seedrow.addWidget(self.seed, 1)
        seedrow.addWidget(btn)
        outer.addLayout(seedrow)
        self.checks = {}
        options_grid = QGridLayout()
        options_grid.setHorizontalSpacing(18)
        options_grid.setVerticalSpacing(6)
        for index, (key, display_name) in enumerate(self.OPTIONS):
            cb = QCheckBox(display_name)
            cb.setChecked(manifest["settings"].get(key, False))
            options_grid.addWidget(cb, index // 2, index % 2)
            self.checks[key] = cb
        outer.addLayout(options_grid)
        outer.addWidget(lbl("Entrance shuffling: deferred. No gameplay is randomized by this build.", "warning", True))
        controls = QDialogButtonBox(QDialogButtonBox.StandardButton.Save | QDialogButtonBox.StandardButton.Cancel)
        controls.accepted.connect(self.accept)
        controls.rejected.connect(self.reject)
        outer.addWidget(controls)

    def values(self):
        return self.seed.text(), Settings(**{k: v.isChecked() for k, v in self.checks.items()})


class StatTile(QFrame):
    def __init__(self, caption: str, value: str, parent=None):
        super().__init__(parent)
        self.setObjectName("statPanel")
        v = QVBoxLayout(self)
        v.setContentsMargins(7, 5, 7, 5)
        v.setSpacing(2)
        v.addWidget(lbl(caption.upper(), "kicker"))
        v.addWidget(lbl(value, "largeStat"))


class Window(QMainWindow):
    """Compact tabbed adventure manager; safe to embed as a secondary window."""

    def __init__(self, store: ProfileStore, launch_callback=None, on_close=None, on_select=None):
        super().__init__()
        self.store = store
        self.launch_callback = launch_callback
        self.on_close = on_close
        self.on_select = on_select
        self.proc = None
        self.selected_id = None
        self.cards = []
        self.setWindowTitle("Hookshot — Adventure Select")
        fit_window(self, preferred=(1000, 640), minimum=(750, 475))
        root = QWidget()
        root.setObjectName("root")
        self.setCentralWidget(root)
        layout = QVBoxLayout(root)
        layout.setContentsMargins(11, 8, 11, 8)
        layout.setSpacing(6)

        header = QHBoxLayout()
        titles = QVBoxLayout()
        titles.setSpacing(1)
        titles.addWidget(lbl("HOOKSHOT  /  SAVE PROFILES", "kicker"))
        titles.addWidget(lbl("Adventure Select", "title"))
        header.addLayout(titles, 1)
        self.dir_button = QPushButton("Game folder…")
        self.dir_button.clicked.connect(self.pick_game_dir)
        header.addWidget(self.dir_button)
        layout.addLayout(header)

        content = QHBoxLayout()
        content.setSpacing(7)
        list_panel = QFrame()
        list_panel.setObjectName("panel")
        list_panel.setMinimumWidth(170)
        list_panel.setMaximumWidth(210)
        left = QVBoxLayout(list_panel)
        left.setContentsMargins(7, 7, 7, 7)
        left.setSpacing(5)
        left.addWidget(lbl("ADVENTURES", "kicker"))
        # Fixed-size, paginated cards: no scrollbars in the save selector.
        self.profile_page = 0
        self.profiles_per_page = 5
        self.page_stack = QWidget()
        self.card_layout = QVBoxLayout(self.page_stack)
        self.card_layout.setContentsMargins(1, 1, 1, 1)
        self.card_layout.setSpacing(4)
        left.addWidget(self.page_stack, 1)
        pagination = QHBoxLayout()
        self.prev_page = QPushButton('◀')
        self.prev_page.setToolTip('Previous save profiles')
        self.prev_page.clicked.connect(lambda: self.turn_page(-1))
        self.page_number = lbl('1 / 1', 'subtle')
        self.next_page = QPushButton('▶')
        self.next_page.setToolTip('Next save profiles')
        self.next_page.clicked.connect(lambda: self.turn_page(1))
        pagination.addWidget(self.prev_page)
        pagination.addWidget(self.page_number, 1)
        pagination.addWidget(self.next_page)
        left.addLayout(pagination)
        add_profile = QPushButton("＋ New adventure")
        add_profile.clicked.connect(self.new)
        left.addWidget(add_profile)
        content.addWidget(list_panel)

        self.details = QFrame()
        self.details.setObjectName("panel")
        self.details_layout = QVBoxLayout(self.details)
        self.details_layout.setContentsMargins(9, 7, 9, 7)
        self.details_layout.setSpacing(5)
        heading = QHBoxLayout()
        heading_text = QVBoxLayout()
        heading_text.setSpacing(1)
        self.profile_title = lbl("Select an adventure", "profileTitle", True)
        self.profile_hero = lbl("", "subtle", True)
        heading_text.addWidget(self.profile_title)
        heading_text.addWidget(self.profile_hero)
        heading.addLayout(heading_text, 1)
        self.profile_mode = lbl("", "kicker", True)
        heading.addWidget(self.profile_mode)
        self.details_layout.addLayout(heading)
        self.profile_slots = []
        self.slot_picker = QComboBox()
        self.slot_picker.setMinimumWidth(165)
        self.slot_picker.setMaximumWidth(370)
        self.slot_picker.setToolTip('Select a saved adventure to view its progress.')
        self.slot_picker.currentIndexChanged.connect(self.render_details)
        heading.addWidget(self.slot_picker)

        self.detail_tabs = QTabWidget()
        self.detail_tabs.setDocumentMode(True)
        self.tab_layouts = {}
        for title in ("Overview", "Save files", "Randomizer", "Manage"):
            tab_body = QWidget()
            tab_body.setObjectName("tabBody")
            tab_layout = QVBoxLayout(tab_body)
            tab_layout.setContentsMargins(7, 5, 7, 5)
            tab_layout.setSpacing(4)
            self.detail_tabs.addTab(tab_body, title)
            self.tab_layouts[title] = tab_layout
        self.details_layout.addWidget(self.detail_tabs, 1)
        self.action_buttons = {}
        # Persistent play control: switching tabs never hides the launch action.
        self.play_button = QPushButton("View adventure details" if launch_callback is not None else "▶ Play this adventure")
        self.play_button.setObjectName("primary")
        self.play_button.clicked.connect(self.play)
        self.details_layout.addWidget(self.play_button)
        content.addWidget(self.details, 1)
        layout.addLayout(content, 1)

        self.runtime_precompile = QCheckBox("Precompile shader pipelines")
        self.runtime_precompile.setChecked(True)
        self.runtime_terrain = QCheckBox("Experimental 10 GB terrain protection")
        vram = detect_nvidia_vram_mb() if launch_callback is None else None
        self.runtime_terrain.setChecked(vram is not None and vram >= 20 * 1024)
        if launch_callback is None:
            launch_opts = QHBoxLayout()
            launch_opts.addWidget(self.runtime_precompile)
            launch_opts.addWidget(self.runtime_terrain)
            launch_opts.addStretch()
            layout.addLayout(launch_opts)
        else:
            self.runtime_precompile.setVisible(False)
            self.runtime_terrain.setVisible(False)

        self.game_label = lbl("", "subtle", True)
        self.game_label.setToolTip("The selected installation contains the game executable. Save profiles live outside it.")
        layout.addWidget(self.game_label)
        self.status = lbl("Original saves preserved • Each adventure uses a separate --user directory.", "subtle", True)
        layout.addWidget(self.status)
        self.watch = QTimer(self)
        self.watch.setInterval(900)
        self.watch.timeout.connect(self.check_process)
        self.refresh()

    def showEvent(self, event):
        # The builder is the only theme selector; the application stylesheet
        # applies to utility windows automatically.
        super().showEvent(event)

    def error(self, error):
        QMessageBox.critical(self, "Hookshot profile manager", str(error))

    def selected(self):
        try:
            return self.store.get(self.selected_id) if self.selected_id else None
        except ProfileError:
            return None

    def refresh(self, preferred_id=None):
        profiles = self.store.list_profiles()
        target = preferred_id or self.selected_id
        if not any(p["id"] == target for p in profiles):
            target = profiles[0]["id"] if profiles else None
        if self.selected_id != target:
            self.profile_page = 0
        self.selected_id = target
        self.cards.clear()
        if target and not getattr(self, '_page_only', False):
            for index, profile in enumerate(profiles):
                if profile['id'] == target:
                    self.profile_page = index // self.profiles_per_page
                    break
        self._page_only = False
        max_page = max(0, (len(profiles) - 1) // self.profiles_per_page)
        self.profile_page = max(0, min(self.profile_page, max_page))
        self.page_number.setText(f'{self.profile_page + 1} / {max_page + 1}')
        self.prev_page.setEnabled(self.profile_page > 0)
        self.next_page.setEnabled(self.profile_page < max_page)
        while self.card_layout.count():
            item = self.card_layout.takeAt(0)
            if item.widget():
                item.widget().deleteLater()
        start = self.profile_page * self.profiles_per_page
        for index, p in enumerate(profiles[start:start+self.profiles_per_page], start):
            kind = 'RANDOMIZED' if p.get('save_label') == 'rando' else 'VANILLA' 
            card = QPushButton(f"{index+1:02d}  {p['name']}\n{kind}")
            card.setObjectName("profileCard")
            card.setFixedHeight(49)
            card.setProperty("active", p["id"] == target)
            card.clicked.connect(lambda checked=False, pid=p["id"]: self.select_profile(pid))
            self.card_layout.addWidget(card)
            self.cards.append(card)
        self.card_layout.addStretch(1)
        self.game_label.setText('Game: ' + (self.store.game_dir().name if self.store.game_dir() else 'Choose a game folder'))
        self.game_label.setToolTip(str(self.store.game_dir() or ''))
        selected = self.selected()
        self.profile_slots = read_adventure_slots(Path(selected['user_dir'])) if selected else []
        was = self.slot_picker.currentData()
        previous = self.slot_picker.blockSignals(True)
        self.slot_picker.clear()
        for index, slot in enumerate(self.profile_slots):
            suffix = '' if not slot.warning else ' · unsupported'
            self.slot_picker.addItem(f"Save {slot.slot+1} ({slot.account[:8]}){suffix}", index)
        if self.profile_slots:
            index = self.slot_picker.findData(was)
            self.slot_picker.setCurrentIndex(index if index >= 0 else 0)
        self.slot_picker.setVisible(bool(self.profile_slots))
        self.slot_picker.blockSignals(previous)
        self.render_details()
        self.refresh_buttons()

    def turn_page(self, delta):
        self.profile_page = max(0, self.profile_page + delta)
        self._page_only = True
        self.refresh(self.selected_id)

    def select_profile(self, pid):
        self.selected_id = pid
        self.refresh(pid)
        self.notify_selection()

    def notify_selection(self):
        if self.selected_id and self.on_select is not None:
            self.on_select(self.selected_id)

    @staticmethod
    def _clear_layout(layout):
        while layout.count():
            item = layout.takeAt(0)
            if item.widget():
                item.widget().deleteLater()
            elif item.layout():
                Window._clear_layout(item.layout())

    def clear_details(self):
        # Do not recreate QTabWidget: the user's selected tab survives refreshes.
        for layout in self.tab_layouts.values():
            self._clear_layout(layout)
        self.action_buttons.clear()

    def _action(self, label, callback, target_layout):
        button = QPushButton(label)
        button.clicked.connect(callback)
        target_layout.addWidget(button)
        self.action_buttons[label] = button
        return button

    def render_details(self):
        self.clear_details()
        overview = self.tab_layouts["Overview"]
        saves = self.tab_layouts["Save files"]
        random = self.tab_layouts["Randomizer"]
        manage = self.tab_layouts["Manage"]
        p = self.selected()
        slot_index = self.slot_picker.currentData()
        actual = (self.profile_slots[slot_index] if isinstance(slot_index, int)
                  and 0 <= slot_index < len(self.profile_slots) else None)
        if p is None:
            self.profile_title.setText("No adventure selected")
            self.profile_hero.setText("Create a new profile to begin.")
            self.profile_mode.setText("")
            overview.addWidget(lbl("Your original save directory will be preserved.", "subtle", True))
            overview.addStretch()
            saves.addWidget(lbl("No selected save profile.", "subtle"))
            random.addWidget(lbl("Randomizer settings are stored per profile.", "subtle"))
        else:
            try:
                disk = scan_user_dir(Path(p["user_dir"]))
            except Exception as exc:
                self.error(exc)
                disk = None
            self.profile_title.setText(p["name"])
            from_save = actual.statistics if actual and not actual.warning else {}
            info = dict(from_save)  # Save bytes are the sole source of displayed statistics.
            character_name = safe_character_name(info)
            self.profile_hero.setText('Hero: ' + (character_name if character_name != 'Unknown' else 'Link'))
            self.profile_mode.setText('RANDOMIZED' if p.get('save_label') == 'rando' else 'VANILLA')

            # The overview is icon-driven. Advanced inventory data stays on Save files.
            # The editor-sized 1120x456 canvas was being compressed into the
            # smaller Manage Saves tab, clipping and distorting game icons.
            # The full-size details view remains on the main Adventure page.
            visual = ZeldaStatsWidget(compact=True)
            visual.setFixedHeight(64)
            visual.set_stats(info if from_save else {'wallet': 'Kokiri', 'wallet_capacity': 200})
            overview.addWidget(visual)
            preview_line = QHBoxLayout()
            preview = QLabel()
            preview.setAlignment(Qt.AlignmentFlag.AlignCenter)
            preview.setFixedSize(132, 74)
            if actual and actual.preview_jpeg:
                pix = QPixmap()
                if pix.loadFromData(actual.preview_jpeg, 'JPEG'):
                    preview.setPixmap(pix.scaled(132, 74, Qt.AspectRatioMode.KeepAspectRatio,
                                                Qt.TransformationMode.SmoothTransformation))
            else:
                preview.setText('No preview')
            preview_line.addWidget(preview)
            summary = ('Automatic: ' + (actual.saved_in_game or 'game timestamp unknown') if from_save else
                       ('Save format not recognized' if actual and actual.warning else 'No recognized save yet; details will populate automatically'))
            note = lbl(summary, 'subtle', True)
            if actual and actual.warning:
                note.setToolTip(actual.warning)
            preview_line.addWidget(note, 1)
            preview_line.addStretch()
            overview.addLayout(preview_line)
            overview.addStretch()

            saves.addWidget(lbl("SAVE DATA", "kicker"))
            if disk:
                saves.addWidget(lbl(f"{disk.file_count} files · {human_size(disk.bytes_used)} · {disk.save_files} save-associated files", "subtle", True))
                saves.addWidget(lbl("Last change: " + (disk.last_changed or "None yet"), "subtle"))
            slots = read_game_slots(Path(p["user_dir"]))
            if slots:
                saves.addWidget(lbl(f'{len(slots)} game slot(s) detected; choose one from the selector above.', 'subtle'))
                if actual:
                    saves.addWidget(lbl('Save ' + str(actual.slot+1) + ' · ' +
                        ('Recognized Switch 2 format' if not actual.warning else 'Unknown format'), 'subtle'))
                    if actual.saved_in_game:
                        saves.addWidget(lbl('In-game timestamp: ' + actual.saved_in_game, 'subtle'))
            else:
                saves.addWidget(lbl("No in-game save slots detected yet.", "subtle"))
            if from_save:
                saves.addWidget(lbl('INVENTORY / EQUIPMENT  ·  LIVE SAVE DATA', 'kicker'))
                supplies = [('Arrows', 'arrows'), ('Bombs', 'bombs'), ('Bombchus', 'bombchus'),
                            ('Seeds', 'deku_seeds'), ('Nuts', 'deku_nuts'),
                            ('Sticks', 'deku_sticks'), ('Beans', 'magic_beans')]
                text = '   ·   '.join(f'{label}: {from_save[key]}' for label, key in supplies
                                     if from_save.get(key) is not None)
                if text:
                    saves.addWidget(lbl(text, 'subtle', True))
                upgrades = [('Quiver', 'quiver_capacity'), ('Bomb bag', 'bomb_bag_capacity'),
                            ('Seed bag', 'deku_seed_capacity'), ('Nut bag', 'deku_nut_capacity'),
                            ('Stick bag', 'deku_stick_capacity')]
                detail = '   ·   '.join(f'{label}: {from_save[key]}' for label, key in upgrades
                                      if from_save.get(key) is not None)
                if detail:
                    saves.addWidget(lbl(detail, 'subtle', True))
                equip = '   ·   '.join(f'{k}: {from_save[v]}' for k, v in
                                      [('Sword', 'equipped_sword'), ('Shield', 'equipped_shield'), ('Bottles', 'bottles')]
                                      if from_save.get(v) is not None)
                if equip:
                    saves.addWidget(lbl(equip, 'subtle', True))
                keys = from_save.get('dungeon_keys') or {}
                keys = {k: v for k,v in keys.items() if v is not None and v > 0}
                if keys:
                    saves.addWidget(lbl('Dungeon keys: ' + '   ·   '.join(f'{k.title()} {v}' for k,v in keys.items()), 'subtle', True))
            saves.addWidget(lbl("Last launch: " + fmt_time(p.get("last_played", "")), "subtle", True))
            path_display = lbl('User directory: ' + Path(p['user_dir']).name, 'subtle')
            path_display.setToolTip(p['user_dir'])
            saves.addWidget(path_display)
            saves.addStretch()

            random.addWidget(lbl("RANDOMIZER", "kicker"))
            if p["kind"] == "randomizer-planned":
                try:
                    manifest = self.store.get_randomizer(p["id"])
                    random.addWidget(lbl("Seed: " + manifest["seed"], "subtle", True))
                    random.addWidget(lbl("Settings hash: " + manifest["settings_hash"], "subtle"))
                    selected = [label for key, label in RandomizerDialog.OPTIONS if manifest["settings"].get(key, False)]
                    random.addWidget(lbl("Selected pools: " + (", ".join(selected) if selected else "None"), "subtle", True))
                except Exception as exc:
                    random.addWidget(lbl("Settings unavailable: " + str(exc), "warning", True))
                self._action("Edit randomizer settings…", self.edit_randomizer, random)
            else:
                random.addWidget(lbl("Vanilla adventure • no randomizer settings", "subtle"))
            random.addWidget(lbl("ITEM SHUFFLING IS NOT ACTIVE. Seed settings do not affect gameplay yet. Entrance randomization is deferred.", "warning", True))
            random.addStretch()

        manage.addWidget(lbl('SHOW ON LAUNCH SCREEN', 'kicker'))
        pin_row = QHBoxLayout()
        active_slots = self.store.launcher_slots()
        for index in range(3):
            present = active_slots[index]
            label = f'Pin to {index + 1}' + (' ✓' if p and present and present['id'] == p['id'] else '')
            btn = QPushButton(label)
            btn.setEnabled(bool(p))
            btn.setToolTip('Show this adventure in launcher box ' + str(index + 1))
            btn.clicked.connect(lambda checked=False, i=index: self.pin_to_launcher(i))
            pin_row.addWidget(btn)
        manage.addLayout(pin_row)
        manage.addWidget(lbl('Three launcher shortcuts only; all other profiles remain accessible here.', 'subtle', True))
        manage.addWidget(lbl("PROFILE ACTIONS", "kicker"))
        actions = [
            ("Rename", self.rename), ("Duplicate", self.duplicate),
            ("Import", self.import_dir), ("Back up", self.backup),
            ("Restore", self.restore), ("Archive", self.archive),
            ("Mark Randomized", self.mark_randomized), ("Mark Vanilla", self.mark_vanilla),
            ("Delete Save…", self.delete_save),
        ]
        action_grid = QGridLayout()
        action_grid.setSpacing(5)
        for i, (text, callback) in enumerate(actions):
            btn = QPushButton(text)
            btn.clicked.connect(callback)
            action_grid.addWidget(btn, i // 3, i % 3)
            self.action_buttons[text] = btn
        manage.addLayout(action_grid)
        manage.addWidget(lbl("Backups and restores create separate copies. The original game saves are not overwritten.", "subtle", True))
        manage.addStretch()

    def refresh_buttons(self):
        p = self.selected()
        busy = (self.proc is not None and self.proc.poll() is None) or hookshot_is_running()
        for text, button in self.action_buttons.items():
            button.setEnabled((text in {"Import", "Restore"} or p is not None) and not busy)
        if "Edit randomizer settings…" in self.action_buttons:
            self.action_buttons["Edit randomizer settings…"].setEnabled(bool(p) and p["kind"] == "randomizer-planned" and not busy)
        self.action_buttons["Archive"].setEnabled(bool(p) and not p["external"] and not busy)
        self.action_buttons["Delete Save…"].setEnabled(bool(p) and not p["external"] and not busy)
        if hasattr(self, "play_button"):
            self.play_button.setEnabled(bool(p) and not busy and self.store.game_dir() is not None)
        self.dir_button.setEnabled(not busy)

    def pick_game_dir(self):
        directory = QFileDialog.getExistingDirectory(self, "Built Hookshot game folder", str(self.store.game_dir() or Path.home() / "Games" / "Hookshot"))
        if directory:
            try:
                self.store.set_game_dir(Path(directory))
                register_installed_saves(self.store, Path(directory))
                self.refresh()
            except Exception as exc:
                self.error(exc)

    def new(self):
        d = NewProfileDialog(self)
        if d.exec() == QDialog.DialogCode.Accepted:
            try:
                profile = self.store.create(d.name.text(), "randomizer-planned" if d.random.isChecked() else "vanilla", d.seed.text())
                self.refresh(profile["id"])
                self.notify_selection()
                self.status.setText('Created and selected ' + profile['name'] + ' — launch from the builder to use its save directory.')
            except Exception as exc:
                self.error(exc)

    def edit_randomizer(self):
        p = self.selected()
        if p and p["kind"] == "randomizer-planned":
            try:
                d = RandomizerDialog(self.store.get_randomizer(p["id"]), self)
                if d.exec() == QDialog.DialogCode.Accepted:
                    seed, settings = d.values()
                    self.store.set_randomizer(p["id"], seed, settings)
                    self.refresh(p["id"])
            except Exception as exc:
                self.error(exc)

    def pin_to_launcher(self, index):
        selected = self.selected()
        if not selected:
            return
        try:
            self.store.pin_launcher_slot(selected['id'], index)
            self.refresh(selected['id'])
            self.notify_selection()
            self.status.setText(f"{selected['name']} will appear in launcher slot {index + 1}.")
        except Exception as ex:
            self.error(ex)

    def rename(self):
        p = self.selected()
        if p:
            name, ok = QInputDialog.getText(self, "Rename adventure", "Profile name", text=p["name"])
            if ok:
                try:
                    self.store.rename(p["id"], name)
                    self.refresh(p["id"])
                except Exception as exc:
                    self.error(exc)

    def duplicate(self):
        p = self.selected()
        if p:
            name, ok = QInputDialog.getText(self, "Duplicate adventure", "New profile name", text=p["name"] + " (copy)")
            if ok:
                try:
                    clone = self.store.duplicate(p["id"], name)
                    self.refresh(clone["id"])
                except Exception as exc:
                    self.error(exc)

    def import_dir(self):
        folder = QFileDialog.getExistingDirectory(self, "Import existing Hookshot USER data directory")
        if not folder:
            return
        name, ok = QInputDialog.getText(self, "Import adventure", "Profile name", text="Imported Adventure")
        if ok:
            try:
                profile = self.store.import_user_dir(Path(folder), name)
                self.refresh(profile["id"])
                self.notify_selection()
            except Exception as exc:
                self.error(exc)

    def backup(self):
        p = self.selected()
        if p:
            try:
                target = self.store.backup(p["id"])
                QMessageBox.information(self, "Backup complete", "Copied user data to:\n" + str(target))
            except Exception as exc:
                self.error(exc)

    def restore(self):
        folder = QFileDialog.getExistingDirectory(self, "Choose an archived backup", str(self.store.backup_path))
        if not folder:
            return
        name, ok = QInputDialog.getText(self, "Restore adventure", "Restored profile name", text="Restored Adventure")
        if ok:
            try:
                p = self.store.restore_backup_as_new(Path(folder), name)
                self.refresh(p["id"])
                self.notify_selection()
                QMessageBox.information(self, "Restored", "Backup restored as a NEW isolated profile. Other saved games were not modified.")
            except Exception as exc:
                self.error(exc)

    def mark_randomized(self):
        self._label_save('rando')

    def mark_vanilla(self):
        self._label_save('vanilla')

    def _label_save(self, label):
        profile = self.selected()
        if profile is None:
            return
        try:
            self.store.set_save_label(profile['id'], label)
            self.refresh(profile['id'])
            self.notify_selection()
        except Exception as exc:
            self.error(exc)

    def delete_save(self):
        profile = self.selected()
        if not profile or profile.get('external'):
            return
        reply = QMessageBox.warning(
            self, 'Delete save profile',
            "Remove '" + profile['name'] + "' from Hookshot?\n\n"
            'This removes its launcher shortcut. Its files will be moved to a '
            'recoverable trash directory, not permanently erased.\n\n'
            'This cannot be done while the game is running.',
            QMessageBox.StandardButton.Yes | QMessageBox.StandardButton.Cancel,
            QMessageBox.StandardButton.Cancel)
        if reply != QMessageBox.StandardButton.Yes:
            return
        try:
            trash = self.store.delete_save(profile['id'])
            self.selected_id = None
            self.refresh()
            self.notify_selection()
            QMessageBox.information(self, 'Save removed',
                'Removed from active adventures. Recovery copy: \n' + str(trash))
        except Exception as exc:
            self.error(exc)

    def archive(self):
        p = self.selected()
        if p is None or p["external"]:
            return
        if QMessageBox.question(self, "Archive adventure", f"Archive '{p['name']}'?\nAll files are moved into the archive, not deleted.") == QMessageBox.StandardButton.Yes:
            try:
                destination = self.store.archive(p["id"])
                self.refresh()
                self.notify_selection()
                QMessageBox.information(self, "Archived", "Moved profile to:\n" + str(destination))
            except Exception as exc:
                self.error(exc)

    def play(self):
        p = self.selected()
        if p is None or (self.proc is not None and self.proc.poll() is None):
            return
        self.notify_selection()
        if self.launch_callback is not None:
            # Integrated mode never bypasses the builder's expanded detail page.
            self.launch_callback(p['id'])
            return
        if hookshot_is_running():
            QMessageBox.warning(self, "Hookshot is running", "Close the running game before launching another adventure. This protects saves from concurrent writes.")
            return
        if p["kind"] == "randomizer-planned":
            result = QMessageBox.question(self, "Randomizer is not active", "This profile stores a randomizer seed and settings, but does NOT randomize item rewards.\n\nLaunch a VANILLA game in this isolated profile anyway?", QMessageBox.StandardButton.Yes | QMessageBox.StandardButton.No, QMessageBox.StandardButton.No)
            if result != QMessageBox.StandardButton.Yes:
                return
        try:
            command, game_dir = self.store.game_command(p["id"])
            env = os.environ.copy()
            env['RX_BACKEND'] = 'vulkan'
            warmup = self.runtime_precompile.isChecked()
            terrain = self.runtime_terrain.isChecked()
            if warmup or terrain:
                envfile = write_runtime_profile(command[0], precompile=warmup, terrain=terrain,
                                                other_env_file=env.get('HOS_ENV_FILE'))
                env['HOS_ENV_FILE'] = str(envfile)
                for key in runtime_env_overrides(precompile=warmup, terrain=terrain):
                    env.pop(key, None)
            self.proc = subprocess.Popen(command, cwd=str(game_dir), env=env)
            self.store.mark_played(p["id"])
            self.status.setText("Running " + p["name"] + " • editing disabled until this launched game exits")
            self.watch.start()
            self.refresh(p["id"])
        except Exception as exc:
            self.proc = None
            self.error(exc)

    def check_process(self):
        if self.proc is not None and self.proc.poll() is not None:
            code = self.proc.returncode
            self.proc = None
            self.watch.stop()
            self.status.setText(f"Game closed (exit code {code}). You can switch profiles now.")
            self.refresh()

    def closeEvent(self, event):
        if self.proc is not None and self.proc.poll() is None:
            QMessageBox.information(self, "Game running", "Hookshot will keep running after the manager closes. Do not edit the active profile until it exits.")
        super().closeEvent(event)
        if event.isAccepted() and self.on_close is not None:
            # Signals are processed after the close; never close the owner.
            QTimer.singleShot(0, self.on_close)



def main() -> int:
    parser = argparse.ArgumentParser(description="Hookshot profile selector")
    parser.add_argument("--game-dir", type=Path, default=None)
    parser.add_argument("--data-root", type=Path, default=None, help="Use an alternate registry root (testing)")
    args = parser.parse_args()
    app = QApplication([sys.argv[0]])
    apply_theme(persist=False)
    try:
        store = ProfileStore(args.data_root)
        game_dir = args.game_dir or store.game_dir()
        if game_dir is None:
            auto = Path.home() / "Games" / "Hookshot"
            if (auto / "bin" / ("hookshot.exe" if sys.platform == "win32" else "hookshot")).is_file():
                game_dir = auto
        if game_dir:
            store.set_game_dir(game_dir)
        if sys.platform != "win32" or store.game_dir() is not None:
            if store.game_dir() is not None:
                register_installed_saves(store, store.game_dir())
    except Exception as exc:
        QMessageBox.critical(None, "Hookshot profiles", str(exc))
        return 1
    window = Window(store)
    window.show()
    return app.exec()


if __name__ == "__main__":
    raise SystemExit(main())
