"""Contracts for the enlarged Adventure Details song-and-mask presentation."""
import ast
import json
from pathlib import Path
import unittest
from adventure_panel_model import MASK_ROLES
from game_icon_core import ROLES, validate_manifest
from save_reader import MASK_STOCKS

HERE = Path(__file__).parent

class SongRelayoutTests(unittest.TestCase):
    def test_song_order_uses_7_then_6_without_reserved_slot(self):
        tree = ast.parse((HERE/'zelda_summary_widget.py').read_text())
        assign = next(node for node in tree.body if isinstance(node, ast.Assign) and
                      any(isinstance(t, ast.Name) and t.id == 'SONG_ITEMS' for t in node.targets))
        songs = ast.literal_eval(assign.value)
        self.assertEqual(len(songs), 13)
        self.assertEqual(songs[6][0], 'song_scarecrow')
        self.assertEqual(len(songs[:7]), 7)
        self.assertEqual(len(songs[7:]), 6)
        self.assertNotIn('song_reserved', [s[0] for s in songs])

    def test_new_song_manifest_roles_are_unmapped_and_not_claimed_verified(self):
        doc = json.loads((HERE/'assets/game_icons/icon_manifest.json').read_text())
        validate_manifest(doc)
        self.assertEqual(len(doc['icons']), 132)
        self.assertEqual(len(ROLES['Songs']), 13)
        for name in ROLES['Songs']:
            self.assertIn(name, doc['icons'])
            self.assertIsNone(doc['icons'][name]['archive'])
            self.assertIsNone(doc['icons'][name]['texture'])
            self.assertFalse(doc['icons'][name]['verified'])
        self.assertTrue(doc['icons']['gold_skulltula']['verified'])
        self.assertNotIn('skulltula', doc['icons'])

    def test_mask_and_equipment_use_collected_styling_only(self):
        self.assertEqual(len(MASK_ROLES), 10)
        self.assertEqual(len(MASK_STOCKS), 10)
        widget = (HERE/'zelda_summary_widget.py').read_text()
        model = (HERE/'adventure_panel_model.py').read_text()
        self.assertNotIn('mask_equipped(', widget)
        self.assertNotIn('if equipped:', widget)
        self.assertNotIn('view.equipped', widget)
        self.assertNotIn('mask_equipped(', model)
        self.assertNotIn("parts.append('Equipped')", model)
        self.assertIn('opacity = 1.0 if owned is True or available else .13', widget)
        self.assertIn("text_rect('SWORDS'", widget)
        self.assertIn("text_rect('SHIELDS'", widget)
        self.assertIn("text_rect('TUNICS'", widget)

    def test_quest_left_inventory_right_and_screenshot_shrinks(self):
        widget = (HERE/'zelda_summary_widget.py').read_text()
        main = (HERE/'hookshot_qt.py').read_text()
        self.assertIn('framed(QRectF(13, 12, 415, 430), 1)', widget)
        self.assertIn('framed(QRectF(443, 12, 665, 430), 1)', widget)
        self.assertIn('QUEST_PANEL_X_OFFSET = 0', widget)
        self.assertIn('INVENTORY_PANEL_X_OFFSET = 430', widget)
        self.assertIn('p.translate(QUEST_PANEL_X_OFFSET, 0)', widget)
        self.assertIn('p.translate(INVENTORY_PANEL_X_OFFSET, 0)', widget)
        # A single inventory translation includes the header, equipment, bags and hitboxes.
        self.assertLess(widget.index('p.translate(INVENTORY_PANEL_X_OFFSET, 0)'), widget.index("text_rect('INVENTORY'"))
        self.assertLess(widget.index('p.translate(INVENTORY_PANEL_X_OFFSET, 0)'), widget.index('for i, role in enumerate(INVENTORY_GRID_ROLES)'))
        self.assertLess(widget.index('p.translate(INVENTORY_PANEL_X_OFFSET, 0)'), widget.index('for i, (bag_key,'))
        # Closing saves must match scaled-canvas and both column saves.
        self.assertTrue(widget.rstrip().endswith('p.restore()\n        p.restore()'))
        self.assertIn('row = 0 if index < 7 else 1', widget)
        self.assertIn('opacity = 1.0 if owned is True or available else .13', widget)
        self.assertIn('self.detail_preview.setFixedSize(186, 105)', main)
        self.assertIn('self.detail_preview_shell.setFixedSize(210, 130)', main)

if __name__ == '__main__':unittest.main()
