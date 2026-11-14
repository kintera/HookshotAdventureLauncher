"""Regression tests for v0.8 read-only Scrub Seeds, item arrangement, bottles and theme gradients."""
from __future__ import annotations
import ast
import json
from pathlib import Path
import unittest

from adventure_panel_model import (
    ADVENTURE_UI_VERSION, INVENTORY_GRID_ROLES, INVENTORY_ROLES, INVENTORY_COLUMNS, INVENTORY_ROWS,
    EQUIPMENT_ROLES, OTHER_GEAR_ROLES, bottle_appearance, item_appearance,
)
from save_reader import BOTTLE_SLOTS, BOTTLE_CONTENTS, SUPPLIES, HEADER_PROGRESS, decode_pair
from test_save_reader_v06 import make_save
from ui_theme import THEMES, stylesheet_for

HERE=Path(__file__).parent


def saved_bottles(states, scrub=4, beans=0):
    fields = {SUPPLIES['business_scrub_seeds'][0]: scrub,
              SUPPLIES['magic_beans'][0]: beans}
    for (stock, valid, content), (owned, enum) in zip(BOTTLE_SLOTS, states):
        fields[stock] = owned
        fields[valid] = owned
        fields[content] = enum if enum < 0x80000000 else enum - 0x100000000
    return decode_pair(make_save(HEADER_PROGRESS, fields), None)[0]


class ExpandedInventoryTests(unittest.TestCase):
    def test_v08_is_still_v08(self):
        self.assertEqual(ADVENTURE_UI_VERSION, 'v0.8')

    def test_item_order_and_categories(self):
        self.assertEqual(INVENTORY_ROLES[7:14], ('bow', 'progressive_hookshot', 'hammer', 'bomb', 'bombchu', 'business_scrub_seed', 'boots_iron'))
        self.assertEqual(INVENTORY_ROLES[12], 'business_scrub_seed')
        self.assertEqual((INVENTORY_COLUMNS, INVENTORY_ROWS), (7,4))
        self.assertEqual(len(INVENTORY_ROLES),21)
        self.assertEqual(len(INVENTORY_GRID_ROLES),28)
        self.assertEqual(INVENTORY_GRID_ROLES[21:26],tuple('bottle_slot_'+str(i) for i in range(1,6)))
        self.assertEqual(INVENTORY_GRID_ROLES[26:],('agony_stone','progressive_strength'))
        self.assertNotIn('deku_seeds', INVENTORY_GRID_ROLES)
        self.assertNotIn('arrows', INVENTORY_GRID_ROLES)
        self.assertNotIn('hammer', EQUIPMENT_ROLES)
        self.assertIn('hammer', INVENTORY_ROLES)
        self.assertEqual(len(set(INVENTORY_ROLES+EQUIPMENT_ROLES+OTHER_GEAR_ROLES)),len(INVENTORY_ROLES+EQUIPMENT_ROLES+OTHER_GEAR_ROLES))

    def test_business_seeds_are_not_magic_beans_or_deku_seeds(self):
        stats=saved_bottles([(1, 0xBE69DE49)]*5,scrub=4,beans=2)
        self.assertEqual(stats['business_scrub_seeds'],4)
        self.assertEqual(stats['magic_beans'],2)
        self.assertIsNone(stats['deku_seeds'])
        self.assertEqual(item_appearance(stats,'business_scrub_seed').quantity,4)
        self.assertEqual(item_appearance(stats,'magic_bean').quantity,2)

    def test_all_five_bottles_use_content_enum_and_correct_role(self):
        contents=[(1,0xF383D6E5),(1,0x6F49E357),(1,0xEEF6E4B0),(1,0xBE69DE49),(0,0xBE69DE49)]
        stats=saved_bottles(contents)
        self.assertEqual(stats['bottles'],4)
        self.assertEqual([v['role'] for v in stats['bottle_slots']],
                         ['bottle_blue_potion','bottle_fairy','bottle_milk_half','bottle_empty',None])
        self.assertEqual(bottle_appearance(stats,5)['label'],'No bottle')
        self.assertFalse(bottle_appearance(stats,5)['owned'])

    def test_every_known_bottle_content_is_mapped_to_manifest_role(self):
        manifest=json.loads((HERE/'assets/game_icons/icon_manifest.json').read_text())['icons']
        for _enum, (role, label) in BOTTLE_CONTENTS.items():
            self.assertIn(role,manifest)
            self.assertTrue(label)

    def test_partial_bottle_decode_retains_known_slots(self):
        stats=saved_bottles([(1,0x1E834EF8),(1,0xDEADBEEF),(0,0xBE69DE49),
                            (1,0xEABA6067),(1,0xBE69DE49)])
        self.assertEqual(stats['bottles'],4)
        self.assertEqual(stats['bottle_slots'][0]['role'], 'bottle_fish')
        self.assertIsNone(stats['bottle_slots'][1]['role'])
        self.assertEqual(stats['bottle_slots'][1]['label'],'Unknown Contents')
        self.assertEqual(stats['bottle_slots'][3]['role'],'bottle_red_potion')
        self.assertEqual(stats['bottle_slots'][4]['role'],'bottle_empty')

    def test_unknown_bottles_not_silently_empty(self):
        stats,_,_=decode_pair(make_save(HEADER_PROGRESS,{}),None)
        self.assertIsNone(stats['bottles'])
        self.assertIsNone(bottle_appearance(stats,2)['owned'])
        self.assertEqual(bottle_appearance({},1)['label'],'Unknown')

    def test_inconsistent_bottle_stays_unknown_without_loss(self):
        stats=saved_bottles([(1,0x6F49E357)]*5)
        # Introduce one mismatched validity field by recreating the synthetic save.
        values={}
        for stock,valid,content in BOTTLE_SLOTS:
            values.update({stock:1,valid:1,content:0x6F49E357})
        values[BOTTLE_SLOTS[1][1]]=0
        values={k:(v if v < 0x80000000 else v-0x100000000) for k,v in values.items()}
        stats,_,_=decode_pair(make_save(HEADER_PROGRESS,values),None)
        self.assertIsNone(stats['bottles'])
        self.assertIsNone(stats['bottle_slots'][1]['owned'])
        self.assertEqual(stats['bottle_slots'][0]['role'],'bottle_fairy')

    def test_all_seven_themes_get_global_gradients(self):
        self.assertEqual(len(THEMES),7)
        for key, _label, _qss in THEMES:
            qss=stylesheet_for(key)
            self.assertIn('QWidget#launcherRoot',qss)
            self.assertIn('QFrame#profileCardFrame',qss)
            self.assertIn('QFrame#footer',qss)
            self.assertIn('QTabWidget::pane',qss)
            self.assertIn('qlineargradient',qss)

    def test_icon_roles_keep_verified_manifest_mappings(self):
        m=json.loads((HERE/'assets/game_icons/icon_manifest.json').read_text())
        self.assertGreaterEqual(len(m['icons']),88)
        self.assertTrue(all(v['verified'] for key, v in m['icons'].items()
                            if key not in ('business_scrub_seed','deku_sticks','deku_nuts','deku_seeds', 'gerudo_pass')
                            and not key.startswith(('bag_', 'trade_', 'song_'))))
        self.assertTrue(m['icons']['hammer']['verified'])
        self.assertEqual(m['icons']['hammer']['texture'],'Item_Hammer_PouchIcon')
        self.assertTrue(m['icons']['heart_piece']['verified'])
        self.assertIn('business_scrub_seed', m['icons'])
        self.assertTrue(m['icons']['agony_stone']['verified'])
        self.assertEqual(m['icons']['agony_stone']['texture'], 'Item_AgonyStone')
        for role in ('deku_sticks','deku_nuts','deku_seeds'):
            self.assertIn(role, m['icons'])
            if m['icons'][role].get('verified'):
                self.assertTrue(m['icons'][role].get('archive'))
                self.assertTrue(m['icons'][role].get('texture'))
        if m['icons']['business_scrub_seed'].get('verified'):
            self.assertTrue(m['icons']['business_scrub_seed'].get('archive'))
            self.assertTrue(m['icons']['business_scrub_seed'].get('texture'))

    def test_no_backup_prompt_and_read_only(self):
        launcher=(HERE/'hookshot_qt.py').read_text()
        renderer=(HERE/'zelda_summary_widget.py').read_text()
        for path in ('hookshot_qt.py','save_reader.py','zelda_summary_widget.py','adventure_panel_model.py'):
            ast.parse((HERE/path).read_text())
        self.assertIn('self.manual_save_backup_button',launcher)
        self.assertIn('bottle_appearance(raw, number)',renderer)
        self.assertIn('draw_scrub_seed',renderer)
        self.assertIn('item_appearance(raw,role)',renderer)


if __name__=='__main__': unittest.main()
