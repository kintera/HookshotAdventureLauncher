"""Adventure UI v0.8 SoH-layout regression suite.

Headless contracts run without Qt or a game dump. Optional QWidget rendering
checks run on systems with an offscreen-capable PyQt6 installation.
"""
import ast
import json
import os
from pathlib import Path
import unittest
from unittest.mock import patch

from adventure_panel_model import (
    ADVENTURE_UI_VERSION, INVENTORY_ROLES, EQUIPMENT_ROLES,
    OTHER_GEAR_ROLES, item_appearance, safe_character_name,
)
from adventure_save_display import (
    format_save_location, readable_equipment, save_kind_badge_text,
)
from adventure_v08_settings_ui import selected_save_info_qss
from ui_theme import THEMES
from ui_layout import bounded_size
from zelda_summary_data import heart_state, visual_from_stats, rupee_text

HERE = Path(__file__).resolve().parent
SOURCE = (HERE/'hookshot_qt.py').read_text(encoding='utf-8')
DRAW = (HERE/'zelda_summary_widget.py').read_text(encoding='utf-8')


class SoHModelTests(unittest.TestCase):
    def test_remains_v08(self):
        self.assertEqual(ADVENTURE_UI_VERSION, 'v0.8')

    def test_inventory_roles_are_unique_and_do_not_include_obsolete_mappings(self):
        roles = (*INVENTORY_ROLES, *EQUIPMENT_ROLES, *OTHER_GEAR_ROLES)
        self.assertEqual(len(roles), len(set(roles)))
        self.assertEqual(len(INVENTORY_ROLES), 21)
        self.assertNotIn('boots_kokiri', roles)
        self.assertNotIn('rupee_silver', roles)

    def test_unknown_items_remain_unknown(self):
        for role in INVENTORY_ROLES + EQUIPMENT_ROLES + OTHER_GEAR_ROLES:
            item = item_appearance({}, role)
            self.assertIsNone(item.owned, role)
            self.assertIsNone(item.quantity, role)
            self.assertFalse(item.equipped, role)

    def test_explicit_flags_only_and_positive_supply(self):
        self.assertTrue(item_appearance({'inventory_flags': {'bow': True}}, 'bow').owned)
        self.assertFalse(item_appearance({'inventory_flags': {'bow': False}}, 'bow').owned)
        self.assertIsNone(item_appearance({'inventory_flags': {'bow': 1}}, 'bow').owned)
        positive = item_appearance({'bombs': 18}, 'bomb')
        self.assertTrue(positive.owned)
        self.assertEqual(positive.quantity, 18)
        self.assertIsNone(item_appearance({'bombs': 0}, 'bomb').owned)
        self.assertIsNone(item_appearance({'arrows': 35}, 'bow').owned,
                          'Arrows do not prove a bow is owned')

    def test_equipped_swords_and_shields_are_selectively_highlighted(self):
        stats = {'equipped_sword':'Weapon_Sword_Kokiri',
                 'equipped_shield':'Armor_Shield_Hylia'}
        self.assertTrue(item_appearance(stats, 'sword_kokiri').equipped)
        self.assertTrue(item_appearance(stats, 'shield_hylian').equipped)
        self.assertTrue(item_appearance(stats, 'sword_kokiri').owned)
        self.assertFalse(item_appearance(stats, 'sword_master').equipped)
        self.assertIsNone(item_appearance(stats, 'sword_master').owned)

    def test_character_uses_verified_name_not_profile_nickname(self):
        self.assertEqual(safe_character_name({'rupees':300}), 'Unknown')
        self.assertEqual(safe_character_name({'player_name':' Link  '}), 'Link')
        self.assertEqual(safe_character_name(None), 'Unknown')

    def test_location_and_equipment_are_human_readable(self):
        self.assertEqual(format_save_location('ZoraRiver', 'Child'), 'Child: Zora River')
        self.assertEqual(format_save_location('KokiriForest', 'Adult'), 'Adult: Kokiri Forest')
        self.assertEqual(format_save_location(None, None), 'Unknown Location')
        for raw, name in [('Weapon_Sword_Kokiri','Kokiri Sword'),
                          ('Weapon_Sword_Master','Master Sword'),
                          ('Weapon_Sword_Biggoron','Biggoron Sword'),
                          ('Armor_Shield_Hylia','Hylian Shield'),
                          ('weapon_shield_deku','Deku Shield'),
                          ('weapon_shield_mirror','Mirror Shield'),
                          ('weapon_hammer','Megaton Hammer')]:
            self.assertEqual(readable_equipment(raw), name)

    def test_vanilla_badges_never_infer_rando_in_v08(self):
        for data in (None, {}, {'kind':'randomizer-planned'},
                     {'metadata': {'is_randomizer': True}}):
            self.assertEqual(save_kind_badge_text(data), 'Vanilla Save')

    def test_quarter_hearts_double_defense_and_unknown_hearts(self):
        d = visual_from_stats({'hearts': 8, 'current_hearts': 3.75,
                               'double_defense': True})
        self.assertEqual(heart_state(d,3),('owned-filled',.75))
        self.assertEqual(heart_state(d,4),('owned-empty',0.0))
        self.assertEqual(heart_state(d,8),('uncollected',0.0))
        self.assertTrue(d.double_defense)
        for filled, fraction in [(1.25,.25),(1.5,.5),(1.75,.75),(2.0,1.0)]:
            self.assertEqual(heart_state(visual_from_stats({'hearts':5,'current_hearts':filled}),1)[1], fraction)
        self.assertEqual(heart_state(visual_from_stats({}),0)[0],'unknown')

    def test_wallet_counts_stay_factual(self):
        for wallet, capacity in [('Kokiri',200),('Adult',500),('Giant',1000)]:
            self.assertEqual(rupee_text(visual_from_stats({'wallet':wallet,
                'wallet_capacity':capacity, 'rupees':17})),f'17/{capacity}')


class SoHSourceContracts(unittest.TestCase):
    def test_source_parses_and_ui_keeps_launch_separate(self):
        ast.parse(SOURCE)
        ast.parse(DRAW)
        detail = SOURCE[SOURCE.index('def make_adventure_detail_page('):
                        SOURCE.index('def set_save_preview(')]
        self.assertIn('layout.addWidget(self.detail_save_info_frame, 1)',detail)
        self.assertIn('actions.addWidget(self.detail_launch)',detail)
        self.assertIn("another.clicked.connect(lambda: self.show_sequence('launch'))", detail)
        self.assertIn('self.detail_preview',detail)
        self.assertNotIn('self.detail_character', detail)
        self.assertIn('self.detail_location',detail)
        self.assertIn('self.detail_age', detail)
        self.assertNotIn('self.detail_type', detail)
        self.assertIn('self.detail_kind_badge',detail)
        self.assertIn('self.detail_visual = ZeldaStatsWidget(compact=False)',detail)

    def test_profile_select_does_not_launch(self):
        self.assertIn('for i in range(3)', SOURCE)
        self.assertIn('self.show_adventure_details(profile[\'id\'])', SOURCE)
        self.assertIn('self.launch_game(profile_id=self.detail_profile_id)',SOURCE)
        self.assertIn('self.launcher_type_badges[i]', SOURCE)
        self.assertIn('self.launcher_icon_summaries[i].set_stats(stats_data)',SOURCE)

    def test_preserves_settings_and_icon_cache(self):
        self.assertIn('self.settings_header_button',SOURCE)
        self.assertIn('self.rebuild_save_assets_button',SOURCE)
        self.assertIn('self.tweaks_sections.addTab(self.tool_page',SOURCE)
        self.assertIn('activate_game_cache(state.directory)',SOURCE)
        self.assertIn('ensure_save_assets(self, cache',SOURCE)
        self.assertNotIn('icon_manifest.py',SOURCE)
        self.assertNotIn('mapped_icon_path', (HERE/'zelda_icon_assets.py').read_text())

    def test_all_seven_theme_panels(self):
        self.assertEqual(len(THEMES),7)
        for _key,label,_file in THEMES:
            css=selected_save_info_qss(label)
            self.assertIn('qlineargradient',css)
            self.assertIn('sohSaveLocation',css)
            self.assertIn('v08SelectedSaveInfo',css)

    def test_stable_1280x800_and_compact_bounds(self):
        self.assertEqual(bounded_size(1280,800),(1000,640))
        width,height=bounded_size(750,475)
        self.assertLessEqual(width,750)
        self.assertLessEqual(height,475)
        self.assertIn('self.detail_visual.set_stats(stats)',SOURCE)

    def test_no_game_images_are_included_as_generated_pngs(self):
        manifest=json.loads((HERE/'assets/game_icons/icon_manifest.json').read_text())
        self.assertEqual(manifest['schema_version'],1)
        for role in ('wallet_child','wallet_adult','wallet_giant',
                     'heart_double_defense','heart_full','heart_empty'):
            self.assertIn(role,manifest['icons'])
        self.assertFalse(list((HERE/'assets/game_icons').glob('*.png')))


try:
    import PyQt6  # noqa: F401
    QT_AVAILABLE = True
except ImportError:
    QT_AVAILABLE = False


@unittest.skipUnless(QT_AVAILABLE, 'PyQt6 is not installed; render on target machine')
class OptionalQtRenderingTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        os.environ.setdefault('QT_QPA_PLATFORM','offscreen')
        from PyQt6.QtWidgets import QApplication
        cls.app = QApplication.instance() or QApplication([])

    def test_expand_and_compact_paint_in_all_themes(self):
        from PyQt6.QtGui import QPixmap
        from zelda_summary_widget import ZeldaStatsWidget
        stats={'hearts':7, 'current_hearts':5.25, 'double_defense':True,
               'rupees':89, 'wallet':'Adult', 'wallet_capacity':500,
               'gold_skulltulas':12, 'bottles':2,
               'equipped_sword':'Weapon_Sword_Master',
               'equipped_shield':'Armor_Shield_Hylia'}
        for key,_label,_file in THEMES:
            with self.subTest(theme=key), patch('zelda_summary_widget.selected_theme',return_value=key):
                for compact,size in [(False,(1080,356)), (True,(650,58)), (False,(700,205))]:
                    w=ZeldaStatsWidget(compact=compact)
                    w.set_stats(stats)
                    w.resize(*size)
                    target=QPixmap(*size)
                    target.fill()
                    w.render(target)
                    self.assertFalse(target.isNull())
                    w.deleteLater()


if __name__ == '__main__':
    unittest.main()
