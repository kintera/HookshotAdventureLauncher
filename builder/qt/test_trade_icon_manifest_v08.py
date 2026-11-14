"""New trade/pass icon roles use verified game mappings only."""
import ast
import json
from pathlib import Path
import tempfile
import unittest

from game_icon_core import (ROLE_SET, ROLES, default_manifest, load_manifest,
                            merge_manifest, save_manifest, validate_manifest)
from save_reader import CHILD_TRADE_STOCKS, ADULT_TRADE_STOCKS

HERE = Path(__file__).resolve().parent
MANIFEST = HERE / 'assets/game_icons/icon_manifest.json'
NEW_ROLES = ('gerudo_pass', 'trade_weird_egg', 'trade_wake_up_cucco', 'trade_zelda_letter',
             'trade_pocket_egg', 'trade_pocket_cucco', 'trade_cojiro', 'trade_odd_mushroom',
             'trade_odd_poultice', 'trade_poachers_saw',
             'trade_broken_goron_sword', 'trade_prescription',
             'trade_eyeball_frog', 'trade_eye_drops', 'trade_claim_check')

class TradeManifestTests(unittest.TestCase):
    def test_mappings_are_real_placeholders_not_speculation(self):
        doc = validate_manifest(json.loads(MANIFEST.read_text()))
        self.assertEqual(len(doc['icons']), 132)
        self.assertEqual(sum(bool(e.get('verified')) for e in doc['icons'].values()), 119)
        self.assertEqual(set(ROLES['Trade Quests']), set(NEW_ROLES))
        self.assertEqual(len(set(NEW_ROLES)), len(NEW_ROLES))
        for role in NEW_ROLES:
            self.assertIn(role, ROLE_SET)
            self.assertIn(role, doc['icons'])
            entry = doc['icons'][role]
            self.assertTrue(entry['verified'])
            self.assertTrue(entry['archive'].startswith('UI/Tex/Icon/'))
            self.assertTrue(entry['texture'])
        self.assertNotIn('skulltula', doc['icons'])
        self.assertEqual(doc['icons']['gold_skulltula']['texture'], 'Item_Token_Gold')
        self.assertEqual(doc['icons']['mask_skull']['texture'], 'Armor_Child_Mask_Skull')
        for role in ('mask_keaton','mask_skull','mask_spooky','mask_bunny','mask_truth'):
            self.assertTrue(doc['icons'][role]['verified'])
            self.assertNotIn(role, NEW_ROLES)

    def test_manifest_role_keys_match_save_reader(self):
        known = {role for role, _label, _hash in CHILD_TRADE_STOCKS + ADULT_TRADE_STOCKS}
        known.add('gerudo_pass')
        self.assertEqual(known, set(NEW_ROLES) | {
            'mask_keaton', 'mask_skull', 'mask_spooky', 'mask_bunny', 'mask_truth'})

    def test_icon_browser_safely_imports_old_manifest_without_overwriting_trade_slots(self):
        doc = json.loads(MANIFEST.read_text())
        legacy = json.loads(MANIFEST.read_text())
        for role in NEW_ROLES:
            legacy['icons'].pop(role)
        merged = merge_manifest(doc, legacy)
        self.assertEqual(set(merged['icons']), set(doc['icons']))
        self.assertEqual(merged['icons']['gold_skulltula'], doc['icons']['gold_skulltula'])
        self.assertEqual(set(default_manifest()['icons']) & set(NEW_ROLES), set(NEW_ROLES))
        with tempfile.TemporaryDirectory() as temp:
            path = Path(temp)/'icons.json'
            save_manifest(path, merged)
            reread = load_manifest(path)
            for role in NEW_ROLES:
                self.assertIn(role, reread['icons'])
            self.assertNotIn('skulltula', reread['icons'])

    def test_preview_renders_only_verified_current_item_role(self):
        text = (HERE/'zelda_summary_widget.py').read_text()
        ast.parse(text)
        self.assertIn("icon_tile('gerudo_pass'", text)
        self.assertIn("icon_tile(trade.role or 'trade_weird_egg'", text)
        self.assertIn('trading_quest_overview_hover_text(raw)', text)
        self.assertNotIn("text_rect(f'{tag}:'", text)

if __name__ == '__main__':
    unittest.main()
