"""Read-only capacity numbers shown in green inside the 3D Bags rail."""
from __future__ import annotations

import ast
import json
from pathlib import Path
import unittest

from adventure_panel_model import BAG_UPGRADES, bag_upgrade, bag_hover_text

HERE = Path(__file__).resolve().parent


class BagMaxCapacityBadgeTests(unittest.TestCase):
    def test_each_capacity_field_uses_tier_max_not_ammo_stock(self):
        for bag, _name, field, tiers, _item in BAG_UPGRADES:
            for tier, maximum in enumerate(tiers, start=1):
                stats = {field: maximum, 'deku_sticks': 1, 'deku_nuts': 1,
                         'deku_seeds': 1, 'arrows': 1, 'bombs': 1}
                upgrade = bag_upgrade(stats, bag)
                self.assertEqual(upgrade.tier, tier)
                self.assertEqual(upgrade.capacity, maximum)
                self.assertEqual(upgrade.icon_role, f'bag_{bag}_{tier}')
                self.assertIn(f'Holds {maximum}', bag_hover_text(stats, bag))

    def test_unknown_absent_and_invalid_tiers_never_claim_a_maximum(self):
        for bag, _name, field, tiers, _item in BAG_UPGRADES:
            self.assertIsNone(bag_upgrade({}, bag).capacity)
            self.assertIsNone(bag_upgrade({field: -1}, bag).capacity)
            self.assertIsNone(bag_upgrade({field: tiers[1] + 1}, bag).capacity)
            self.assertEqual(bag_upgrade({field: 0}, bag).tier, 0)
            self.assertEqual(bag_upgrade({field: 0}, bag).capacity, 0)

    def test_icon_badges_are_green_over_game_art_and_only_when_owned(self):
        source = (HERE/'zelda_summary_widget.py').read_text()
        ast.parse(source)
        self.assertIn('numeric_badge(525, y, 35, upgrade.capacity', source)
        self.assertIn("'#80f5a0'", source)
        self.assertIn('upgrade.tier > 0 and upgrade.capacity is not None', source)
        self.assertIn('numeric_badge(x, y, size, quantity', source)
        self.assertIn('AMMO_BADGE_ALPHA = 86', source)
        self.assertIn("f'{upgrade.level} · Level {upgrade.tier}/3'", source)

    def test_uploaded_manifest_keeps_all_fifteen_mapped_bags_and_gold_token(self):
        icons = json.loads((HERE/'assets/game_icons/icon_manifest.json').read_text())['icons']
        self.assertNotIn('skulltula', icons)
        self.assertEqual(icons['gold_skulltula']['texture'], 'Item_Token_Gold')
        self.assertEqual(icons['mask_skull']['texture'], 'Armor_Child_Mask_Skull')
        self.assertEqual(icons['ocarina']['texture'], 'Item_Ocarina_Fairy')
        self.assertEqual(len([x for x in icons if x.startswith('bag_')]), 15)
        for bag, _name, _field, _tiers, _item in BAG_UPGRADES:
            for tier in (1, 2, 3):
                mapping = icons[f'bag_{bag}_{tier}']
                self.assertTrue(mapping['verified'])
                self.assertIsNotNone(mapping['archive'])
                self.assertIsNotNone(mapping['texture'])


if __name__ == '__main__':
    unittest.main()
