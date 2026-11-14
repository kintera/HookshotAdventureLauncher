"""Regression coverage for stripped UI metadata and conservative item decoding."""
import importlib.util
import sys
import unittest
from pathlib import Path
from tempfile import TemporaryDirectory
from unittest.mock import patch
from adventure_save_display import save_header_location_age
from profile_store import ProfileStore, register_installed_saves

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import save_reader


class FakeFields:
    def __init__(self, items):
        self.fields = dict(items)
        self.values = items
    def string64(self, key):
        return self.values[key]


class SaveHeaderTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.ui = (HERE/'hookshot_qt.py').read_text()

    def test_user_requested_save_header(self):
        self.assertNotIn('self.detail_character', self.ui)
        self.assertIn('self.detail_age', self.ui)
        self.assertIn("'Location: ' + scene", self.ui)
        self.assertIn("'Age: ' + age_label", self.ui)
        self.assertIn("'Save Date: ' + current.saved_in_game", self.ui)
        self.assertIn("str(profile.get('name') or '').strip() or 'Link'", self.ui)

    def test_no_original_directory_label(self):
        self.assertNotIn('self.detail_type', self.ui)
        self.assertNotIn('Original save directory', self.ui)

    def test_location_and_age_are_independent(self):
        self.assertEqual(save_header_location_age('ZoraRiver', 'Child'), ('Zora River', 'Child'))
        self.assertEqual(save_header_location_age('Child: Zora River'), ('Zora River', 'Child'))
        self.assertEqual(save_header_location_age('Adult_KokiriForest'), ('Kokiri Forest', 'Adult'))
        self.assertEqual(save_header_location_age(None, None), ('Unknown Location', 'Unknown'))
        self.assertEqual(save_header_location_age('Child: Zora River', 'Adult'), ('Zora River', 'Adult'))

    def test_auto_import_link_without_overwriting_custom_profile_name(self):
        with TemporaryDirectory() as temp:
            base = Path(temp)
            store = ProfileStore(base / 'profiles')
            game = base / 'game'
            game.mkdir()
            existing = base / 'user' / 'save' / 'acct' / 'slot_00' / 'progress.sav'
            existing.parent.mkdir(parents=True)
            existing.write_bytes(b'untouched')
            with patch('profile_store.default_hookshot_user', return_value=base / 'user'):
                current = register_installed_saves(store, game)[0]
                self.assertEqual(current['name'], 'Link')
                store.rename(current['id'], 'My Adventure')
                another = register_installed_saves(store, game)[0]
                self.assertEqual(another['name'], 'My Adventure')
                self.assertEqual(existing.read_bytes(), b'untouched')

    def test_existing_launch_and_profile_selection_retained(self):
        self.assertIn('Choose Another Adventure', self.ui)
        self.assertIn('self.launch_game(profile_id=self.detail_profile_id)', self.ui)
        self.assertIn("profile['user_dir']", self.ui)

    def test_named_positive_fairy_ocarina_only(self):
        table = FakeFields({save_reader.SELECTED_ITEMS['ocarina']: 'Item_Ocarina_Fairy'})
        decoded = save_reader.decode_selected_items(table)
        self.assertIs(decoded['inventory_flags']['ocarina_fairy'], True)
        self.assertNotIn('ocarina_time', decoded['inventory_flags'])
        self.assertEqual(decoded['stone_flags'], {})

    def test_selected_stone_does_not_establish_ownership(self):
        table = FakeFields({save_reader.SELECTED_ITEMS['quest_stone']: 'Item_Stone_Water'})
        decoded = save_reader.decode_selected_items(table)
        self.assertEqual(decoded['stone_flags'], {})
        self.assertNotIn('Goron Ruby', decoded['stone_flags'])
        self.assertNotIn('Kokiri Emerald', decoded['stone_flags'])

    def test_unrecognized_values_are_unknown(self):
        table = FakeFields({save_reader.SELECTED_ITEMS['quest_stone']: 'Some_Unfamiliar_Field'})
        self.assertEqual(save_reader.decode_selected_items(table)['stone_flags'], {})

    def test_missing_optional_fields_are_unknown(self):
        self.assertEqual(save_reader.decode_selected_items(FakeFields({})),
                         {'inventory_flags': {}, 'stone_flags': {}})


if __name__ == '__main__':
    unittest.main()
