"""Synthetic binary saves only; does not touch personal game files."""
import hashlib
from pathlib import Path
import struct
import tempfile
import unittest

from save_reader import (AdventureSlot, CAPTION, PROGRESS, FieldTable,
                         HEADER_PROGRESS, HEADER_CAPTION, UnsupportedSave,
                         decode_pair, read_adventure_slots)


def make_save(header, values, strings=None, jpeg=False):
    strings = strings or {}
    size = header + 512
    data = bytearray(size)
    struct.pack_into('<III', data, 0, 0x01020304, 0x002D80EB, header)
    off = 40
    next_data = header + 16
    for key, value in values.items():
        struct.pack_into('<I', data, off, key)
        struct.pack_into('<i', data, off+4, value)
        off += 8
    for key, value in strings.items():
        struct.pack_into('<I', data, off, key)
        struct.pack_into('<I', data, off+4, next_data)
        encoded = value.encode('utf-8')[:63]
        data[next_data:next_data+len(encoded)] = encoded
        next_data += 64
        off += 8
    if jpeg:
        contents = b'\xff\xd8' + b'pretend-JPEG' + b'\xff\xd9'
        struct.pack_into('<II', data, off, CAPTION['jpeg'], next_data)
        struct.pack_into('<I', data, next_data, len(contents))
        data[next_data+4:next_data+4+len(contents)] = contents
    return bytes(data)


class ReaderTests(unittest.TestCase):
    def progress(self):
        return make_save(HEADER_PROGRESS, {
            PROGRESS['rupees']: 355, PROGRESS['hearts_max']: 48,
            PROGRESS['hearts_current']: 47, PROGRESS['skulltulas']: 42,
            PROGRESS['magic_current']: 125, PROGRESS['magic_max']: 240,
        }, {PROGRESS['location']: 'Kokiri Forest', PROGRESS['progress_id']: 'Save_A'})

    def caption(self):
        return make_save(HEADER_CAPTION, {
            CAPTION['year']: 2026, CAPTION['month']: 11,
            CAPTION['day']: 12, CAPTION['hour']: 14,
            CAPTION['minute']: 23, CAPTION['autosave']: 1,
        }, jpeg=True)

    def test_real_save_field_ids_and_scaling(self):
        stats, saved, jpeg = decode_pair(self.progress(), self.caption())
        self.assertEqual(stats['rupees'], 355)
        self.assertEqual(stats['hearts'], 12.0)
        self.assertEqual(stats['current_hearts'], 11.75)
        self.assertEqual(stats['magic'], 12.5)
        self.assertEqual(stats['max_magic'], 24.0)
        self.assertEqual(stats['gold_skulltulas'], 42)
        self.assertEqual(stats['location'], 'Kokiri Forest')
        self.assertTrue(stats['autosave'])
        self.assertEqual(saved, '2026-11-12 14:23')
        self.assertTrue(jpeg.startswith(b'\xff\xd8'))

    def test_missing_caption_still_reads_progress(self):
        stats, saved, preview = decode_pair(self.progress(), None)
        self.assertEqual(stats['location'], 'Kokiri Forest')
        self.assertIsNone(saved)
        self.assertIsNone(preview)

    def test_unknown_file_rejected(self):
        with self.assertRaises(UnsupportedSave):
            decode_pair(bytes(HEADER_PROGRESS), None)

    def test_bad_block_is_rejected(self):
        malformed = make_save(HEADER_PROGRESS, {}, {PROGRESS['location']: 'Here'})
        data = bytearray(malformed)
        struct.pack_into('<I', data, 44, 0xFFFFFFFF)
        with self.assertRaises(UnsupportedSave):
            decode_pair(bytes(data), None)

    def test_folder_scan_never_mutates_files(self):
        with tempfile.TemporaryDirectory() as t:
            slot = Path(t)/'save'/'user123'/'slot_00'
            slot.mkdir(parents=True)
            (slot/'progress.sav').write_bytes(self.progress())
            (slot/'caption.sav').write_bytes(self.caption())
            original = hashlib.sha256((slot/'progress.sav').read_bytes()).hexdigest()
            rows = read_adventure_slots(Path(t))
            self.assertEqual(len(rows), 1)
            self.assertEqual(rows[0].statistics['rupees'], 355)
            self.assertEqual(original, hashlib.sha256((slot/'progress.sav').read_bytes()).hexdigest())

    def test_bad_save_is_reported_not_crash(self):
        with tempfile.TemporaryDirectory() as t:
            slot = Path(t)/'save'/'user123'/'slot_01'
            slot.mkdir(parents=True)
            (slot/'progress.sav').write_bytes(b'garbage')
            rows = read_adventure_slots(Path(t))
            self.assertEqual(len(rows), 1)
            self.assertTrue(rows[0].warning)
            self.assertEqual(rows[0].statistics, {})

    def test_symlink_does_not_escape_profile(self):
        with tempfile.TemporaryDirectory() as t:
            base=Path(t)
            (base/'save').mkdir()
            (base/'save'/'link').symlink_to(base, target_is_directory=True)
            self.assertEqual(read_adventure_slots(base), [])


if __name__ == '__main__': unittest.main()
