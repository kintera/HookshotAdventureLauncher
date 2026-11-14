"""Bottle contents are visible without a mouse/hover. UI v0.8."""
from pathlib import Path
import ast
import unittest
from adventure_panel_model import ADVENTURE_UI_VERSION, bottle_caption, bottle_appearance
from save_reader import BOTTLE_CONTENTS, BOTTLE_SLOTS, SUPPLIES, HEADER_PROGRESS, decode_pair
from test_save_reader_v06 import make_save

HERE = Path(__file__).resolve().parent

class VisibleBottleTests(unittest.TestCase):
    def test_v08_unchanged(self):
        self.assertEqual(ADVENTURE_UI_VERSION, 'v0.8')

    def test_all_known_contents_have_visible_names(self):
        for enum,(role,name) in BOTTLE_CONTENTS.items():
            caption = bottle_caption({'slot':1,'owned':True,'role':role,'label':name})
            self.assertTrue(caption)
            self.assertNotEqual(caption, 'Unknown', (hex(enum),name))
            self.assertLessEqual(len(caption), 13)

    def test_bottle_caption_handles_empty_absent_unknown(self):
        self.assertEqual(bottle_caption({'owned':True,'label':'Empty Bottle'}), 'Empty')
        self.assertEqual(bottle_caption({'owned':False,'label':'No bottle'}), '—')
        self.assertEqual(bottle_caption({'owned':None,'label':'Unknown'}), 'Unknown')
        self.assertEqual(bottle_caption({'owned':True,'label':'Unknown Contents'}), 'Unknown')
        self.assertEqual(bottle_caption(None), 'Unknown')

    def test_real_save_like_four_contents_render_without_hover(self):
        vals={}
        contents=[0x73356827,0x74D62ECB,0x6F49E357,0xBE69DE49,None]
        for index,(stock,valid,enum_key) in enumerate(BOTTLE_SLOTS):
            vals[stock]=int(index<4)
            vals[valid]=int(index<4)
            if contents[index] is not None:
                v=contents[index]
                vals[enum_key]=v if v < 0x80000000 else v-0x100000000
        stats,_,_=decode_pair(make_save(HEADER_PROGRESS,vals),None)
        captions=[bottle_caption(bottle_appearance(stats,n)) for n in range(1,6)]
        self.assertEqual(captions,['Milk','Bug','Fairy','Empty','—'])

    def test_expanded_renderer_labels_are_painted_not_tooltips(self):
        source=(HERE/'zelda_summary_widget.py').read_text()
        ast.parse(source)
        block=source.split('def set_stats(self, stats):',1)[1].split('def paintEvent',1)[0]
        self.assertNotIn('setToolTip',block)
        self.assertNotIn('Bottle %s:',source)
        self.assertIn('caption = bottle_caption(bottle)',source)
        self.assertIn('text_rect(caption, x-20',source)
        self.assertIn('icon_tile(bottle_icon',source)

if __name__=='__main__': unittest.main()
