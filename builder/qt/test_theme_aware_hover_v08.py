"""Seven-theme popups inherit the same palette as the actual menu."""
from __future__ import annotations

import re
from pathlib import Path
import unittest

from ui_theme import THEMES, SURFACE_COLORS, adventure_tooltip_qss, stylesheet_for

HERE = Path(__file__).parent


class ThemeAwareHoverTests(unittest.TestCase):
    def test_all_seven_themes_have_distinct_tooltip_chrome(self):
        self.assertEqual(len(THEMES), 7)
        styles = {}
        for theme, _label, _qss in THEMES:
            qss = adventure_tooltip_qss(theme)
            self.assertIn('QToolTip {', qss)
            self.assertIn('qlineargradient', qss)
            self.assertIn('border-radius:7px', qss)
            self.assertTrue(all(re.fullmatch(r'#[0-9a-f]{6}', x)
                                for x in re.findall(r'#[0-9a-fA-F]{6}', qss)))
            self.assertIn('QToolTip {', stylesheet_for(theme))
            self.assertTrue(stylesheet_for(theme).endswith(qss))
            styles[theme] = qss
        self.assertEqual(len(set(styles.values())), 7)
        self.assertEqual(set(SURFACE_COLORS), set(styles))

    def test_tooltips_update_with_theme_selection(self):
        launcher = (HERE / 'hookshot_qt.py').read_text()
        widget = (HERE / 'zelda_summary_widget.py').read_text()
        self.assertIn('apply_theme(self.theme_combo.currentData())', launcher)
        self.assertIn('self.detail_visual.refresh_hover_theme()', launcher)
        self.assertIn('QToolTip.hideText()', widget)
        self.assertIn('QToolTip.showText(event.globalPosition().toPoint(), hit, self)', widget)
        for label in ('wallet_name', 'bottle_hover_text(raw, number)',
                      'bag_hover_text(raw, bag_key)', 'STONES[i]', 'MEDALLIONS[i]',
                      'item_hover_text(raw, role)'):
            self.assertIn(label, widget)

    def test_older_icon_manifests_receive_new_unmapped_progression_roles(self):
        import tempfile
        import json
        from game_icon_core import load_manifest, default_manifest
        from adventure_panel_model import BAG_UPGRADES
        expected = {f'bag_{name}_{tier}' for name, *_ in BAG_UPGRADES
                    for tier in (1, 2, 3)}
        self.assertEqual(len(expected), 15)
        self.assertTrue(expected.issubset(default_manifest()['icons']))
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / 'icon_manifest.json'
            old = {'schema_version': 1, 'game_version': '2.1.0',
                   'icons': {'heart_full': {'archive': 'UI/Tex/Icon/Icon_Heart.bntx.zs',
                                           'texture': 'Icon_Heart', 'crop': None,
                                           'verified': True, 'notes': 'Personal mapping'}}}
            path.write_text(json.dumps(old), encoding='utf-8')
            merged = load_manifest(path)['icons']
            self.assertEqual(merged['heart_full'], old['icons']['heart_full'])
            self.assertTrue(expected.issubset(merged))
            self.assertTrue(all(merged[role]['archive'] is None
                                and merged[role]['verified'] is False
                                for role in expected))
            self.assertEqual(json.loads(path.read_text()), old,
                             'Auto migration must not modify the user manifest on disk')

    def test_tooltips_preserve_newline_details(self):
        from adventure_panel_model import item_hover_text, bottle_hover_text, bag_hover_text
        self.assertIn('Arrows: 23', item_hover_text({'arrows': 23}, 'bow'))
        self.assertIn('Fairy', bottle_hover_text({'bottle_slots': [
            {'slot': 1, 'owned': True, 'role': 'bottle_fairy', 'label': 'Fairy'}
        ]}, 1))
        self.assertIn('Bigger', bag_hover_text({'bomb_bag_capacity': 30}, 'bombs'))


if __name__ == '__main__':
    unittest.main()
