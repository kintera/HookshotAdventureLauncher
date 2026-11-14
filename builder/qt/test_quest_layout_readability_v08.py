"""Headless visual-geometry contracts for the expanded Adventure quest column.

No Qt runtime, user save, or copyrighted game sprites are required.
"""
import ast
from pathlib import Path
import unittest


SOURCE = (Path(__file__).resolve().parent / 'zelda_summary_widget.py').read_text()
TREE = ast.parse(SOURCE)
GEOMETRY = {
    target.id: ast.literal_eval(node.value)
    for node in TREE.body if isinstance(node, ast.Assign)
    for target in node.targets if isinstance(target, ast.Name)
    and target.id in {
        'MEDALLIONS_LABEL_Y', 'MEDALLIONS_ICON_Y', 'MASKS_LABEL_Y',
        'MASKS_ICON_Y', 'MASKS_ICON_X', 'MASKS_ICON_STEP',
        'MASKS_ICON_SIZE', 'AMMO_BADGE_ALPHA',
    }
}


class QuestLayoutReadabilityTests(unittest.TestCase):
    def test_medallions_raise_but_clear_stones(self):
        self.assertGreaterEqual(GEOMETRY['MEDALLIONS_ICON_Y'], 203 + 33 + 8)
        self.assertLess(GEOMETRY['MEDALLIONS_ICON_Y'], 275)
        self.assertLessEqual(
            abs(GEOMETRY['MEDALLIONS_LABEL_Y'] - GEOMETRY['MEDALLIONS_ICON_Y']), 5)
        self.assertIn("'medallion_'+name,x,MEDALLIONS_ICON_Y,32", SOURCE)

    def test_masks_label_raised_and_icons_larger_with_clear_inner_border(self):
        # Earlier row was (label 289, icon 310, size 30), with only 2 px
        # below the frame. Preserve the new breathing room inside the rail.
        self.assertLessEqual(GEOMETRY['MASKS_LABEL_Y'], 277)
        self.assertLessEqual(GEOMETRY['MASKS_ICON_Y'], 302)
        self.assertGreaterEqual(GEOMETRY['MASKS_ICON_SIZE'], 34)
        # The medallions LABEL is left-aligned above MASKS. Medallion icons
        # are off to the right, so they may extend slightly below this label.
        self.assertGreaterEqual(GEOMETRY['MASKS_LABEL_Y'],
                                GEOMETRY['MEDALLIONS_LABEL_Y'] + 18 + 7)
        # MASKS label has a 17 px box. 3 px is icon frame shadow padding.
        # Keep an additional 5 px clear between label and top tile frames.
        self.assertGreaterEqual(GEOMETRY['MASKS_ICON_Y'] - 3 -
                                (GEOMETRY['MASKS_LABEL_Y'] + 17), 5)
        # The inner inset rail is at x=16..425, y=15..342. Keep >=4 px
        # margin around the frames INSIDE this rail, not just the outer box.
        self.assertGreaterEqual(342 - (GEOMETRY['MASKS_ICON_Y'] +
                                GEOMETRY['MASKS_ICON_SIZE'] + 3), 5)
        first = GEOMETRY['MASKS_ICON_X'] - 3
        last = (GEOMETRY['MASKS_ICON_X'] + 9*GEOMETRY['MASKS_ICON_STEP']
                + GEOMETRY['MASKS_ICON_SIZE'] + 3)
        self.assertGreaterEqual(first - 16, 4)
        self.assertGreaterEqual(425 - last, 4)
        # Enlarging each tile must not produce overlapping frames.
        self.assertGreaterEqual(GEOMETRY['MASKS_ICON_STEP'],
                                GEOMETRY['MASKS_ICON_SIZE'] + 6)
        self.assertIn("'MASKS', 25, MASKS_LABEL_Y", SOURCE)
        self.assertIn("MASKS_ICON_Y, MASKS_ICON_SIZE, view.owned", SOURCE)

    def test_ammo_badge_is_translucent_and_remains_in_tile(self):
        self.assertLessEqual(GEOMETRY['AMMO_BADGE_ALPHA'], 100)
        self.assertGreaterEqual(GEOMETRY['AMMO_BADGE_ALPHA'], 50)
        self.assertIn("p.setBrush(c('#10101d', AMMO_BADGE_ALPHA))", SOURCE)
        self.assertIn('badge = QRectF(x+size-badge_width-2, y+size-17,', SOURCE)
        self.assertIn('badge_width = min(size - 4, badge_width)', SOURCE)


if __name__ == '__main__':
    unittest.main()
