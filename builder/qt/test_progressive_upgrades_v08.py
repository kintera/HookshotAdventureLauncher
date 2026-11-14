"""Regression tests for the exact v0.8 7-column layout and progressive upgrades.

This module intentionally requires neither PyQt6 nor proprietary game textures.
"""
from pathlib import Path
import unittest

from adventure_panel_model import (
    ADVENTURE_UI_VERSION, INVENTORY_COLUMNS, INVENTORY_GRID_ROLES,
    INVENTORY_ROLES, EQUIPMENT_ROLES, OTHER_GEAR_ROLES,
    PROGRESSIVE_TIERS, item_appearance,
)

HERE = Path(__file__).resolve().parent


class ProgressiveUpgradeTests(unittest.TestCase):
    def test_version_stays_v08(self):
        self.assertEqual(ADVENTURE_UI_VERSION, 'v0.8')

    def test_exact_inventory_grid(self):
        self.assertEqual(INVENTORY_COLUMNS, 7)
        self.assertEqual(INVENTORY_GRID_ROLES[:7], (
            'slingshot', 'boomerang', 'deku_sticks', 'deku_nuts',
            'magic_bean', 'progressive_ocarina', 'lens_of_truth',
        ))
        self.assertEqual(INVENTORY_GRID_ROLES[7:14], (
            'bow', 'progressive_hookshot', 'hammer', 'bomb', 'bombchu',
            'business_scrub_seed', 'boots_iron',
        ))
        self.assertEqual(INVENTORY_GRID_ROLES[14:21], (
            'dins_fire', 'farores_wind', 'nayrus_love',
            'fire_arrow', 'ice_arrow', 'light_arrow', 'boots_hover',
        ))
        self.assertEqual(INVENTORY_GRID_ROLES[21:], (
            'bottle_slot_1', 'bottle_slot_2', 'bottle_slot_3',
            'bottle_slot_4', 'bottle_slot_5', 'agony_stone',
            'progressive_strength',
        ))
        self.assertNotIn('deku_seeds', INVENTORY_GRID_ROLES)
        self.assertNotIn('arrows', INVENTORY_GRID_ROLES)

    def test_upgrade_chains_ordered_from_base_to_max(self):
        self.assertEqual(PROGRESSIVE_TIERS, {
            'progressive_ocarina': ('ocarina_fairy', 'ocarina_time'),
            'progressive_hookshot': ('hookshot', 'longshot'),
            'progressive_strength': ('bracelet_goron', 'gauntlets_silver', 'gauntlets_gold'),
        })

    def test_base_and_each_strength_upgrade_replaces_predecessor(self):
        flags = {}
        for owned, expected in (
            ('bracelet_goron', 'bracelet_goron'),
            ('gauntlets_silver', 'gauntlets_silver'),
            ('gauntlets_gold', 'gauntlets_gold'),
        ):
            flags[owned] = True
            actual = item_appearance({'equipment_flags': dict(flags)}, 'progressive_strength')
            self.assertEqual(actual.role, expected)
            self.assertIs(actual.owned, True)

    def test_highest_owned_overrides_lower_unverified(self):
        stats = {'equipment_flags': {'gauntlets_gold': True, 'gauntlets_silver': False}}
        self.assertEqual(item_appearance(stats, 'progressive_strength').role, 'gauntlets_gold')

    def test_hookshot_and_ocarina_replace_at_upgrade(self):
        cases = [
            ('progressive_hookshot', 'hookshot', 'longshot'),
            ('progressive_ocarina', 'ocarina_fairy', 'ocarina_time'),
        ]
        for slot, base, upgrade in cases:
            self.assertEqual(item_appearance({'inventory_flags': {base: True}}, slot).role, base)
            item = item_appearance({'inventory_flags': {base: True, upgrade: True}}, slot)
            self.assertEqual(item.role, upgrade)
            self.assertTrue(item.owned)

    def test_unknown_progression_is_not_falsely_owned(self):
        for slot, tiers in PROGRESSIVE_TIERS.items():
            item = item_appearance({}, slot)
            self.assertEqual(item.role, tiers[0])
            self.assertIsNone(item.owned)

    def test_shields_swords_and_tunics_stay_individual(self):
        self.assertEqual(EQUIPMENT_ROLES, (
            'sword_kokiri', 'sword_master', 'sword_biggoron',
            'shield_deku', 'shield_hylian', 'shield_mirror',
        ))
        self.assertEqual(OTHER_GEAR_ROLES, ('tunic_kokiri', 'tunic_goron', 'tunic_zora'))
        source = (HERE / 'zelda_summary_widget.py').read_text()
        self.assertIn('equip_col = (0, 1, 2, 4, 5, 6)', source)
        self.assertNotIn("text_rect('BOTTLES'", source)
        self.assertIn('icon_tile(view.role,x,y,32,view.owned,quantity=view.quantity,', source)

    def test_existing_manifest_roles_reused(self):
        import json
        icons = json.loads((HERE / 'assets/game_icons/icon_manifest.json').read_text())['icons']
        for chain in PROGRESSIVE_TIERS.values():
            for item in chain:
                self.assertIn(item, icons)
        for slot in PROGRESSIVE_TIERS:
            self.assertNotIn(slot, icons, 'Progressive slots are layout aliases, not new textures')


if __name__ == '__main__':
    unittest.main()
