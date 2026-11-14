"""Read-only mask and Child Link equipped-tunic regression tests, Adventure UI v0.8."""
import unittest

from adventure_panel_model import MASK_ROLES, item_appearance
from save_reader import (
    HEADER_PROGRESS, PROGRESS, MASK_STOCKS, TUNIC_STOCKS,
    decode_pair, derived_child_tunic,
)
from test_save_reader_v06 import make_save


class MasksAndTunicsTests(unittest.TestCase):
    def test_ten_catalog_masks_are_exact_and_unique(self):
        self.assertEqual(set(MASK_ROLES), set(MASK_STOCKS))
        self.assertEqual(len(set(MASK_STOCKS.values())), 10)
        self.assertEqual(MASK_STOCKS['mask_bunny'], 2120249395)
        self.assertEqual(MASK_STOCKS['mask_amiibo_0'], 4125509594)
        self.assertEqual(MASK_STOCKS['mask_amiibo_1'], 1215060388)

    def test_mask_owned_unowned_unknown_and_invalid(self):
        raw = make_save(HEADER_PROGRESS, {
            MASK_STOCKS['mask_bunny']: 1,
            MASK_STOCKS['mask_zora']: 0,
            MASK_STOCKS['mask_skull']: 4,
        })
        stats, _, _ = decode_pair(raw, None)
        self.assertIs(item_appearance(stats, 'mask_bunny').owned, True)
        self.assertIs(item_appearance(stats, 'mask_zora').owned, False)
        self.assertIsNone(item_appearance(stats, 'mask_skull').owned)
        self.assertIsNone(item_appearance(stats, 'mask_keaton').owned)
        self.assertEqual(stats['equipment_sources']['mask_bunny'], 'item_stock:0x7E607033')
        self.assertNotIn('mask_skull', stats['equipment_sources'])
        self.assertNotIn('mask_keaton', stats['equipment_sources'])

    def test_child_kokiri_is_selected_without_marking_every_tunic(self):
        raw = make_save(HEADER_PROGRESS, {
            TUNIC_STOCKS['tunic_kokiri']: 1,
            TUNIC_STOCKS['tunic_goron']: 0,
            TUNIC_STOCKS['tunic_zora']: 0,
        }, {PROGRESS['location']: 'ZoraRiver_Child'})
        stats, _, _ = decode_pair(raw, None)
        self.assertEqual(stats['equipped_tunic'], 'Obj_Armor_Child_Normal')
        self.assertEqual(stats['equipment_sources']['equipped_tunic'], 'child_form_only_tunic')
        self.assertTrue(item_appearance(stats, 'tunic_kokiri').equipped)
        self.assertFalse(item_appearance(stats, 'tunic_goron').equipped)
        self.assertFalse(item_appearance(stats, 'tunic_zora').equipped)
        self.assertFalse(item_appearance(stats, 'tunic_goron').owned)

    def test_adult_and_unknown_tunic_selection_not_inferred(self):
        for location in ('ZoraRiver_Adult', 'Kokiri Forest', None):
            self.assertIsNone(derived_child_tunic(location, {'tunic_kokiri': True}))
        raw = make_save(HEADER_PROGRESS, {
            TUNIC_STOCKS['tunic_kokiri']: 1,
            TUNIC_STOCKS['tunic_goron']: 1,
        }, {PROGRESS['location']: 'ZoraRiver_Adult'})
        stats, _, _ = decode_pair(raw, None)
        self.assertIsNone(stats['equipped_tunic'])
        self.assertTrue(item_appearance(stats, 'tunic_kokiri').owned)
        self.assertFalse(item_appearance(stats, 'tunic_kokiri').equipped)
        self.assertTrue(item_appearance(stats, 'tunic_goron').owned)
        self.assertFalse(item_appearance(stats, 'tunic_goron').equipped)

    def test_all_fields_in_bare_save_stay_unknown(self):
        stats, _, _ = decode_pair(make_save(HEADER_PROGRESS, {}), None)
        self.assertTrue(all(item_appearance(stats, role).owned is None for role in MASK_ROLES))
        self.assertIsNone(stats['equipped_tunic'])


if __name__ == '__main__':
    unittest.main()
