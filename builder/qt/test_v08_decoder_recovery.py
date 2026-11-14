"""Regression contracts for non-destructive runtime decoder recovery."""
from __future__ import annotations
from pathlib import Path
from unittest import TestCase, mock
import ast
import importlib.util

HERE = Path(__file__).resolve().parent


class DecoderRecoveryTests(TestCase):
    def test_decoder_bootstrap_script_is_standard_library_and_explicit(self):
        code = (HERE / 'setup_save_icon_decoder.py').read_text()
        ast.parse(code)
        self.assertIn("'--only-binary=:all:'", code)
        self.assertIn("'--target'", code)
        self.assertIn('texture2ddecoder==1.0.6', code)
        self.assertNotIn('venv', code.split('def install(')[1])
        self.assertNotIn('sudo', code)

    def test_missing_dependencies_discovered_without_installing(self):
        from setup_save_icon_decoder import missing_requirements, REQUIREMENTS
        with mock.patch('importlib.util.find_spec', return_value=None):
            self.assertEqual(set(missing_requirements()), set(REQUIREMENTS.values()))

    def test_import_has_no_install_side_effects(self):
        with mock.patch('subprocess.run', side_effect=AssertionError('silent installation')):
            from setup_save_icon_decoder import decoder_ready
            self.assertIsInstance(decoder_ready(), bool)

    def test_qt_rebuild_button_offers_explicit_repair(self):
        text = (HERE / 'hookshot_qt.py').read_text()
        self.assertIn('Install / Repair Icon Decoder', text)
        self.assertIn('self.install_save_icon_decoder', text)
        self.assertIn('self.prepare_adventure_save_assets(force=True)', text)
        self.assertIn('QMessageBox.StandardButton.Yes | QMessageBox.StandardButton.No', text)
        self.assertIn('sys.executable', text)

    def test_cache_skips_mass_decode_attempts_with_missing_astc(self):
        text = (HERE / 'save_asset_cache.py').read_text()
        self.assertIn('self.decoder_factory is GameAssets and not decoder_ready()', text)
        self.assertIn('Game icon ASTC decoder missing:', text)
        self.assertIn('not system Python', text)

    def test_theme_separation(self):
        from ui_theme import SURFACE_COLORS, stylesheet_for
        from adventure_v08_settings_ui import resolve_theme_colors
        self.assertNotEqual(SURFACE_COLORS['goron'], SURFACE_COLORS['zelda_gold'])
        self.assertIn('stop:1 #d75524', stylesheet_for('goron'))
        self.assertIn('stop:1 #664379', stylesheet_for('zelda_pearl'))
        self.assertIn('background:#fcf8fd', stylesheet_for('zelda_pearl'))
        self.assertIn('color:#ffac72', stylesheet_for('goron'))
        self.assertEqual(resolve_theme_colors('Zelda').fill, '#2c2b60')
        self.assertEqual(resolve_theme_colors('Goron').accent, '#ff8d42')

if __name__ == '__main__':
    import unittest
    unittest.main()
