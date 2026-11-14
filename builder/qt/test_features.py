from pathlib import Path
import json
import tempfile
import unittest

from profile_store import ProfileStore, ProfileError
from randomizer_config import generate_seed, normalized_seed, Settings, make_manifest, validate_manifest
from save_info import scan_user_dir, validate_display_info, human_size


class ConfigTests(unittest.TestCase):
    def test_seed_determinism(self):
        a = make_manifest("Zelda Adventure")
        b = make_manifest("Zelda Adventure")
        self.assertEqual(a, b)
        self.assertEqual(len(a["seed"]), 16)
        self.assertEqual(a["engine_status"], "configuration_only")
        self.assertFalse(a["gameplay_enabled"])

    def test_setting_changes_hash(self):
        a = make_manifest("A", Settings(shuffle_chests=True))
        b = make_manifest("A", Settings(shuffle_chests=False))
        self.assertNotEqual(a["settings_hash"], b["settings_hash"])

    def test_seed_is_64_bits(self):
        self.assertEqual(len(generate_seed()), 16)
        self.assertEqual(normalized_seed("aB3"), "0000000000000AB3")

    def test_gameplay_enabled_refused(self):
        d = make_manifest("A")
        d["gameplay_enabled"] = True
        with self.assertRaises(ValueError):
            validate_manifest(d)

    def test_entrance_mode_rejected(self):
        with self.assertRaises(ValueError):
            Settings.from_dict({"entrance_randomization": True})


class SaveInfoTests(unittest.TestCase):
    def test_read_only_stat(self):
        with tempfile.TemporaryDirectory() as td:
            root = Path(td)
            save = root / "save" / "gameid"
            save.mkdir(parents=True)
            (save / "data.bin").write_bytes(b"unmodified game data")
            (root / "cache").mkdir()
            (root / "cache" / "shader-cache.dat").write_bytes(b"1234")
            before = (save / "data.bin").read_bytes()
            result = scan_user_dir(root)
            self.assertEqual(result.file_count, 2)
            self.assertEqual(result.save_files, 1)
            self.assertEqual(result.bytes_used, len(before) + 4)
            self.assertEqual((save / "data.bin").read_bytes(), before)

    def test_display_validation(self):
        a = validate_display_info({"hero_name": "Link", "hearts": 7, "rupees": 0})
        self.assertEqual(a["rupees"], 0)
        with self.assertRaises(ValueError):
            validate_display_info({"hearts": 30})
        with self.assertRaises(ValueError):
            validate_display_info({"rupees": "100"})


class StoreFeaturesTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.store = ProfileStore(self.root / "mgr")

    def test_info_metadata_is_separate_from_game_saves(self):
        p = self.store.create("Real Save")
        user_dir = Path(p["user_dir"])
        (user_dir / "save.bin").write_bytes(b"important")
        self.store.set_info(p["id"], {"hero_name": "K", "hearts": 8})
        self.assertEqual((user_dir / "save.bin").read_bytes(), b"important")
        self.assertEqual(self.store.get_info(p["id"])["hearts"], 8)
        self.assertFalse((user_dir / "adventure_info.json").exists())

    def test_randomizer_manifest_saved_and_duplicate(self):
        a = self.store.create("Rando", "randomizer-planned", "abc")
        self.store.set_randomizer(a["id"], "testing", Settings(shuffle_songs=False))
        b = self.store.duplicate(a["id"], "Rando copy")
        self.assertEqual(self.store.get_randomizer(a["id"]), self.store.get_randomizer(b["id"]))
        self.assertFalse(self.store.get_randomizer(b["id"])["gameplay_enabled"])

    def test_managed_archive_keeps_sidecar_metadata(self):
        p = self.store.create("Save")
        self.store.set_info(p["id"], {"hearts": 12})
        archive = self.store.archive(p["id"])
        self.assertEqual(json.loads((archive / "metadata" / "adventure_info.json").read_text())["hearts"], 12)

    def test_original_registered_in_place_and_not_archivable(self):
        original = self.root / "original"
        original.mkdir()
        (original / "world.sav").write_text("DO NOT DELETE")
        p = self.store.register_legacy(original)
        with self.assertRaises(ProfileError):
            self.store.archive(p["id"])
        self.assertEqual((original / "world.sav").read_text(), "DO NOT DELETE")

    def test_symlink_copy_refused(self):
        original = self.root / "src"
        original.mkdir()
        (original / "real").write_bytes(b"data")
        (original / "linked").symlink_to(original / "real")
        with self.assertRaises(ProfileError):
            self.store.import_user_dir(original, "Link")


if __name__ == "__main__":
    unittest.main()

class RestoreBackupTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.store = ProfileStore(Path(self.temp.name) / "mgr")

    def test_restores_into_new_profile_no_overwrite(self):
        p = self.store.create("A", "randomizer-planned", "abc")
        Path(p["user_dir"]).joinpath("my.sav").write_bytes(b"before")
        self.store.set_info(p["id"], {"hero_name": "Hero", "hearts": 10})
        backup = self.store.backup(p["id"])
        Path(p["user_dir"]).joinpath("my.sav").write_bytes(b"after")
        restored = self.store.restore_backup_as_new(backup, "B")
        self.assertNotEqual(p["user_dir"], restored["user_dir"])
        self.assertEqual(Path(restored["user_dir"]).joinpath("my.sav").read_bytes(), b"before")
        self.assertEqual(Path(p["user_dir"]).joinpath("my.sav").read_bytes(), b"after")
        self.assertEqual(self.store.get_info(restored["id"])["hearts"], 10)
        self.assertEqual(self.store.get_randomizer(restored["id"])["seed"], self.store.get_randomizer(p["id"])["seed"])

    def test_rejects_external_restore_folder(self):
        with tempfile.TemporaryDirectory() as other:
            with self.assertRaises(ProfileError):
                self.store.restore_backup_as_new(Path(other), "Unsafe")
