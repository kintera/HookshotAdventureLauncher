"""Regression checks for the --user profile integration with v2.0.14's build layout."""
import json
import os
import sys
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

from profile_store import ProfileStore, ProfileError, register_installed_saves
from save_info import read_game_slots
from build_flow import inspect_game


class IntegrationTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.store = ProfileStore(self.root / 'manager')
        self.game = self.root / 'installed'
        self.game.mkdir()
        (self.game / 'image.bin').write_bytes(b'not a game: TEST-ONLY')

    def install(self, in_bin=False):
        executable = self.game / 'bin' / 'hookshot' if in_bin else self.game / 'hookshot'
        executable.parent.mkdir(parents=True, exist_ok=True)
        executable.write_bytes(b'dummy executable')
        return executable

    def test_root_game_executable_supported(self):
        exe = self.install(False)
        self.store.set_game_dir(self.game)
        p = self.store.create('My game')
        args, cwd = self.store.game_command(p['id'])
        self.assertEqual(args, [str(exe), '--user', p['user_dir']])
        self.assertEqual(cwd, self.game.resolve())

    def test_bin_executable_supported(self):
        exe = self.install(True)
        (self.game / 'romfs').mkdir()
        self.store.set_game_dir(self.game)
        p = self.store.create('Second game')
        args, cwd = self.store.game_command(p['id'])
        self.assertEqual(args[0], str(exe))
        self.assertEqual(args[1], str(self.game / 'image.bin'))
        self.assertEqual(args[2:4], ['--romfs', str(self.game / 'romfs')])
        self.assertEqual(args[-2], '--user')
        self.assertTrue(Path(args[-1]).is_absolute())

    def test_default_save_is_preserved_with_profile_isolation(self):
        self.install()
        self.store.set_game_dir(self.game)
        original = self.root / 'local' / 'share' / 'hookshot'
        save = original / 'save' / 'abc123' / 'slot_00'
        save.mkdir(parents=True)
        (save / 'progress.sav').write_bytes(b'current original save')
        legacy = self.store.register_legacy(original)
        new = self.store.create('Fresh')
        args, _ = self.store.game_command(new['id'])
        self.assertEqual(args[-1], new['user_dir'])
        self.assertNotEqual(args[-1], legacy['user_dir'])
        self.assertEqual((save / 'progress.sav').read_bytes(), b'current original save')

    def test_slot_discovery_read_only(self):
        save = self.root / 'user' / 'save' / '5a3c9e1f0b7d24680000000000000001'
        for n in ('slot_00','slot_02'):
            (save / n).mkdir(parents=True)
            (save / n / 'progress.sav').write_bytes(bytes([int(n[-2:])]))
        results = read_game_slots(self.root / 'user')
        self.assertEqual([s.index for s in results], [0, 2])
        self.assertEqual([s.has_progress for s in results], [True, True])

    def test_empty_original_register_is_non_destructive(self):
        root = self.root / 'empty-original'
        p = self.store.register_legacy(root, initialize_empty=True)
        self.assertIsNotNone(p)
        self.assertTrue(root.is_dir())
        self.assertEqual(list(root.iterdir()), [])
        self.assertEqual(self.store.register_legacy(root)['id'], p['id'])

    def test_old_game_folder_saves_are_registered_in_place(self):
        self.install()
        self.store.set_game_dir(self.game)
        old_progress = self.game / 'user' / 'save' / 'account' / 'slot_00' / 'progress.sav'
        old_progress.parent.mkdir(parents=True)
        old_progress.write_bytes(b'legacy bytes')
        with patch('profile_store.default_hookshot_user', return_value=self.root / 'xdg' / 'hookshot'):
            profiles = register_installed_saves(self.store, self.game)
        self.assertEqual(len(profiles), 1)
        self.assertEqual(profiles[0]['user_dir'], str((self.game / 'user').resolve()))
        self.assertEqual(old_progress.read_bytes(), b'legacy bytes')
        self.assertFalse((self.root / 'xdg' / 'hookshot').exists())

    def test_xdg_and_game_folder_saves_are_both_discovered(self):
        self.install()
        self.store.set_game_dir(self.game)
        legacy = self.game / 'user' / 'save' / 'account' / 'slot_00'
        xdg = self.root / 'xdg' / 'hookshot' / 'save' / 'account' / 'slot_00'
        for root in (legacy, xdg):
            root.mkdir(parents=True)
            (root / 'progress.sav').write_bytes(b'distinct')
        with patch('profile_store.default_hookshot_user', return_value=self.root / 'xdg' / 'hookshot'):
            profiles = register_installed_saves(self.store, self.game)
            repeated = register_installed_saves(self.store, self.game)
        self.assertEqual(len(profiles), 2)
        self.assertEqual([p['id'] for p in profiles], [p['id'] for p in repeated])
        self.assertEqual(len(self.store.list_profiles()), 2)

    def test_randomizer_profile_remains_vanilla(self):
        self.install()
        self.store.set_game_dir(self.game)
        p = self.store.create('Seeded', 'randomizer-planned', '0abc')
        manifest = self.store.get_randomizer(p['id'])
        self.assertEqual(manifest['seed'], '0000000000000ABC')
        self.assertFalse(manifest['gameplay_enabled'])
        self.assertEqual(manifest['engine_status'], 'configuration_only')

if __name__ == '__main__':
    unittest.main()
