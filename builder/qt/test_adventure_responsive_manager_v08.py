"""Save-manager responsive and safety regressions (Qt rendering optional)."""
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch
from profile_store import ProfileStore, ProfileError
from adventure_save_display import save_kind_badge_text
from zelda_summary_data import visual_from_stats

BASE = Path(__file__).parent


class ResponsiveManagerTests(unittest.TestCase):
    def test_fixed_card_heights_not_maximized_stretch(self):
        source = (BASE / 'hookshot_qt.py').read_text()
        self.assertIn("card.setFixedHeight(120)", source)
        self.assertIn('adventure_card.setMaximumWidth(1180)', source)
        self.assertIn('adventure_layout.addStretch(1)', source)
        self.assertIn('Qt.AlignmentFlag.AlignTop | Qt.AlignmentFlag.AlignHCenter', source)
        stats = (BASE / 'zelda_summary_widget.py').read_text()
        self.assertIn('width = min(690, max(1, self.width() - 8))', stats)
        self.assertNotIn('p.scale(width/690,1)', stats)

    def test_manager_auto_save_not_manual_editor(self):
        source = (BASE / 'profile_gui.py').read_text()
        self.assertIn('info = dict(from_save)', source)
        self.assertNotIn('Edit manual details…', source)
        self.assertNotIn('class AdventureInfoDialog', source)
        self.assertIn('self.name.setText("Link")', source)
        self.assertIn("'Randomized' if checked else 'Link'", source)
        self.assertIn('self.setMinimumSize(510, 310)', source)

    def test_missing_wallet_tier_uses_child_role(self):
        data = visual_from_stats({})
        from pathlib import Path
        source = (BASE / 'zelda_summary_widget.py').read_text()
        self.assertIsNone(data.wallet_tier)
        self.assertIn(".get(data.wallet_tier, 'wallet_child')", source)

    def test_profile_mode_labels(self):
        self.assertEqual(save_kind_badge_text({'save_label':'rando'}),'Rando Save')
        self.assertEqual(save_kind_badge_text({'save_label':'vanilla'}),'Vanilla Save')
        self.assertEqual(save_kind_badge_text({'kind':'randomizer-planned'}),'Vanilla Save')
        self.assertEqual(save_kind_badge_text({'is_rando':True}),'Vanilla Save')


class DeleteSaveSafetyTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.store = ProfileStore(Path(self.tmp.name) / 'manager')

    def test_rando_label_survives_reopen_without_touching_game_files(self):
        p = self.store.create('Link')
        data_path = Path(p['user_dir']) / 'progress.sav'
        data_path.write_bytes(b'save untouched')
        self.store.set_save_label(p['id'], 'rando')
        reopened = ProfileStore(self.store.root)
        self.assertEqual(reopened.get(p['id'])['save_label'],'rando')
        self.assertEqual(data_path.read_bytes(), b'save untouched')
        with self.assertRaises(ProfileError):
            reopened.set_save_label(p['id'], 'bad')

    def test_duplicate_preserves_user_display_label(self):
        profile=self.store.create('Rando imported as vanilla')
        self.store.set_save_label(profile['id'], 'rando')
        copy=self.store.duplicate(profile['id'], 'Clone')
        self.assertEqual(copy['save_label'], 'rando')

    def test_new_randomizer_profile_label_is_explicit(self):
        p = self.store.create('Randomized','randomizer-planned','123')
        self.assertEqual(p['save_label'],'rando')
        self.assertFalse(self.store.get_randomizer(p['id'])['gameplay_enabled'])

    def test_delete_moves_into_recoverable_trash_and_unpins(self):
        p = self.store.create('test')
        save = Path(p['user_dir']) / 'progress.sav'
        save.write_bytes(b'valuable')
        self.store.set_info(p['id'], {'hero_name':'Link'})
        self.store.pin_launcher_slot(p['id'], 1)
        dest = self.store.delete_save(p['id'])
        self.assertEqual((dest/'profile'/'user'/'progress.sav').read_bytes(), b'valuable')
        self.assertTrue((dest/'metadata'/'adventure_info.json').is_file())
        self.assertEqual((dest/'profile.json').is_file(), True)
        self.assertNotIn(p['id'],[x['id'] for x in self.store.list_profiles()])
        self.assertEqual(self.store.db['launcher_slots'][1],None)
        self.assertEqual(len(ProfileStore(self.store.root).list_profiles()),0)

    def test_external_is_never_deleted(self):
        ext=Path(self.tmp.name)/'user'
        ext.mkdir()
        (ext/'progress.sav').write_bytes(b'original')
        p=self.store.register_legacy(ext)
        with self.assertRaises(ProfileError):
            self.store.delete_save(p['id'])
        self.assertEqual((ext/'progress.sav').read_bytes(),b'original')

    def test_running_game_blocks_deletion(self):
        p=self.store.create('Playing')
        with patch('profile_store.hookshot_is_running',return_value=True):
            with self.assertRaises(ProfileError):
                self.store.delete_save(p['id'])
        self.assertTrue(Path(p['user_dir']).is_dir())


if __name__ == '__main__': unittest.main()
