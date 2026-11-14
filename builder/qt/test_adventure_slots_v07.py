"""v0.7 launcher shortcuts and expanded, read-only save parsing regressions."""
from pathlib import Path
import hashlib
import struct
import tempfile
import unittest
from unittest.mock import patch

from profile_store import ProfileStore, register_installed_saves
from save_reader import (decode_pair, read_adventure_slots, PROGRESS, SUPPLIES,
                         CAPACITIES, WALLET, DUNGEON_KEYS, BOTTLE_STOCK_HASHES,
                         EQUIPMENT, HEADER_PROGRESS, HEADER_CAPTION)
from test_save_reader_v06 import make_save


class ExpandedReader(unittest.TestCase):
    def test_extended_save_fields_and_legacy_magic(self):
        values = {
            PROGRESS['rupees']: 120,
            PROGRESS['hearts_max']: 48,
            PROGRESS['hearts_current']: 42,
            PROGRESS['magic_max']: 24,
            PROGRESS['magic_current']: 12,
            PROGRESS['skulltulas']: 33,
            WALLET['capacity']: 500,
            WALLET['medium']: 1,
            SUPPLIES['heart_pieces'][0]: 2,
            SUPPLIES['bombs'][0]: 17,
            SUPPLIES['arrows'][0]: 12,
            SUPPLIES['deku_seeds'][0]: 27,
            CAPACITIES['quiver_capacity'][0]: 30,
            DUNGEON_KEYS['forest'][0]: 2,
            **{hash_value: (1 if i < 2 else 0) for i, hash_value in enumerate(BOTTLE_STOCK_HASHES)},
        }
        strings = {WALLET['type']: 'Item_Wallet_Medium',
                   EQUIPMENT['sword']: 'Weapon_Sword_Master',
                   EQUIPMENT['shield']: 'Armor_Shield_Hylia'}
        stats, timestamp, image = decode_pair(make_save(HEADER_PROGRESS, values, strings), None)
        self.assertEqual(stats['hearts'], 12)
        self.assertEqual(stats['current_hearts'], 10.5)
        self.assertEqual(stats['magic'], 12)
        self.assertEqual(stats['max_magic'], 24)
        self.assertEqual(stats['wallet'], 'Adult')
        self.assertEqual(stats['wallet_capacity'], 500)
        self.assertEqual(stats['heart_pieces'], 2)
        self.assertEqual(stats['bombs'], 17)
        self.assertEqual(stats['quiver_capacity'], 30)
        self.assertEqual(stats['dungeon_keys']['forest'], 2)
        self.assertEqual(stats['bottles'], 2)
        self.assertEqual(stats['equipped_sword'], 'Weapon_Sword_Master')
        self.assertEqual(stats['equipped_shield'], 'Armor_Shield_Hylia')
        self.assertNotIn('medallions', stats)  # not yet verified; do not invent values
        self.assertNotIn('playtime_minutes', stats)
        self.assertIsNone(timestamp)
        self.assertIsNone(image)

    def test_only_verified_present_fields_are_used(self):
        stats, _, _ = decode_pair(make_save(HEADER_PROGRESS, {PROGRESS['rupees']: 50}), None)
        self.assertEqual(stats['rupees'], 50)
        self.assertIsNone(stats['wallet_capacity'])
        self.assertIsNone(stats['bottles'])
        self.assertIsNone(stats['heart_pieces'])

    def test_synthetic_save_bytes_not_modified(self):
        with tempfile.TemporaryDirectory() as d:
            loc = Path(d)/'save'/'account'/'slot_00'
            loc.mkdir(parents=True)
            save = make_save(HEADER_PROGRESS, {PROGRESS['rupees']: 55})
            (loc/'progress.sav').write_bytes(save)
            before = hashlib.sha256(save).digest()
            self.assertEqual(read_adventure_slots(Path(d))[0].statistics['rupees'],55)
            self.assertEqual(hashlib.sha256((loc/'progress.sav').read_bytes()).digest(), before)


class ThreeBoxProfiles(unittest.TestCase):
    def setUp(self):
        self.t = tempfile.TemporaryDirectory()
        self.addCleanup(self.t.cleanup)
        self.dir = Path(self.t.name)
        self.store = ProfileStore(self.dir/'registry')

    def test_empty_shortcuts(self):
        self.assertEqual(self.store.launcher_slots(), [None, None, None])
        self.assertEqual(self.store.list_profiles(), [])

    def test_created_profiles_fill_boxes_without_relocating(self):
        created = [self.store.create(f'Adventure {i}') for i in range(4)]
        self.assertEqual([p['id'] for p in self.store.launcher_slots()], [p['id'] for p in created[:3]])
        self.assertEqual(len(self.store.list_profiles()), 4)
        self.store.pin_launcher_slot(created[3]['id'], 1)
        self.assertEqual(self.store.launcher_slots()[1]['id'], created[3]['id'])
        self.assertEqual(len(self.store.list_profiles()), 4)
        self.assertEqual(len(self.store.list_profiles()), 4)

    def test_pins_survive_restart_and_never_duplicate(self):
        p = self.store.create('Link')
        q = self.store.create('Ruto')
        self.store.pin_launcher_slot(p['id'], 2)
        self.store.pin_launcher_slot(p['id'], 0)
        restored = ProfileStore(self.dir/'registry')
        ids = [x['id'] if x else None for x in restored.launcher_slots()]
        self.assertEqual(ids.count(p['id']), 1)
        self.assertEqual(ids[0], p['id'])
        self.assertIn(q['id'], ids)

    def test_existing_save_auto_pins_and_names_link(self):
        game = self.dir/'game'
        game.mkdir()
        (game/'hookshot').touch()
        (game/'image.bin').touch()
        self.store.set_game_dir(game)
        xdg = self.dir/'xdg'/'hookshot'
        original = xdg/'save'/'userabc'/'slot_00'/'progress.sav'
        original.parent.mkdir(parents=True)
        original.write_bytes(b'original save contents')
        with patch('profile_store.default_hookshot_user', return_value=xdg):
            registered = register_installed_saves(self.store, game)
            repeated = register_installed_saves(self.store, game)
        self.assertEqual(len(registered), 1)
        self.assertEqual(registered[0]['name'], 'Link')
        self.assertEqual(len(repeated), 1)
        self.assertEqual(self.store.launcher_slots()[0]['id'], registered[0]['id'])
        self.assertEqual(original.read_bytes(), b'original save contents')

    def test_empty_default_does_not_take_new_adventure_box(self):
        empty = self.store.register_legacy(self.dir/'xdg', initialize_empty=True)
        self.assertIsNotNone(empty)
        self.assertEqual(self.store.launcher_slots(), [None, None, None])
        fresh = self.store.create('New adventure')
        self.assertEqual(self.store.launcher_slots()[0]['id'], fresh['id'])

if __name__ == '__main__':
    unittest.main()
