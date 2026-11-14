"""Seven-theme compatibility + royal-blue Zelda and volcanic Goron regression tests."""
from __future__ import annotations
import ast
from pathlib import Path
import unittest
from adventure_v08_settings_ui import resolve_theme_colors, selected_save_info_qss
from ui_theme import THEMES, SURFACE_COLORS, normalized_theme, theme_labels, stylesheet_for

HERE=Path(__file__).resolve().parent

class ZeldaThemeV08Tests(unittest.TestCase):
    def test_stable_internal_keys_and_visible_names(self):
        self.assertEqual(tuple(k for k,_,_ in THEMES),
                         ('goron','gannon','zelda','purple','blue_crystal','zelda_gold','zelda_pearl'))
        labels=dict(theme_labels())
        self.assertEqual(labels['zelda'],'Link')
        self.assertEqual(labels['zelda_gold'],'Tri-force')
        self.assertEqual(labels['zelda_pearl'],'Zelda')
        self.assertEqual(labels['purple'], 'Royal')
        self.assertEqual(labels['blue_crystal'], 'Ruto')
        self.assertEqual(labels['goron'], 'Darunia')
        self.assertEqual(normalized_theme('zelda'),'zelda')  # existing user setting
        self.assertEqual(normalized_theme('zelda_gold'),'zelda_gold')
        self.assertEqual(normalized_theme('zelda_pearl'),'zelda_pearl')

    def test_all_seven_themes_include_global_gradient_for_main_settings_and_details(self):
        self.assertEqual(len(THEMES),7)
        self.assertEqual(len(SURFACE_COLORS),7)
        for key,label,filename in THEMES:
            self.assertTrue((HERE/filename).is_file())
            css=stylesheet_for(key)
            for selector in ('QWidget#launcherRoot','QWidget#launcherSettingsPage',
                             'QWidget#launcherDetailPage','QFrame#profileCardFrame',
                             'QTabWidget::pane'):
                self.assertIn(selector,css)
            self.assertIn('qlineargradient',css)
            self.assertIn('sohSaveLocation',selected_save_info_qss(label))

    def test_new_theme_is_royal_blue_violet_with_white_controls(self):
        css=stylesheet_for('zelda_pearl')
        for color in ('#17172d','#6566a9','#f5e7f3','#ca9a70'):
            self.assertIn(color,css)
        self.assertNotEqual(css,stylesheet_for('zelda'))
        self.assertNotEqual(css,stylesheet_for('zelda_gold'))
        self.assertEqual(resolve_theme_colors('Zelda').fill, '#2c2b60')
        self.assertIn('color:#fff7f9',selected_save_info_qss('Zelda'))
        self.assertIn('color:#e5d9ed',selected_save_info_qss('Zelda'))
        self.assertEqual(resolve_theme_colors('Royal').accent, '#b58af2')
        self.assertEqual(resolve_theme_colors('Darunia').fill, '#222024')
        self.assertEqual(resolve_theme_colors('Tri-force').accent, '#efbd3e')

    def test_theme_applies_to_existing_save_renderer_and_keeps_manual_backups(self):
        renderer=(HERE/'zelda_summary_widget.py').read_text()
        launcher=(HERE/'hookshot_qt.py').read_text()
        for path in ('zelda_summary_widget.py','hookshot_qt.py','ui_theme.py'):
            ast.parse((HERE/path).read_text())
        self.assertIn("'zelda_pearl': 'zelda'",renderer)
        self.assertIn("'zelda_pearl':'zelda'",launcher)
        self.assertIn("'goron':'darunia'",launcher)
        self.assertIn('Seven themes; changes apply',launcher)
        self.assertIn('self.manual_save_backup_button',launcher)
        self.assertIn('Business', (HERE/'save_reader.py').read_text() +
                      (HERE/'adventure_panel_model.py').read_text().replace('business_scrub_seed','Business'))
        self.assertIn("text_rect('TUNICS'",renderer)

if __name__=='__main__':
    unittest.main()
