"""Separate non-Qt inventory layout contracts for Adventure UI v0.8."""
import json
from pathlib import Path
import unittest
from adventure_panel_model import (
    INVENTORY_COLUMNS, INVENTORY_ROWS, INVENTORY_ROLES,
    INVENTORY_GRID_ROLES, PROGRESSIVE_TIERS, item_appearance,
)
HERE = Path(__file__).resolve().parent

class InventoryGridV08Tests(unittest.TestCase):
    def test_no_redundant_ordinary_ammo_tiles(self):
        self.assertNotIn('arrows', INVENTORY_GRID_ROLES)
        self.assertNotIn('deku_seeds', INVENTORY_GRID_ROLES)
        self.assertIn('fire_arrow', INVENTORY_GRID_ROLES)
        self.assertIn('ice_arrow', INVENTORY_GRID_ROLES)
        self.assertIn('light_arrow', INVENTORY_GRID_ROLES)

    def test_ammo_counts_are_on_weapon_icons(self):
        stats = {'arrows': 0, 'deku_seeds': 41,
                 'inventory_flags': {'slingshot': True, 'bow': False}}
        self.assertEqual(item_appearance(stats, 'slingshot').quantity, 41)
        self.assertEqual(item_appearance(stats, 'bow').quantity, 0)
        self.assertFalse(item_appearance(stats, 'bow').owned)

    def test_seven_column_layout_places_stone_after_five_bottles(self):
        self.assertEqual((INVENTORY_COLUMNS, INVENTORY_ROWS), (7,4))
        self.assertEqual(len(INVENTORY_ROLES),21)
        self.assertEqual(INVENTORY_GRID_ROLES[21:28], (
            'bottle_slot_1', 'bottle_slot_2', 'bottle_slot_3',
            'bottle_slot_4', 'bottle_slot_5', 'agony_stone',
            'progressive_strength'))
        self.assertEqual((26 // INVENTORY_COLUMNS, 26 % INVENTORY_COLUMNS), (3,5))

    def test_agony_uses_inventory_flags_not_equipment_flags(self):
        self.assertTrue(item_appearance({'inventory_flags': {'agony_stone': True}}, 'agony_stone').owned)
        self.assertIsNone(item_appearance({'equipment_flags': {'agony_stone': True}}, 'agony_stone').owned)

    def test_manifest_can_map_missing_supply_art(self):
        icons = json.loads((HERE/'assets/game_icons/icon_manifest.json').read_text())['icons']
        self.assertGreaterEqual(len(icons), 88)
        self.assertTrue(icons['agony_stone']['verified'])
        self.assertIn('business_scrub_seed', icons)
        for role in ('business_scrub_seed','deku_sticks','deku_nuts','deku_seeds'):
            self.assertIn(role, icons)
            if icons[role].get('verified'):
                self.assertTrue(icons[role].get('archive'))
                self.assertTrue(icons[role].get('texture'))

    def test_scrub_seed_has_no_caption_and_echo_gem_uses_existing_asset_key(self):
        render = (HERE/'zelda_summary_widget.py').read_text()
        self.assertNotIn("text_rect('SCRUB SEED'", render)
        self.assertNotIn("text_rect('AGONY'", render)
        self.assertNotIn("text_rect('ECHO GEM'", render)
        self.assertEqual(INVENTORY_GRID_ROLES[-2], "agony_stone")
        self.assertIn("'business_scrub_seed'", render)
        self.assertEqual(INVENTORY_GRID_ROLES[-2], 'agony_stone')

    def test_requested_seven_column_row_order(self):
        self.assertEqual(INVENTORY_GRID_ROLES[:7], (
            'slingshot', 'boomerang', 'deku_sticks', 'deku_nuts',
            'magic_bean', 'progressive_ocarina', 'lens_of_truth'))
        self.assertEqual(INVENTORY_GRID_ROLES[7:14], (
            'bow', 'progressive_hookshot', 'hammer', 'bomb', 'bombchu',
            'business_scrub_seed', 'boots_iron'))
        self.assertEqual(INVENTORY_GRID_ROLES[14:21], (
            'dins_fire', 'farores_wind', 'nayrus_love',
            'fire_arrow', 'ice_arrow', 'light_arrow', 'boots_hover'))

    def test_progressive_replaces_lower_tier_with_highest_owned(self):
        self.assertEqual(PROGRESSIVE_TIERS['progressive_strength'],
                         ('bracelet_goron', 'gauntlets_silver', 'gauntlets_gold'))
        self.assertEqual(item_appearance({'equipment_flags': {
            'bracelet_goron': True}}, 'progressive_strength').role, 'bracelet_goron')
        self.assertEqual(item_appearance({'equipment_flags': {
            'bracelet_goron': True, 'gauntlets_silver': True}},
            'progressive_strength').role, 'gauntlets_silver')
        self.assertEqual(item_appearance({'equipment_flags': {
            'bracelet_goron': True, 'gauntlets_silver': True, 'gauntlets_gold': True}},
            'progressive_strength').role, 'gauntlets_gold')
        self.assertEqual(item_appearance({'inventory_flags': {
            'ocarina_fairy': True, 'ocarina_time': True}},
            'progressive_ocarina').role, 'ocarina_time')
        self.assertEqual(item_appearance({'inventory_flags': {
            'hookshot': True, 'longshot': True}},
            'progressive_hookshot').role, 'longshot')
        self.assertIsNone(item_appearance({}, 'progressive_strength').owned)
        self.assertEqual(item_appearance({}, 'progressive_strength').role, 'bracelet_goron')

    def test_no_left_side_bottle_total_and_three_equipment_rows(self):
        render = (HERE/'zelda_summary_widget.py').read_text()
        self.assertNotIn("text_rect('BOTTLES'", render)
        self.assertIn("text_rect('SWORDS'", render)
        self.assertIn("text_rect('SHIELDS'", render)
        self.assertIn("text_rect('TUNICS'", render)

    def test_render_source_uses_seven_columns(self):
        source = (HERE/'zelda_summary_widget.py').read_text()
        self.assertIn('i % INVENTORY_COLUMNS', source)
        self.assertIn('i // INVENTORY_COLUMNS', source)
        self.assertIn("role.startswith('bottle_slot_')", source)
        self.assertIn("icon_tile(view.role,x,y,32,view.owned,quantity=view.quantity,", source)

if __name__ == '__main__':
    unittest.main()
