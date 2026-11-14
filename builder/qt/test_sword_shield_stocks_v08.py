"""Switch 2 sword/shield StockNum decoding, using synthetic read-only saves."""
from __future__ import annotations

import hashlib
import unittest

from adventure_panel_model import EQUIPMENT_ROLES, item_appearance
from save_reader import (
    EQUIPMENT, HEADER_PROGRESS, SHIELD_STOCKS, SWORD_STOCKS,
    decode_pair, decode_weapon_ownership, FieldTable,
)
from test_save_reader_v06 import make_save


class WeaponStocksTests(unittest.TestCase):
    def test_exact_sword_and_shield_hashes(self):
        self.assertEqual(SWORD_STOCKS, {
            'sword_kokiri': 4004812217,
            'sword_master': 4056175252,
            'sword_biggoron': 2964579225,
        })
        self.assertEqual(SHIELD_STOCKS, {
            'shield_deku': 2091274019,
            'shield_hylian': 146734394,
            'shield_mirror': 4119727663,
        })
        self.assertEqual(EQUIPMENT['shield'], 3997577781)
        self.assertEqual(set(EQUIPMENT_ROLES), set(SWORD_STOCKS) | set(SHIELD_STOCKS))
        self.assertNotIn('sword_giant', EQUIPMENT_ROLES)
        self.assertNotIn('sword_broken_giant', EQUIPMENT_ROLES)

    def test_current_stock_ownership_and_sources(self):
        data = make_save(HEADER_PROGRESS, {
            SWORD_STOCKS['sword_kokiri']: 0,
            SWORD_STOCKS['sword_master']: 1,
            SWORD_STOCKS['sword_biggoron']: 1,
            SHIELD_STOCKS['shield_deku']: 0,
            SHIELD_STOCKS['shield_hylian']: 2,
            SHIELD_STOCKS['shield_mirror']: 1,
        })
        original = hashlib.sha256(data).digest()
        stats, _, _ = decode_pair(data, None)
        self.assertEqual(hashlib.sha256(data).digest(), original)
        self.assertEqual({role: stats['equipment_flags'][role] for role in EQUIPMENT_ROLES}, {
            'sword_kokiri': False,
            'sword_master': True,
            'sword_biggoron': True,
            'shield_deku': False,
            'shield_hylian': True,
            'shield_mirror': True,
        })
        for role, key in {**SWORD_STOCKS, **SHIELD_STOCKS}.items():
            self.assertEqual(stats['equipment_sources'][role], f'item_stock:0x{key:08X}')
            self.assertIs(item_appearance(stats, role).owned, stats['equipment_flags'][role])

    def test_unknown_absent_negative_and_implausible_stock(self):
        data = make_save(HEADER_PROGRESS, {
            SWORD_STOCKS['sword_kokiri']: -1,
            SWORD_STOCKS['sword_biggoron']: 1000,
            SHIELD_STOCKS['shield_hylian']: 99,
        })
        stats, _, _ = decode_pair(data, None)
        for role in ('sword_kokiri', 'sword_biggoron', 'shield_deku',
                     'shield_mirror', 'sword_master'):
            self.assertNotIn(role, stats['equipment_flags'])
            self.assertNotIn(role, stats['equipment_sources'])
            self.assertIsNone(item_appearance(stats, role).owned)
        self.assertIs(item_appearance(stats, 'shield_hylian').owned, True)

    def test_equip_shield_field_is_not_new_or_a_stock_field(self):
        data = make_save(HEADER_PROGRESS, {
            SHIELD_STOCKS['shield_hylian']: 1,
            SHIELD_STOCKS['shield_mirror']: 0,
            SWORD_STOCKS['sword_master']: 1,
        }, {
            EQUIPMENT['shield']: 'Weapon_Shield_Hylian',
            EQUIPMENT['sword']: 'Weapon_Sword_Master',
        })
        stats, _, _ = decode_pair(data, None)
        self.assertEqual(stats['equipped_shield'], 'Weapon_Shield_Hylian')
        self.assertEqual(stats['equipped_sword'], 'Weapon_Sword_Master')
        self.assertTrue(item_appearance(stats, 'shield_hylian').equipped)
        self.assertTrue(item_appearance(stats, 'sword_master').equipped)
        self.assertFalse(item_appearance(stats, 'shield_mirror').equipped)
        self.assertIs(item_appearance(stats, 'shield_mirror').owned, False)

    def test_equipped_hylian_shield_overrides_zero_pouch_stock(self):
        data = make_save(HEADER_PROGRESS, {
            SHIELD_STOCKS['shield_hylian']: 0,
            SHIELD_STOCKS['shield_deku']: 0,
        }, {EQUIPMENT['shield']: 'Weapon_Shield_Hylian'})
        stats, _, _ = decode_pair(data, None)
        view = item_appearance(stats, 'shield_hylian')
        self.assertIs(view.owned, True)
        self.assertTrue(view.equipped)
        self.assertIs(stats['equipment_flags']['shield_hylian'], True)
        self.assertEqual(stats['equipment_sources']['shield_hylian'],
                         f"equipped_item:0x{EQUIPMENT['shield']:08X}")
        self.assertIs(item_appearance(stats, 'shield_deku').owned, False)
        # Legacy saves without a known stock field still retain the same
        # equipped-item fallback; only the named shield is recognized.
        legacy_stats, _, _ = decode_pair(make_save(HEADER_PROGRESS, {},
            {EQUIPMENT['shield']: 'Weapon_Shield_Hylian'}), None)
        legacy = item_appearance(legacy_stats, 'shield_hylian')
        self.assertIs(legacy.owned, True)
        self.assertTrue(legacy.equipped)

    def test_both_eras_equipped_fields_are_checked(self):
        data = make_save(HEADER_PROGRESS, {
            SHIELD_STOCKS['shield_hylian']: 0,
            SWORD_STOCKS['sword_master']: 0,
        }, {
            EQUIPMENT['second_shield']: 'Armor_Shield_Hylia',
            EQUIPMENT['second_sword']: 'Weapon_Sword_Master',
        })
        stats, _, _ = decode_pair(data, None)
        self.assertTrue(item_appearance(stats, 'shield_hylian').owned)
        self.assertTrue(item_appearance(stats, 'shield_hylian').equipped)
        self.assertTrue(item_appearance(stats, 'sword_master').owned)
        self.assertTrue(item_appearance(stats, 'sword_master').equipped)
        self.assertIs(stats['equipment_flags']['shield_hylian'], True)
        self.assertIs(stats['equipment_flags']['sword_master'], True)

    def test_no_item_claim_from_other_shield_or_invalid_equipped_value(self):
        data = make_save(HEADER_PROGRESS, {
            SHIELD_STOCKS['shield_hylian']: 0,
            SHIELD_STOCKS['shield_mirror']: 0,
        }, {
            EQUIPMENT['shield']: 'Weapon_Shield_Mirror',
            EQUIPMENT['second_sword']: 'Weapon_Shield_Hylian',
        })
        stats, _, _ = decode_pair(data, None)
        self.assertIs(item_appearance(stats, 'shield_mirror').owned, True)
        self.assertIs(item_appearance(stats, 'shield_hylian').owned, False)
        self.assertIs(stats['equipment_flags']['shield_hylian'], False)


if __name__ == '__main__':
    unittest.main()
