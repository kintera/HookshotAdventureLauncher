"""Headless regression checks for Ruby theme wiring and bundled resources."""
from pathlib import Path
import sys
import unittest

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import ui_theme


class RubyThemeTests(unittest.TestCase):
    def test_ruby_is_registered(self):
        labels = dict(ui_theme.theme_labels())
        self.assertEqual(len(labels), 8)
        self.assertEqual(labels["ruby"], "Ruby")
        self.assertEqual(ui_theme.normalized_theme("ruby"), "ruby")

    def test_all_stylesheets_load(self):
        for key, label in ui_theme.theme_labels():
            with self.subTest(theme=label):
                css = ui_theme.stylesheet_for(key)
                self.assertIn("QWidget", css)
                self.assertIn("QPushButton", css)
                self.assertIn("QToolTip", css)

    def test_theme_layout_wrappers_transparent(self):
        for key, label in ui_theme.theme_labels():
            with self.subTest(theme=label):
                css = ui_theme.stylesheet_for(key)
                self.assertIn("QWidget { background:transparent; }", css)
                self.assertIn("QWidget#launcherAdventurePage", css)
                self.assertIn("QFrame#profileCardFrame", css)

    def test_ruby_is_distinct_from_gannon(self):
        self.assertNotEqual(ui_theme.SURFACE_COLORS["ruby"], ui_theme.SURFACE_COLORS["gannon"])
        self.assertNotEqual(ui_theme.stylesheet_for("ruby"), ui_theme.stylesheet_for("gannon"))


if __name__ == "__main__":
    unittest.main()
