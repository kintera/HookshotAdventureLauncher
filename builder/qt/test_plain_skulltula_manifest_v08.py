"""Regression: preserve Gold Skulltula; retire ONLY plain skulltula alias."""
import json
from pathlib import Path
import tempfile
import unittest

from game_icon_core import ROLES, default_manifest, load_manifest, save_manifest, merge_manifest

HERE = Path(__file__).parent

class PlainSkulltulaManifestTests(unittest.TestCase):
    def test_shipped_manifest_retains_gold_and_mask_and_no_plain(self):
        icons = json.loads((HERE / 'assets/game_icons/icon_manifest.json').read_text())['icons']
        self.assertNotIn('skulltula', icons)
        self.assertIn('gold_skulltula', icons)
        self.assertEqual(icons['gold_skulltula']['texture'], 'Item_Token_Gold')
        self.assertTrue(icons['gold_skulltula']['verified'])
        self.assertIn('mask_skull', icons)
        self.assertEqual(len(icons), 132)  # 119 mapped roles, Gold Skulltula preserved

    def test_roles_keep_gold_but_not_plain(self):
        self.assertIn('gold_skulltula', ROLES['Quest'])
        self.assertNotIn('skulltula', ROLES['Quest'])
        defaults = default_manifest()['icons']
        self.assertIn('gold_skulltula', defaults)
        self.assertNotIn('skulltula', defaults)

    def test_legacy_plain_only_removed_in_load_save_merge(self):
        doc = json.loads((HERE / 'assets/game_icons/icon_manifest.json').read_text())
        doc['icons']['skulltula'] = {'archive': 'UI/Tex/Icon/UnusedGeneric.bntx.zs',
                                      'texture': 'UnusedGeneric', 'verified': True}
        with tempfile.TemporaryDirectory() as td:
            path = Path(td) / 'legacy.json'
            path.write_text(json.dumps(doc))
            loaded = load_manifest(path)
            self._check_preserved(loaded)
            save_manifest(path, doc)
            self._check_preserved(json.loads(path.read_text()))
            merged = merge_manifest(default_manifest(), doc)
            self._check_preserved(merged)
            replaced = merge_manifest(default_manifest(), doc, replace=True)
            self._check_preserved(replaced)

    def _check_preserved(self, doc):
        icons = doc['icons']
        self.assertNotIn('skulltula', icons)
        self.assertIn('gold_skulltula', icons)
        self.assertEqual(icons['gold_skulltula']['texture'], 'Item_Token_Gold')
        self.assertTrue(icons['gold_skulltula']['verified'])
        self.assertIn('mask_skull', icons)

if __name__ == '__main__':
    unittest.main()
