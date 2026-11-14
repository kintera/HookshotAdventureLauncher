"""Song-state regression checks; synthetic saves only, never edit game data."""
import hashlib
import ast
from pathlib import Path
import unittest

from save_reader import (
    SONG_GET_EVENT_FIELDS, SONG_ISGET_FIELDS,
    HEADER_PROGRESS, SELECTED_ITEMS, decode_pair,
)
from test_save_reader_v06 import make_save


class SongStatusFieldsTests(unittest.TestCase):
    def read(self, fields, strings=None):
        data = make_save(HEADER_PROGRESS, fields, strings=strings)
        digest = hashlib.sha256(data).hexdigest()
        stats, _, _ = decode_pair(data, None)
        self.assertEqual(hashlib.sha256(data).hexdigest(), digest)
        return stats

    def test_song_hashes_are_exact_catalog_ids(self):
        self.assertEqual(SONG_ISGET_FIELDS, {
            'song_zelda_lullaby': 29491202,
            'song_saria': 3556294604,
            'song_storms': 725212495,
            'song_forest_minuet': 3691645780,
            'song_fire_bolero': 1225005753,
            'song_water_serenade': 1067858310,
            'song_shadow_nocturne': 3271267828,
            'song_spirit_requiem': 2150250152,
        })
        self.assertEqual(SONG_GET_EVENT_FIELDS, {
            'song_epona': 3682874359,
            'song_sun': 1262575547,
            'song_light_prelude': 4017837317,
        })
        tree = ast.parse((Path(__file__).parent/'zelda_summary_widget.py').read_text())
        songs = next(ast.literal_eval(node.value) for node in tree.body
                     if isinstance(node, ast.Assign) and
                     any(isinstance(t, ast.Name) and t.id == 'SONG_ITEMS' for t in node.targets))
        self.assertEqual(len(songs), 13)
        self.assertEqual(len(set(SONG_ISGET_FIELDS) | set(SONG_GET_EVENT_FIELDS)), 11)
        self.assertEqual({r for r, _ in songs} -
                         set(SONG_ISGET_FIELDS) - set(SONG_GET_EVENT_FIELDS),
                         {'song_time', 'song_scarecrow'})

    def test_explicit_isget_positive_negative_and_invalid(self):
        fields = {
            SONG_ISGET_FIELDS['song_zelda_lullaby']: 1,
            SONG_ISGET_FIELDS['song_saria']: 0,
            SONG_ISGET_FIELDS['song_storms']: 2,
            SONG_ISGET_FIELDS['song_fire_bolero']: -1,
        }
        stats = self.read(fields)
        self.assertIs(stats['song_flags']['song_zelda_lullaby'], True)
        self.assertIs(stats['song_flags']['song_saria'], False)
        self.assertNotIn('song_storms', stats['song_flags'])
        self.assertNotIn('song_fire_bolero', stats['song_flags'])
        self.assertTrue(stats['song_sources']['song_saria'].startswith('isget:'))

    def test_event_positive_only_no_false_absent_claim(self):
        stats = self.read({
            SONG_GET_EVENT_FIELDS['song_epona']: 1,
            SONG_GET_EVENT_FIELDS['song_sun']: 1,
            SONG_GET_EVENT_FIELDS['song_light_prelude']: 1,
        })
        self.assertIs(stats['song_flags']['song_epona'], True)
        self.assertIs(stats['song_flags']['song_sun'], True)
        self.assertIs(stats['song_flags']['song_light_prelude'], True)
        self.assertTrue(stats['song_sources']['song_light_prelude'].startswith('get_event_candidate:'))
        stats = self.read({
            SONG_GET_EVENT_FIELDS['song_epona']: 0,
            SONG_GET_EVENT_FIELDS['song_sun']: 0,
            SONG_GET_EVENT_FIELDS['song_light_prelude']: 2,
        })
        self.assertNotIn('song_epona', stats['song_flags'])
        self.assertNotIn('song_sun', stats['song_flags'])
        self.assertNotIn('song_light_prelude', stats['song_flags'])

    def test_unrelated_npc_events_do_not_claim_songs(self):
        stats = self.read({
            144026111: 1, 3934625377: 1, 1549343575: 1,
            3146441824: 1, 2880902647: 1,
        })
        self.assertEqual(stats['song_flags'], {})
        self.assertEqual(stats['song_sources'], {})

    def test_sarias_song_time_not_song_of_time_even_when_set(self):
        # The user's live observation rules this field out as Song of Time.
        stats = self.read({2880902647: 1, SONG_GET_EVENT_FIELDS['song_sun']: 1})
        self.assertNotIn('song_time', stats['song_flags'])
        self.assertIs(stats['song_flags']['song_sun'], True)
        stats = self.read({2880902647: 1})
        self.assertNotIn('song_time', stats['song_flags'])
        self.assertNotIn('song_saria', stats['song_flags'])

    def test_owned_ocarina_of_time_unlocks_song_of_time(self):
        stats = self.read({}, {SELECTED_ITEMS['ocarina']: 'Item_Ocarina_Time'})
        self.assertIs(stats['inventory_flags']['ocarina_time'], True)
        self.assertIs(stats['song_flags']['song_time'], True)
        self.assertEqual(stats['song_sources']['song_time'],
                         f'ocarina_time_progression:0x{SELECTED_ITEMS["ocarina"]:08X}')
        self.assertNotIn('song_scarecrow', stats['song_flags'])

    def test_fairy_ocarina_does_not_unlock_song_of_time(self):
        stats = self.read({}, {SELECTED_ITEMS['ocarina']: 'Item_Ocarina_Fairy'})
        self.assertIs(stats['inventory_flags']['ocarina_fairy'], True)
        self.assertNotIn('ocarina_time', stats['inventory_flags'])
        self.assertNotIn('song_time', stats['song_flags'])
        self.assertNotIn('song_time', stats['song_sources'])

    def test_unknown_ocarina_does_not_unlock_song_of_time(self):
        stats = self.read({}, {SELECTED_ITEMS['ocarina']: 'Unrecognized_Ocarina'})
        self.assertNotIn('song_time', stats['song_flags'])

    def test_saria_time_field_is_never_used_as_song_of_time_evidence(self):
        # The old field is ignored even in a save with another ocarina.
        stats = self.read({2880902647: 1},
                          {SELECTED_ITEMS['ocarina']: 'Item_Ocarina_Fairy'})
        self.assertNotIn('song_time', stats['song_flags'])
        # The positive Ocarina-of-Time rule does not depend on that field.
        stats = self.read({2880902647: 0},
                          {SELECTED_ITEMS['ocarina']: 'Item_Ocarina_Time'})
        self.assertIs(stats['song_flags']['song_time'], True)

    def test_missing_fields_are_not_silently_negative(self):
        stats = self.read({})
        self.assertEqual(stats['song_flags'], {})
        self.assertEqual(stats['song_sources'], {})


if __name__ == '__main__':
    unittest.main()
