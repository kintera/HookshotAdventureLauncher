"""Presentation labels must not pretend a missing field has been decoded."""
from __future__ import annotations

import unittest

from adventure_panel_model import item_appearance, item_hover_text


class EquipmentLabelTests(unittest.TestCase):
    def test_unacquired_or_missing_weapon_says_not_collected(self):
        for value in (None, False):
            stats = {'equipment_flags': {'shield_hylian': value}}
            self.assertEqual(item_hover_text(stats, 'shield_hylian'),
                             'Hylian Shield\nNot Collected')
            self.assertIs(item_appearance(stats, 'shield_hylian').owned, value)

    def test_equipped_item_proves_ownership_without_equipped_label(self):
        stats = {'equipment_flags': {'shield_hylian': False},
                 'second_equipped_shield': 'Armor_Shield_Hylia'}
        self.assertEqual(item_hover_text(stats, 'shield_hylian'),
                         'Hylian Shield')
        self.assertIs(item_appearance(stats, 'shield_hylian').owned, True)

    def test_inventory_unverified_shows_not_collected_without_changing_model(self):
        self.assertEqual(item_hover_text({}, 'slingshot'),
                         'Fairy Slingshot\nNot Collected')
        self.assertIsNone(item_appearance({}, 'slingshot').owned)


if __name__ == '__main__':
    unittest.main()
