"""Theme identity and compatibility tests for all seven Adventure v0.8 palettes."""
from pathlib import Path
import unittest
from ui_theme import THEMES, SURFACE_COLORS, theme_labels, stylesheet_for, normalized_theme
from adventure_v08_settings_ui import resolve_theme_colors
HERE=Path(__file__).resolve().parent
EXPECTED=(('goron','Darunia'),('gannon','Gannon'),('zelda','Link'),('purple','Royal'),
          ('blue_crystal','Ruto'),('zelda_gold','Tri-force'),('zelda_pearl','Zelda'))
class SevenThemeTests(unittest.TestCase):
    def test_stable_existing_ids_and_labels(self):
        self.assertEqual(theme_labels(),EXPECTED)
        self.assertEqual(len(THEMES),7)
        self.assertEqual(len(SURFACE_COLORS),7)
        for key,label in EXPECTED:
            self.assertEqual(normalized_theme(key),key)
            self.assertIsNotNone(resolve_theme_colors(label))
        self.assertEqual(normalized_theme('invalid'),'purple')
    def test_distinct_surfaces_and_goron_palettes(self):
        self.assertEqual(len(set(SURFACE_COLORS.values())),7)
        self.assertIn('#f5c649',stylesheet_for('zelda_gold'))
        goron=stylesheet_for('goron')
        self.assertIn('#101114',goron)
        self.assertIn('#ffac72',goron)
        self.assertIn('qlineargradient',goron)
        self.assertIn('QFrame#profileCardFrame',goron)
        for code in ('success','warning','error'):
            self.assertIn(f'status="{code}"',goron)
    def test_no_missing_style_files(self):
        for key,label,file in THEMES:
            self.assertTrue((HERE/file).is_file(), (key,file))
            self.assertIn('QPushButton#primary',stylesheet_for(key))
            self.assertIn('QWidget#launcherRoot',stylesheet_for(key))
    def test_royal_uses_existing_purple_style_and_id(self):
        self.assertIn(('purple','Royal','hookshot_theme.qss'),THEMES)
        self.assertEqual(resolve_theme_colors('Royal'),resolve_theme_colors('purple vaporwave'))

if __name__=='__main__': unittest.main()
