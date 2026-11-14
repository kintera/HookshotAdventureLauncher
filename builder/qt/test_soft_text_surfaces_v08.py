"""Theme v0.8: sharp text rectangles must not obscure the common gradients."""
from pathlib import Path
import unittest

import ui_theme
import adventure_v08_settings_ui as panel

ROOT = Path(__file__).parent


class SoftTextSurfaces(unittest.TestCase):
    def test_all_seven_theme_chrome_still_has_common_round_geometry(self):
        for key, label in ui_theme.theme_labels():
            with self.subTest(label=label):
                qss = ui_theme.stylesheet_for(key)
                self.assertIn('border-radius:9px;', qss)
                self.assertIn('QLabel#readyTitle', qss)
                self.assertIn('background:transparent; border:none;', qss)

    def test_zelda_and_darunia_have_transparent_unstyled_wrappers(self):
        for filename in ('hookshot_zelda_pearl.qss', 'hookshot_goron.qss'):
            with self.subTest(filename=filename):
                qss = (ROOT / filename).read_text(encoding='utf-8')
                self.assertIn('QWidget { background: transparent; }', qss)
                self.assertIn('QLabel#readyTitle', qss)
                self.assertIn('QLabel#statValue', qss)
                self.assertIn('background: transparent; border: none;', qss)
                self.assertIn('QPushButton#primaryButton', qss)

    def test_selected_save_info_uses_color_only_for_border_not_corner_fill(self):
        for theme in ('Royal', 'Zelda', 'Darunia', 'Tri-force', 'Ruto', 'Gannon', 'Link'):
            with self.subTest(theme=theme):
                tokens = panel.resolve_theme_colors(theme)
                qss = panel.selected_save_info_qss(theme)
                self.assertIn(f'border: 1px solid {tokens.border}', qss)
                self.assertNotIn(f'stop:0 {tokens.border},', qss)
                self.assertIn('border-radius: 9px', qss)
                self.assertIn('QFrame#v08SelectedSaveInfo QLabel { background: transparent;', qss)

    def test_versions_unchanged(self):
        from adventure_panel_model import ADVENTURE_UI_VERSION
        self.assertEqual(ADVENTURE_UI_VERSION, 'v0.8')


if __name__ == '__main__':
    unittest.main()
