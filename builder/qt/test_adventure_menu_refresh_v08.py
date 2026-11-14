"""Source-level layout/styling checks for the v0.8 Adventure menu refresh.

No game data or Qt rendering dependency; pixel checks belong on the user's
PyQt6 desktop with the existing game artwork/cache.
"""
from pathlib import Path
import ast
import unittest

from ui_theme import THEMES, adventure_menu_qss, stylesheet_for

HERE = Path(__file__).parent
SRC = (HERE / 'hookshot_qt.py').read_text(encoding='utf-8')


def method_source(name):
    tree = ast.parse(SRC)
    klass = next(n for n in tree.body if isinstance(n, ast.ClassDef) and n.name == 'Window')
    member = next(n for n in klass.body if isinstance(n, ast.FunctionDef) and n.name == name)
    return ast.get_source_segment(SRC, member)


class AdventureMenuRefreshTests(unittest.TestCase):
    def test_three_slots_keep_the_select_then_detail_flow(self):
        make = method_source('make_launch_page')
        self.assertIn('for i in range(3):', make)
        self.assertIn("select = QPushButton('Create')", make)
        self.assertIn('self.activate_launcher_box(index)', make)
        self.assertNotIn('self.launch_game(', make)
        self.assertIn('self.show_adventure_details(profile[\'id\'])', method_source('activate_launcher_box'))

    def test_flat_list_without_double_bordered_step_card(self):
        make = method_source('make_launch_page')
        self.assertIn('adventure_card = QWidget()', make)
        self.assertIn("adventure_card.setObjectName('adventureList')", make)
        self.assertNotIn("adventure_card.setObjectName('stepCard')", make)
        self.assertIn("card.setObjectName('profileCardFrame')", make)
        self.assertIn("slot_label.setObjectName('adventureSlotLabel')", make)
        self.assertIn('setFixedSize(156, 88)', make)
        self.assertIn('QGraphicsDropShadowEffect(card)', make)
        self.assertIn('card.setGraphicsEffect(shadow)', make)

    def test_save_summary_is_present_for_filled_rows_only(self):
        make = method_source('make_launch_page')
        refresh = method_source('refresh_launcher_boxes')
        self.assertIn('ZeldaStatsWidget(compact=True)', make)
        self.assertIn('info.setObjectName(\'adventureEmptyHint\')', make)
        self.assertIn('description.show()', refresh)
        self.assertIn('description.hide()', refresh)
        self.assertIn("self.launcher_icon_summaries[i].hide()", refresh)
        self.assertIn("self.launcher_icon_summaries[i].show()", refresh)

    def test_seven_themes_have_the_same_menu_component_styling(self):
        for key, _, _ in THEMES:
            with self.subTest(theme=key):
                css = stylesheet_for(key)
                self.assertIn('Adventure menu refresh:', css)
                for part in ('QWidget#adventureList', 'QFrame#profileCardFrame',
                             'QLabel#adventureCardTitle', 'QLabel#adventureIntro',
                             'QLabel#adventureSlotLabel', 'QPushButton#adventureSelectButton',
                             'QTabWidget#launchTabs::pane', 'QWidget#root QFrame#panel',
                             'QWidget#root QPushButton#profileCard'):
                    self.assertIn(part, css)
                menu = adventure_menu_qss(key)
                self.assertIn('background:transparent; border:none;', menu)
                self.assertIn('QFrame#profileCardFrame { background:qlineargradient(', menu)
                self.assertIn('QFrame#profileCardFrame:hover { background:qlineargradient(', menu)
                self.assertIn('border-top-color:', menu)
                self.assertIn('border-bottom-color:', menu)

    def test_readiness_and_install_flow_are_still_separate(self):
        self.assertIn('self.install_tag = tag(\'Checking installation…\')', SRC)
        self.assertIn('self.step_status.hide()', SRC)
        self.assertIn('self.footer.setVisible(installing)', SRC)
        self.assertIn("self.detail_launch = QPushButton('Launch Game')", SRC)
        self.assertIn("self.detail_launch.clicked.connect(", SRC)

    def test_heart_tracking_was_not_added(self):
        self.assertNotIn('heart_pickup_probe', SRC)
        self.assertNotIn('Boss Heart Containers collected', SRC)
        self.assertNotIn('Pieces of Heart collected', SRC)


if __name__ == '__main__':
    unittest.main()
