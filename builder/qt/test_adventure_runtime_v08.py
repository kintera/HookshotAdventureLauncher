"""Headless integration tests: the release needs no icon-browser modules."""
from pathlib import Path
from tempfile import TemporaryDirectory
import ast
import json
import unittest
from PIL import Image
from save_asset_cache import SaveAssetCache

QT = Path(__file__).with_name('hookshot_qt.py')
ASSETS = Path(__file__).with_name('zelda_icon_assets.py')


class FakeDecoder:
    fails = False
    calls = 0
    def __init__(self, *args):
        pass
    def mapped_image(self, entry):
        type(self).calls += 1
        if self.fails:
            raise RuntimeError('Invalid BNTX texture in fixture')
        return Image.new('RGBA', (11, 9), (20, 30, 40, 77))


class RuntimeTests(unittest.TestCase):
    def setUp(self):
        self.tmp = TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        root = Path(self.tmp.name)
        self.romfs = root / 'romfs'
        (self.romfs / 'UI' / 'Tex' / 'Icon').mkdir(parents=True)
        (self.romfs / 'UI' / 'Tex' / 'Icon' / 'Sample.bntx.zs').write_bytes(b'test')
        self.manifest = root / 'icon_manifest.json'
        self.manifest.write_text(json.dumps({
            'schema_version': 1, 'game_version': '2.1.0',
            'icons': {'heart_full': {'archive': 'UI/Tex/Icon/Sample.bntx.zs',
                                     'texture': 'Sample', 'crop': None,
                                     'verified': True},
                      'heart_empty': {'archive': None, 'texture': None,
                                      'crop': None, 'verified': False}}}))
        self.cache = SaveAssetCache(self.manifest, self.romfs, root, root/'cache', decoder_factory=FakeDecoder)
        FakeDecoder.fails = False
        FakeDecoder.calls = 0

    def test_atomic_generate_and_rgba(self):
        steps = []
        r = self.cache.generate(lambda n, total, role: steps.append((n,total,role)))
        self.assertTrue(r.ready)
        self.assertTrue(r.rebuilt)
        self.assertEqual(steps, [(1,1,'heart_full')])
        self.assertEqual(Image.open(r.directory/'heart_full.png').getpixel((0,0)), (20,30,40,77))
        self.assertIsNone(self.cache.icon_path('heart_empty'))
        self.assertTrue(self.cache.current().ready)
        self.assertFalse(self.cache.generate().rebuilt)
        self.assertEqual(FakeDecoder.calls,1)

    def test_error_keeps_fallback_and_no_partial_cache(self):
        FakeDecoder.fails = True
        result = self.cache.generate()
        self.assertFalse(result.ready)
        self.assertIn('Invalid BNTX', result.error)
        self.assertIsNone(self.cache.icon_path('heart_full'))
        self.assertFalse(list((self.cache.root).glob('.building-*')))

    def test_manifest_changes_invalidate(self):
        first = self.cache.generate()
        self.assertTrue(first.ready)
        data = json.loads(self.manifest.read_text())
        data['icons']['heart_full']['notes'] = 'another reviewed selection'
        self.manifest.write_text(json.dumps(data))
        self.assertFalse(self.cache.current().ready)
        self.assertNotEqual(first.directory, self.cache.generate().directory)


    def test_partial_cache_uses_good_icons_and_records_faults(self):
        doc = json.loads(self.manifest.read_text())
        doc['icons']['stone_goron'] = {'archive':'UI/Tex/Icon/Missing.bntx.zs',
                                       'texture':'Broken', 'crop':None, 'verified':True}
        self.manifest.write_text(json.dumps(doc))
        class SelectiveDecoder:
            def __init__(self, *_): pass
            def mapped_image(self, entry):
                if entry['texture'] == 'Broken':
                    raise RuntimeError('Unsupported test texture')
                return Image.new('RGBA',(5,4),(1,2,3,84))
        self.cache.decoder_factory = SelectiveDecoder
        steps=[]
        result=self.cache.generate(lambda i,n,role: steps.append((i,n,role)))
        self.assertTrue(result.ready)
        self.assertEqual(result.roles, ('heart_full',))
        self.assertEqual(len(result.failed),1)
        self.assertEqual(result.failed[0][0],'stone_goron')
        self.assertIn('Unsupported test texture',result.error)
        self.assertTrue(self.cache.icon_path('heart_full').is_file())
        self.assertIsNone(self.cache.icon_path('stone_goron'))
        self.assertEqual(len(steps),2)
        data=json.loads((result.directory/'index.json').read_text())
        self.assertIn('stone_goron',data['failures'])
        self.assertEqual(set(data['requested_roles']),{'heart_full','stone_goron'})
        self.assertTrue(self.cache.current().ready)

    def test_explicit_retry_recovers_failed_role_without_changing_manifest(self):
        doc=json.loads(self.manifest.read_text())
        doc['icons']['bottle_fairy']={'archive':'UI/Tex/Icon/Sample.bntx.zs',
                                     'texture':'Bad', 'crop':None, 'verified':True}
        self.manifest.write_text(json.dumps(doc))
        class RetryDecoder:
            fails=True
            def __init__(self,*_): pass
            def mapped_image(self,entry):
                if entry['texture']=='Bad' and type(self).fails:
                    raise RuntimeError('temporarily unsupported')
                return Image.new('RGBA',(4,4),(2,3,4,140))
        self.cache.decoder_factory=RetryDecoder
        partial=self.cache.generate()
        self.assertEqual(len(partial.failed),1)
        RetryDecoder.fails=False
        self.assertFalse(self.cache.generate().rebuilt, 'cached failure is not retried automatically')
        complete=self.cache.generate(force=True)
        self.assertTrue(complete.ready)
        self.assertTrue(complete.rebuilt)
        self.assertFalse(complete.failed)
        self.assertEqual(len(complete.roles),2)
        self.assertEqual(complete.directory,partial.directory)
        self.assertTrue(self.cache.icon_path('bottle_fairy').is_file())

    def test_vanilla_label_until_future_rando_tagging(self):
        from adventure_save_display import save_kind, save_kind_badge_text
        self.assertEqual(save_kind_badge_text({}), 'Vanilla Save')
        self.assertEqual(save_kind_badge_text(None), 'Vanilla Save')
        self.assertEqual(save_kind({'kind':'randomizer-planned'}),'Vanilla')
        self.assertEqual(save_kind_badge_text({'is_rando':False}), 'Vanilla Save')
        self.assertEqual(save_kind_badge_text({'metadata':{'is_randomizer':True}}), 'Vanilla Save')

    def test_decoder_systemexit_is_reported_instead_of_aborting_progress(self):
        doc=json.loads(self.manifest.read_text())
        doc['icons']['bottle_fairy']={'archive':'UI/Tex/Icon/Sample.bntx.zs',
                                     'texture':'BadDictionary', 'crop':None, 'verified':True}
        self.manifest.write_text(json.dumps(doc))
        class Decoder:
            def __init__(self,*_): pass
            def mapped_image(self,entry):
                if entry['texture']=='BadDictionary':
                    raise SystemExit('zs.zsdic not found')
                return Image.new('RGBA',(4,4),(1,2,3,4))
        self.cache.decoder_factory=Decoder
        result=self.cache.generate()
        self.assertTrue(result.ready)
        self.assertEqual(len(result.failed),1)
        self.assertIn('zs.zsdic not found',result.error)
        self.assertTrue((result.directory/'heart_full.png').is_file())

    def test_cache_schema_includes_dependency_versions_for_rebuild(self):
        self.assertEqual(self.cache._snapshot()[0],self.cache._snapshot()[0])
        first=self.cache.generate()
        self.assertTrue(first.ready)
        index=json.loads((first.directory/'index.json').read_text())
        self.assertEqual(index['cache_schema'],2)
        self.assertIn('requested_roles',index)
        self.assertIn('failures',index)

    def test_launcher_is_wired_not_only_helpers(self):
        src = QT.read_text()
        ast.parse(src)
        for method in ('prepare_adventure_save_assets','toggle_header_settings','current_diagnostics',
                       'make_launch_page','make_adventure_detail_page'):
            self.assertIn('def '+method+'(', src)
        self.assertIn('activate_game_cache(state.directory)',src)
        self.assertIn('ensure_save_assets(self, cache',src)
        self.assertIn('self.rebuild_save_assets_button',src)
        self.assertIn("self.tweaks_sections.addTab(self.tool_page, 'Shader / Pipeline Tools')",src)
        self.assertIn('make_grey_save_tag({}, card)',src)
        self.assertIn('style_selected_save_info(self.detail_save_info_frame',src)
        self.assertIn('diagnostic_from_code(',src)
        self.assertNotIn("self.launch_tabs.addTab(runtime, 'Runtime')",src)
        self.assertIn('_active_cache', ASSETS.read_text())
        self.assertNotIn('mapped_icon_path', ASSETS.read_text())


if __name__ == '__main__':
    unittest.main()
