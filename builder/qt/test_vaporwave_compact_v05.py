"""Headless regressions for the compact vaporwave GUI; PyQt6 not required."""
from __future__ import annotations

import ast
from pathlib import Path
import unittest

from ui_layout import bounded_size

HERE = Path(__file__).resolve().parent


def cls_fn(tree, cls, name):
    for node in tree.body:
        if isinstance(node, ast.ClassDef) and node.name == cls:
            for member in node.body:
                if isinstance(member, ast.FunctionDef) and member.name == name:
                    return member
    raise AssertionError(f'Missing {cls}.{name} method')


class WindowLayoutTests(unittest.TestCase):
    def test_steam_deck_native_1280x800(self):
        w, h = bounded_size(1280, 800)
        self.assertLessEqual(w, 1280-36)
        self.assertLessEqual(h, 800-54)
        self.assertEqual((w,h), (1000,640))

    def test_scaled_steam_deck_1024x640_logical(self):
        w, h = bounded_size(1024, 640)
        self.assertEqual((w,h), (988,586))

    def test_small_window_never_exceeds_display(self):
        w, h = bounded_size(800, 520)
        self.assertLessEqual(w, 800)
        self.assertLessEqual(h, 520)

    def test_main_window_is_one_persistent_view(self):
        source = (HERE/'hookshot_qt.py').read_text()
        tree = ast.parse(source)
        self.assertNotIn('class StepDialog(', source)
        self.assertIn('self.wizard_stack.setCurrentWidget(', source)
        self.assertIn("self.show_sequence('source')", source)
        self.assertNotIn('self.close()  # Source-step Cancel', source)
        self.assertIn('self.show_launch_summary(existing=True)', source)
        self.assertIn('app.setQuitOnLastWindowClosed(True)', source)
        self.assertIn('self._exiting = True', source)
        self.assertNotIn('self.tools_dialog.show()', source)
        for method in ('setup_sequence_dialogs','show_sequence','show_tools','closeEvent','launch_game'):
            self.assertIsNotNone(cls_fn(tree,'Window',method))

    def test_profile_manager_close_cannot_resurrect_builder(self):
        src = (HERE/'hookshot_qt.py').read_text()
        f = cls_fn(ast.parse(src), 'Window', 'on_adventure_window_closed')
        self.assertTrue(any(isinstance(n, ast.Attribute) and n.attr=='_exiting'
                            for n in ast.walk(f)))
        profile = (HERE/'profile_gui.py').read_text()
        self.assertIn('if event.isAccepted() and self.on_close is not None:',profile)
        self.assertIn('on_close=self.on_adventure_window_closed',src)

    def test_shader_and_adventure_navigation_both_present(self):
        src = (HERE/'hookshot_qt.py').read_text()
        self.assertIn('self.tool_sections.addTab(warm',src)
        self.assertIn('self.tool_sections.addTab(states_page',src)
        self.assertIn("self.launch_tabs.addTab(adventure",src)
        self.assertIn("self.tweaks_sections.addTab(self.tool_page",src)
        self.assertIn("self.launch_tabs.addTab(diagnostics",src)
        self.assertIn('self.profile_store.get(active_profile_id)',src)
        self.assertIn('self.profile_store.game_command(active_profile_id)', src)
        self.assertIn('\"--user\"', (HERE/'profile_store.py').read_text())

    def test_theme_unified_purple_and_bright_progress(self):
        qss=(HERE/'hookshot_theme.qss').read_text()
        builder=(HERE/'hookshot_qt.py').read_text()
        profile=(HERE/'profile_gui.py').read_text()
        self.assertIn('qlineargradient',qss)
        self.assertIn('#ffe978',qss)
        self.assertIn('#120f20',qss)
        self.assertIn('apply_theme(persist=False)',builder)
        self.assertIn('apply_theme(persist=False)',profile)
        self.assertNotIn('self.adventure_window.setStyleSheet(',builder)
        self.assertIn('fit_window(self, preferred=(1000, 640)',profile)
        self.assertIn('fit_window(self, preferred=(1000, 640)',builder)

    def test_theme_options_are_distinct_and_shared(self):
        from ui_theme import THEMES, normalized_theme, stylesheet_for, theme_labels
        self.assertEqual(tuple(key for key, label, file in THEMES), ('goron', 'gannon', 'zelda', 'purple', 'ruby', 'blue_crystal', 'zelda_gold', 'zelda_pearl'))
        self.assertEqual(normalized_theme('unexpected'), 'purple')
        self.assertEqual(normalized_theme('zelda'), 'zelda')
        labels=dict(theme_labels())
        self.assertEqual(labels['zelda'], 'Link')
        self.assertEqual(labels['zelda_gold'], 'Tri-force')
        self.assertEqual(labels['zelda_pearl'], 'Zelda')
        self.assertEqual(labels['blue_crystal'], 'Ruto')
        self.assertEqual(labels['gannon'], 'Gannon')
        self.assertEqual(labels['purple'], 'Royal')
        self.assertEqual(labels['goron'], 'Darunia')
        zelda=stylesheet_for('zelda')
        purple=stylesheet_for('purple')
        self.assertNotEqual(zelda,purple)
        for qss in (zelda,purple,stylesheet_for('zelda_gold'),stylesheet_for('blue_crystal'),stylesheet_for('gannon'),stylesheet_for('zelda_pearl'),stylesheet_for('goron')):
            self.assertIn('QTabWidget::pane',qss)
            self.assertIn('QPushButton#primary',qss)
            self.assertIn('qlineargradient',qss)
        self.assertIn('#0a1618',zelda)
        self.assertIn('#dbb756',zelda)
        self.assertIn('#f76bd5',zelda)
        self.assertIn('#120f20',purple)
        self.assertIn('#191207',stylesheet_for('zelda_gold'))
        self.assertIn('#071325',stylesheet_for('blue_crystal'))
        self.assertIn('#09080c',stylesheet_for('gannon'))

    def test_theme_selection_is_in_main_settings_only(self):
        builder=(HERE/'hookshot_qt.py').read_text()
        profile=(HERE/'profile_gui.py').read_text()
        self.assertIn('self.theme_combo.currentIndexChanged.connect(self.change_theme)', builder)
        self.assertNotIn('self.theme_combo', profile)
        self.assertIn("self.tabs.addTab(page, 'Settings')", builder)
        self.assertIn("self.settings_sections.addTab(appearance, 'Appearance')", builder)

    def test_manual_editor_removed_and_randomizer_grid_preserved(self):
        profile=(HERE/'profile_gui.py').read_text()
        self.assertNotIn('class AdventureInfoDialog',profile)
        self.assertNotIn('Edit manual details…',profile)
        self.assertIn('options_grid = QGridLayout()',profile)


if __name__=='__main__':
    unittest.main()
