import json
from pathlib import Path
import tempfile
import unittest
from profile_store import ProfileStore, ProfileError

class StoreTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.base = Path(self.tmp.name)
        self.store = ProfileStore(self.base / "manager")
        self.original = self.base / "original-hookshot"
        self.original.mkdir()
        (self.original / "progress.sav").write_bytes(b"precious game progress")
        self.legacy = self.store.register_legacy(self.original)

    def test_legacy_remains_untouched(self):
        other = self.store.create("Fresh Save")
        self.assertNotEqual(Path(other["user_dir"]), self.original)
        self.assertEqual((self.original / "progress.sav").read_bytes(), b"precious game progress")
        self.assertFalse((Path(other["user_dir"]) / "progress.sav").exists())

    def test_clone_does_not_change_original(self):
        new = self.store.duplicate(self.legacy["id"], "Alternative Run")
        self.assertEqual((Path(new["user_dir"]) / "progress.sav").read_bytes(), b"precious game progress")
        (Path(new["user_dir"]) / "progress.sav").write_bytes(b"changed clone")
        self.assertEqual((self.original / "progress.sav").read_bytes(), b"precious game progress")

    def test_external_cannot_archive(self):
        with self.assertRaises(ProfileError):
            self.store.archive(self.legacy["id"])
        self.assertTrue((self.original / "progress.sav").exists())

    def test_archive_preserves_files(self):
        fresh = self.store.create("New Save")
        (Path(fresh["user_dir"]) / "new.sav").write_bytes(b"data")
        archived = self.store.archive(fresh["id"])
        self.assertEqual((archived / "user" / "new.sav").read_bytes(), b"data")
        self.assertNotIn(fresh["id"], [p["id"] for p in self.store.list_profiles()])
        self.assertTrue((archived / "profile.json").is_file())

    def test_reopen_registry_no_duplicate_original(self):
        again = ProfileStore(self.base / "manager")
        again.register_legacy(self.original)
        self.assertEqual(len(again.list_profiles()), 1)

    def test_randomizer_is_metadata_only(self):
        future = self.store.create("Random", "randomizer-planned", "12345")
        self.assertEqual(future["seed"], "0000000000012345")
        self.assertFalse(self.store.get_randomizer(future["id"])["gameplay_enabled"])
        self.assertEqual(future["kind"], "randomizer-planned")

    def test_import_copies_original(self):
        p = self.store.import_user_dir(self.original, "Imported")
        self.assertEqual((Path(p["user_dir"]) / "progress.sav").read_bytes(), b"precious game progress")
        self.assertTrue((self.original / "progress.sav").exists())

    def test_backup_preserves_original(self):
        backup = self.store.backup(self.legacy["id"])
        self.assertEqual((backup / "user" / "progress.sav").read_bytes(), b"precious game progress")
        self.assertTrue((self.original / "progress.sav").exists())

if __name__ == "__main__":
    unittest.main()
