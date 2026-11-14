"""Synthetic read-only stock tests for ranged weapons and trading fields."""
from __future__ import annotations

import ast
import hashlib
from pathlib import Path
import unittest

from adventure_panel_model import (
    gerudo_pass_hover_text, item_appearance, item_hover_text,
    trading_hover_text, trading_quest, latest_trading_item,
    trading_quest_overview_hover_text,
)
from save_reader import (
    ADULT_TRADE_STOCKS, CHILD_TRADE_STOCKS, GERUDO_PASS_STOCK,
    HEADER_PROGRESS, MASK_STOCKS, RANGED_STOCKS, decode_pair,
)
from test_save_reader_v06 import make_save

ROOT = Path(__file__).resolve().parent


class RangedWeaponAndTradeTests(unittest.TestCase):
    def decoded(self, fields):
        binary = make_save(HEADER_PROGRESS, fields)
        fingerprint = hashlib.sha256(binary).digest()
        stats, _, _ = decode_pair(binary, None)
        self.assertEqual(hashlib.sha256(binary).digest(), fingerprint)
        return stats

    def test_exact_stock_hashes(self):
        self.assertEqual(RANGED_STOCKS, {
            'slingshot': 0x80D34CE9,
            'boomerang': 0xA1D76D31,
            'bow': 0x938D56BF,
        })
        self.assertEqual(GERUDO_PASS_STOCK, 4273263979)
        self.assertEqual(MASK_STOCKS['mask_truth'], CHILD_TRADE_STOCKS[-1][2])
        self.assertEqual(CHILD_TRADE_STOCKS[0][2], 609840782)
        self.assertEqual(CHILD_TRADE_STOCKS[1], ('trade_wake_up_cucco', 'Wake-Up Cucco', 3066716307))
        self.assertEqual(ADULT_TRADE_STOCKS[1], ('trade_pocket_cucco', 'Pocket Cucco', 23455690))
        self.assertEqual(ADULT_TRADE_STOCKS[-1][2], 3738757331)

    def test_zero_ammo_still_owns_slingshot_and_boomerang(self):
        s = self.decoded({
            RANGED_STOCKS['slingshot']: 1, RANGED_STOCKS['boomerang']: 1,
            RANGED_STOCKS['bow']: 0, 0x5FADF7FC: 0,
        })
        self.assertIs(item_appearance(s, 'slingshot').owned, True)
        self.assertIs(item_appearance(s, 'slingshot').quantity, 0)
        self.assertIs(item_appearance(s, 'boomerang').owned, True)
        self.assertIs(item_appearance(s, 'bow').owned, False)
        self.assertIn('Deku Seeds: 0', item_hover_text(s, 'slingshot'))
        self.assertNotIn('Not Collected', item_hover_text(s, 'boomerang'))

    def test_ammo_does_not_create_slingshot_or_bow_ownership(self):
        s = self.decoded({
            RANGED_STOCKS['slingshot']: 0,
            RANGED_STOCKS['bow']: 0,
            0x5FADF7FC: 30, 0x518EC74B: 50,
        })
        self.assertIs(item_appearance(s, 'slingshot').owned, False)
        self.assertIs(item_appearance(s, 'bow').owned, False)
        self.assertIsNone(item_appearance(s, 'boomerang').owned)
        self.assertIn('Not Collected', item_hover_text(s, 'slingshot'))
        self.assertNotIn('boomerang', s['inventory_flags'])

    def test_invalid_stock_is_not_promoted(self):
        s = self.decoded({
            RANGED_STOCKS['boomerang']: 5, GERUDO_PASS_STOCK: -1,
        })
        self.assertNotIn('boomerang', s['inventory_flags'])
        self.assertNotIn('gerudo_pass', s['inventory_flags'])
        self.assertEqual(gerudo_pass_hover_text(s), 'Gerudo Pass\nNot Collected')

    def test_gerudo_pass_independent_of_shields(self):
        s = self.decoded({GERUDO_PASS_STOCK: 1})
        self.assertIs(s['inventory_flags']['gerudo_pass'], True)
        self.assertIn('Collected', gerudo_pass_hover_text(s))
        self.assertNotIn('shield_hylian', s['equipment_flags'])

    def test_child_latest_owned_and_truth_completion(self):
        s = self.decoded({
            CHILD_TRADE_STOCKS[0][2]: 0,
            CHILD_TRADE_STOCKS[4][2]: 1,
            CHILD_TRADE_STOCKS[-1][2]: 0,
        })
        child = trading_quest(s, 'child')
        self.assertEqual(child.label, 'Skull Mask')
        self.assertEqual(child.stage, 5)
        self.assertFalse(child.completed)
        self.assertFalse(s['child_trade_complete'])
        self.assertFalse(s['mask_shop_unlocked'])
        self.assertIn('earlier trades not independently tracked', trading_hover_text(s, 'child'))
        complete = self.decoded({CHILD_TRADE_STOCKS[-1][2]: 1})
        child = trading_quest(complete, 'child')
        self.assertTrue(child.completed)
        self.assertEqual(child.stage, 8)
        self.assertTrue(complete['mask_shop_unlocked'])
        self.assertIn('Regular masks unlocked', trading_hover_text(complete, 'child'))
        self.assertNotIn('mask_amiibo_0', complete['equipment_flags'])

    def test_mask_of_truth_unlocks_regular_masks_without_claiming_ownership(self):
        s = self.decoded({
            CHILD_TRADE_STOCKS[-1][2]: 1,
            MASK_STOCKS['mask_goron']: 0,
            MASK_STOCKS['mask_amiibo_0']: 0,
        })
        self.assertIs(item_appearance(s, 'mask_goron').owned, False)
        self.assertIs(s['mask_available_flags']['mask_goron'], True)
        self.assertIn('Unlocked at Mask Shop', item_hover_text(s, 'mask_goron'))
        self.assertIn('Not Collected', item_hover_text(s, 'mask_goron'))
        self.assertNotIn('mask_amiibo_0', s['mask_available_flags'])
        self.assertNotIn('Unlocked', item_hover_text(s, 'mask_amiibo_0'))

    def test_trading_order_matches_supplied_names(self):
        self.assertEqual([name for _, name, _ in CHILD_TRADE_STOCKS], [
            'Weird Egg', 'Wake-Up Cucco', "Zelda's Letter", 'Keaton Mask', 'Skull Mask',
            'Spooky Mask', 'Bunny Hood', 'Mask of Truth',
        ])
        self.assertEqual([name for _, name, _ in ADULT_TRADE_STOCKS], [
            'Pocket Egg', 'Pocket Cucco', 'Cojiro', 'Odd Mushroom', 'Odd Poultice',
            "Poacher's Saw", 'Broken Goron Sword', 'Prescription',
            'Eyeball Frog', 'Specialty Eye Drops', 'Biggoron Receipt',
        ])

    def test_adult_stage_order_and_consumed_item_caution(self):
        s = self.decoded({
            ADULT_TRADE_STOCKS[0][2]: 0,
            ADULT_TRADE_STOCKS[5][2]: 1,
            ADULT_TRADE_STOCKS[-1][2]: 0,
        })
        adult = trading_quest(s, 'adult')
        self.assertEqual(adult.label, "Poacher's Saw")
        self.assertEqual(adult.stage, 6)
        self.assertFalse(adult.completed)
        s = self.decoded({ADULT_TRADE_STOCKS[-1][2]: 1})
        self.assertEqual(trading_quest(s, 'adult').stage, 11)
        self.assertEqual(trading_quest(s, 'adult').label, 'Biggoron Receipt')
        self.assertFalse(trading_quest(s, 'adult').completed)
        self.assertEqual(trading_quest({}, 'adult').label, 'Not Collected')

    def test_hatched_cuccos_are_distinct_trade_stages_and_read_only(self):
        child_hash = CHILD_TRADE_STOCKS[1][2]
        adult_hash = ADULT_TRADE_STOCKS[1][2]
        s = self.decoded({child_hash:1, adult_hash:0})
        self.assertEqual(trading_quest(s, 'child').label, 'Wake-Up Cucco')
        self.assertEqual(trading_quest(s, 'child').stage, 2)
        self.assertEqual(trading_quest(s, 'child').total, 8)
        self.assertEqual(trading_quest(s, 'adult').label, 'Not Collected')
        self.assertIs(s['trade_flags']['trade_wake_up_cucco'], True)
        self.assertIs(s['trade_flags']['trade_pocket_cucco'], False)
        self.assertIn('Wake-Up Cucco', trading_hover_text(s, 'child'))
        s = self.decoded({child_hash:0, adult_hash:1})
        self.assertEqual(trading_quest(s, 'adult').label, 'Pocket Cucco')
        self.assertEqual(trading_quest(s, 'adult').stage, 2)
        self.assertEqual(trading_quest(s, 'adult').total, 11)
        self.assertEqual(trading_quest(s, 'child').label, 'Not Collected')
        self.assertIn('Pocket Cucco', trading_hover_text(s, 'adult'))
        s = self.decoded({child_hash:1, adult_hash:1})
        self.assertEqual(trading_quest(s, 'child').role, 'trade_wake_up_cucco')
        self.assertEqual(trading_quest(s, 'adult').role, 'trade_pocket_cucco')

    def test_compact_trade_chooses_later_known_item_across_both_eras(self):
        child = CHILD_TRADE_STOCKS[-1]  # Mask of Truth, explicitly complete
        adult = ADULT_TRADE_STOCKS[1]  # Pocket Cucco, current adult item
        s = self.decoded({child[2]: 1, adult[2]: 1})
        self.assertEqual(latest_trading_item(s).role, 'trade_pocket_cucco')
        self.assertEqual(trading_quest_overview_hover_text(s),
                         'Trading Quest\nPocket Cucco')
        # An adult zero or missing field is not progression evidence: show
        # latest confirmed child item instead of guessing an adult item.
        for adult_stock in (0, None):
            fields = {child[2]: 1}
            if adult_stock is not None:
                fields[adult[2]] = adult_stock
            s = self.decoded(fields)
            self.assertEqual(latest_trading_item(s).role, 'mask_truth')
            self.assertEqual(trading_quest_overview_hover_text(s),
                             'Trading Quest\nMask of Truth · Complete')
        s = self.decoded({})
        self.assertIsNone(latest_trading_item(s).role)
        self.assertEqual(trading_quest_overview_hover_text(s),
                         'Trading Quest\nNot Collected')

    def test_compact_pass_trade_icons_below_headings_no_era_breakdown(self):
        src = (ROOT/'zelda_summary_widget.py').read_text()
        tree = ast.parse(src)
        body = ast.unparse(next(n for n in ast.walk(tree)
                          if isinstance(n, ast.FunctionDef) and n.name == '_draw_expanded'))
        for expected in ('TRADING QUEST', 'PASS', 'latest_trading_item(raw)',
                         'trading_quest_overview_hover_text(raw)',
                         'gerudo_pass_hover_text(raw)',
                         'pass_x = equipment_x(4)',
                         'trading_x = equipment_x(6)',
                         "icon_tile('gerudo_pass', pass_x, 385, quest_icon_size, pass_flag",
                         "icon_tile(trade.role or 'trade_weird_egg', trading_x, 385, quest_icon_size"):
            self.assertIn(expected, body)
        # The label is centered over each icon, on the same grid as its shield.
        self.assertIn('quest_icon_size = 36', body)
        self.assertIn('pass_x + (quest_icon_size - pass_label_width) / 2', body)
        self.assertIn('trading_x + (quest_icon_size - trading_label_width) / 2', body)
        self.assertIn('trading_label_width = 100', body)
        # Mirror Shield center x=457; label must clear Bags divider x=510.
        self.assertLessEqual(25 + 6*69 + (36 + 100)/2, 510)
        self.assertIn("text_rect('TUNICS', 25, 361, 260", body)
        self.assertNotIn('Child:', body)
        self.assertNotIn('Adult:', body)
        self.assertNotIn('trading_hover_text(raw, era)', body)
        self.assertIn('MASKS_ICON_SIZE = 34', src)
        self.assertIn('DETAIL_CANVAS_WIDTH = 1120', src)
        launcher = (ROOT/'hookshot_qt.py').read_text()
        self.assertNotIn('self.settings_sections.addTab(self.pipeline_defaults_page', launcher)
        self.assertIn("prediction_form.addRow('Prediction budget', self.predict_seconds)", launcher)
        self.assertIn("self.tweaks_sections.addTab(self.tool_page, 'Shader / Pipeline Tools')", launcher)


if __name__ == '__main__':
    unittest.main()
