"""Adventure v0.8: exact Switch 2 Stone and medallion stock-field decoding."""
from __future__ import annotations

import hashlib
from pathlib import Path
import struct
import sys
import tempfile
import unittest
import zipfile

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
from save_reader import (
    MAGIC, HEADER_PROGRESS, FieldTable, SPIRITUAL_STONE_STOCKS,
    MEDALLION_REGISTRATIONS, decode_pair, decode_quest_ownership,
)
from zelda_summary_data import visual_from_stats


def synthetic_save(values, selected_stone=None, progress_id=None):
    payload = bytearray(HEADER_PROGRESS + 512)
    struct.pack_into('<III', payload, 0, *MAGIC, HEADER_PROGRESS)
    data = dict(values)
    end = HEADER_PROGRESS
    # The selected item and chapter are deliberately separate from stock.
    for hash_key, text in ((0x0F4E78BC, selected_stone), (0x874C7ED4, progress_id)):
        if text is not None:
            data[hash_key] = end
            payload[end:end+64] = text.encode('utf-8').ljust(64, b'\0')
            end += 64
    for i, (field, value) in enumerate(data.items()):
        struct.pack_into('<II', payload, 40+i*8, field, value)
    return bytes(payload)


class DirectQuestFieldsTests(unittest.TestCase):
    def test_stone_ids_match_catalog_entries(self):
        self.assertEqual(SPIRITUAL_STONE_STOCKS, {
            'Kokiri Emerald': 3419530636,
            'Goron Ruby': 2104077639,
            'Zora Sapphire': 3101933381,
        })

    def test_medallion_registration_tuples_match_editor(self):
        self.assertEqual(MEDALLION_REGISTRATIONS, {
            'Fire': (1558336678, 272240071, 3573259677),
            'Forest': (1764579275, 4242069075, 1094569096),
            'Light': (593264026, 2418459015, 3385396654),
            'Shadow': (541925475, 316029638, 360829024),
            'Spirit': (209346512, 3798589240, 1739129708),
            'Water': (22128271, 3479496576, 1139493017),
        })

    def test_direct_owned_and_unowned_values(self):
        values = {h: 1 for h in SPIRITUAL_STONE_STOCKS.values()}
        values.update({stock: 0 for stock, _, _ in MEDALLION_REGISTRATIONS.values()})
        stats, _, _ = decode_pair(synthetic_save(values), None)
        self.assertEqual(stats['stone_flags'], dict.fromkeys(SPIRITUAL_STONE_STOCKS, True))
        self.assertEqual(stats['medallion_flags'], dict.fromkeys(MEDALLION_REGISTRATIONS, False))
        self.assertEqual(visual_from_stats(stats).stones, (True, True, True))
        self.assertEqual(visual_from_stats(stats).medallions, (False,) * 6)
        self.assertEqual(stats['stone_sources']['Kokiri Emerald'], 'item_stock:0xCBD1E58C')
        self.assertEqual(stats['medallion_sources']['Forest'], 'item_stock:0x692D57CB')

    def test_missing_and_unexpected_values_stay_unknown(self):
        stats, _, _ = decode_pair(synthetic_save({
            SPIRITUAL_STONE_STOCKS['Kokiri Emerald']: 1,
            SPIRITUAL_STONE_STOCKS['Goron Ruby']: 3,
            MEDALLION_REGISTRATIONS['Fire'][0]: 5,
        }), None)
        self.assertEqual(stats['stone_flags'], {'Kokiri Emerald': True})
        self.assertEqual(stats['medallion_flags'], {})
        self.assertEqual(visual_from_stats(stats).stones, (True, None, None))
        self.assertEqual(visual_from_stats(stats).medallions, (None,) * 6)

    def test_story_and_selected_stone_never_drive_ownership(self):
        stats, _, _ = decode_pair(synthetic_save({}, selected_stone='Item_Stone_Water',
                                                 progress_id='DoorOfTime'), None)
        self.assertEqual(stats['stone_flags'], {})
        self.assertEqual(stats['medallion_flags'], {})
        older_setting, _, _ = decode_pair(synthetic_save({}, selected_stone='Item_Stone_Water',
                                                         progress_id='DoorOfTime'), None,
                                          vanilla_story_assumptions=True)
        self.assertEqual(older_setting['stone_flags'], {})

    def test_acquisition_flags_are_not_stock(self):
        stock, acquired, related = MEDALLION_REGISTRATIONS['Water']
        # Acquisition is historical information and must not be interpreted as current stock.
        stats, _, _ = decode_pair(synthetic_save({stock: 0, acquired: 1, related: 1}), None)
        self.assertEqual(stats['medallion_flags']['Water'], False)
        stats, _, _ = decode_pair(synthetic_save({stock: 1, acquired: 0, related: 0}), None)
        self.assertEqual(stats['medallion_flags']['Water'], True)

    def test_independent_medallion_ownership(self):
        stocks = {row[0]: int(i % 2 == 0) for i, row in enumerate(MEDALLION_REGISTRATIONS.values())}
        stats, _, _ = decode_pair(synthetic_save(stocks), None)
        expected = {name: i % 2 == 0 for i, name in enumerate(MEDALLION_REGISTRATIONS)}
        self.assertEqual(stats['medallion_flags'], expected)

    def test_real_uploaded_saves_are_read_only(self):
        paths = [Path('/mnt/data/save.zip'), HERE.parents[3] / 'save.zip']
        archive = next((path for path in paths if path.is_file()), None)
        if archive is None:
            self.skipTest('External save.zip fixture not present in release')
        before = hashlib.sha256(archive.read_bytes()).hexdigest()
        with zipfile.ZipFile(archive) as z:
            names = [n for n in z.namelist() if n.endswith('/progress.sav')]
            self.assertEqual(len(names), 6)
            for name in names:
                stats, _, _ = decode_pair(z.read(name), None)
                self.assertEqual(stats['stone_flags'], dict.fromkeys(SPIRITUAL_STONE_STOCKS, True))
                self.assertEqual(stats['medallion_flags'], dict.fromkeys(MEDALLION_REGISTRATIONS, False))
        self.assertEqual(hashlib.sha256(archive.read_bytes()).hexdigest(), before)


if __name__ == '__main__':
    unittest.main()
