"""Final Adventure UI v0.8 contracts for magic, verified equipment, labels and positions.

No Qt, ROM data or user saves needed: small synthetic read-only fields.
"""
from pathlib import Path
import ast
import json
import unittest

from adventure_panel_model import (ADVENTURE_UI_VERSION, INVENTORY_GRID_ROLES,
                                   PROGRESSIVE_TIERS, item_appearance, bottle_caption)
from save_reader import (decode_pair, HEADER_PROGRESS, PROGRESS, STRENGTH_STOCKS,
                         TUNIC_STOCKS, ECHO_GEM_STOCK)
from test_save_reader_v06 import make_save
from zelda_summary_data import magic_upgrade_state, visual_from_stats

HERE=Path(__file__).resolve().parent

class FinalV08Tests(unittest.TestCase):
    def test_ocarina_and_lens_swapped(self):
        self.assertEqual(ADVENTURE_UI_VERSION,'v0.8')
        self.assertEqual(INVENTORY_GRID_ROLES[5:7],('progressive_ocarina','lens_of_truth'))
        self.assertEqual(PROGRESSIVE_TIERS['progressive_ocarina'],('ocarina_fairy','ocarina_time'))

    def test_magic_upgrade_is_capacity_not_current_mp(self):
        for max_raw,now_raw,level in ((0,0,0),(240,0,1),(240,200,1),(480,20,2)):
            stats,_,_=decode_pair(make_save(HEADER_PROGRESS,{
                PROGRESS['magic_max']:max_raw,PROGRESS['magic_current']:now_raw}),None)
            self.assertEqual(stats['magic_upgrade_level'],level)
            self.assertEqual(visual_from_stats(stats).magic_level,level)
        self.assertIsNone(magic_upgrade_state({}))
        self.assertIsNone(magic_upgrade_state({'max_magic':13}))

    def test_direct_strength_tunic_echo_fields_and_override(self):
        vals={**{h:0 for h in STRENGTH_STOCKS.values()},
              **{h:0 for h in TUNIC_STOCKS.values()},ECHO_GEM_STOCK:1}
        vals[STRENGTH_STOCKS['bracelet_goron']]=1
        vals[TUNIC_STOCKS['tunic_kokiri']]=1
        stats,_,_=decode_pair(make_save(HEADER_PROGRESS,vals),None)
        self.assertEqual(item_appearance(stats,'progressive_strength').role,'bracelet_goron')
        self.assertTrue(item_appearance(stats,'tunic_kokiri').owned)
        self.assertFalse(item_appearance(stats,'tunic_zora').owned)
        self.assertTrue(item_appearance(stats,'agony_stone').owned)
        vals[STRENGTH_STOCKS['gauntlets_silver']]=1
        stats,_,_=decode_pair(make_save(HEADER_PROGRESS,vals),None)
        self.assertEqual(item_appearance(stats,'progressive_strength').role,'gauntlets_silver')
        vals[STRENGTH_STOCKS['gauntlets_gold']]=1
        stats,_,_=decode_pair(make_save(HEADER_PROGRESS,vals),None)
        self.assertEqual(item_appearance(stats,'progressive_strength').role,'gauntlets_gold')

    def test_user_supplied_manifest_is_complete(self):
        manifest=json.loads((HERE/'assets/game_icons/icon_manifest.json').read_text())
        self.assertEqual(len(manifest['icons']),132)  # 119 user-mapped roles, including both hatched Cuccos
        self.assertEqual(sum(d.get('verified') is True for d in manifest['icons'].values()),119)
        self.assertEqual(sum(k.startswith('bag_') for k in manifest['icons']),15)
        self.assertTrue(all(v.get('verified') and v.get('archive') and v.get('texture')
                            for k,v in manifest['icons'].items() if k.startswith('bag_')))
        for role in ('deku_sticks','deku_nuts','deku_seeds','business_scrub_seed',
                     'hammer','heart_quarter','heart_half','heart_three_quarters'):
            self.assertIn(role,manifest['icons'])
            self.assertTrue(manifest['icons'][role].get('texture'))

    def test_equipment_label_and_spacing_source(self):
        source=(HERE/'zelda_summary_widget.py').read_text()
        ast.parse(source)
        for token in ("text_rect('SWORDS'", "text_rect('SHIELDS'",
                      "text_rect('TUNICS'", "badge = QRectF(x+size-badge_width-2",
                      "y = INVENTORY_GRID_Y+(i // INVENTORY_COLUMNS)*INVENTORY_GRID_ROW_STEP", "text_rect(caption, x-20, y+37",
                      "text_rect('MASKS'", "paint_magic_upgrade(p, d.magic_level"):
            self.assertIn(token,source)
        for old in ("text_rect(f'{sword}   •   {shield}'", "text_rect('ECHO GEM'",
                    "text_rect('SCRUB SEED'"):
            self.assertNotIn(old,source)
        self.assertEqual(bottle_caption({'owned':True,'label':'Bugs'}),'Bug')

if __name__=='__main__': unittest.main()
