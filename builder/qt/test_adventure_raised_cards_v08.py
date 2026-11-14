"""Headless checks: 3 raised gradient shortcuts, unchanged launcher behavior."""
import ast
from pathlib import Path
import unittest

from ui_theme import THEMES, adventure_menu_qss

SOURCE = (Path(__file__).with_name('hookshot_qt.py')).read_text(encoding='utf-8')


def launch_method():
    module = ast.parse(SOURCE)
    window = next(node for node in module.body if isinstance(node, ast.ClassDef) and node.name == 'Window')
    make = next(node for node in window.body if isinstance(node, ast.FunctionDef) and node.name == 'make_launch_page')
    return ast.get_source_segment(SOURCE, make)


class RaisedAdventureCardsTests(unittest.TestCase):
    def test_three_cards_each_have_shadow_not_the_outer_wrapper(self):
        method = launch_method()
        self.assertIn('for i in range(3):', method)
        loop = method.split('for i in range(3):', 1)[1]
        self.assertIn("card.setObjectName('profileCardFrame')", loop)
        self.assertIn('QGraphicsDropShadowEffect(card)', loop)
        self.assertIn('shadow.setBlurRadius(22)', loop)
        self.assertIn('shadow.setOffset(0, 5)', loop)
        self.assertIn('card.setGraphicsEffect(shadow)', loop)
        self.assertIn("adventure_card.setObjectName('adventureList')", method)
        self.assertNotIn('adventure_card.setGraphicsEffect(', method)
        self.assertIn('self.activate_launcher_box(index)', method)
        self.assertNotIn('self.launch_game(', method)

    def test_theme_derived_convex_gradient_and_bevels(self):
        gradients = []
        for key, _, _ in THEMES:
            with self.subTest(theme=key):
                css = adventure_menu_qss(key)
                rules = css.split('QFrame#profileCardFrame {', 1)[1].split('}', 1)[0]
                self.assertIn('background:qlineargradient(x1:0,y1:0,x2:0,y2:1,', rules)
                for stop in ('stop:0 ', 'stop:0.12 ', 'stop:0.55 ', 'stop:1 '):
                    self.assertIn(stop, rules)
                for side in ('top', 'left', 'right', 'bottom'):
                    self.assertIn(f'border-{side}-color:', rules)
                self.assertIn('border-radius:12px', rules)
                self.assertIn('QFrame#profileCardFrame:hover { background:qlineargradient(', css)
                gradients.append(rules.split(';', 1)[0])
        self.assertEqual(len(gradients), len(set(gradients)))

    def test_no_new_collectible_tracking_or_save_mutation(self):
        self.assertNotIn('heart_pickup_probe', SOURCE)
        self.assertNotIn('Heart Pieces collected', SOURCE)
        self.assertNotIn('write_adventure_slots(', SOURCE)


if __name__ == '__main__':
    unittest.main()
