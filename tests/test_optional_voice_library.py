import optional_voice_module
import copy
import unittest
import test_tts_bundle as bundle_tests
from state import Store
from voice_library import import_and_register, register_current, apply_saved


class OptionalVoiceLibraryTests(unittest.TestCase):
    setUp = bundle_tests.TtsBundleTests.setUp
    tearDown = bundle_tests.TtsBundleTests.tearDown

    def test_import_is_visible_applied_and_survives_restart(self):
        result = import_and_register(self.root, self.package)
        config = Store(self.root).get()
        self.assertEqual(len(config['profiles']), 1)
        self.assertEqual(config['profiles'][0], result['profile'])
        self.assertEqual(config['voice'], config['profiles'][0]['voice'])
        self.assertTrue(self.store.resolve(config['voice']['reference']).is_file())
        for key in ('engines', 'token', 'harness', 'appearance', 'phrases'):
            self.assertEqual(config[key], self.before[key])

    def test_repeat_import_reuses_profile_and_preserves_user_rename(self):
        first = import_and_register(self.root, self.package)['profile']
        renamed = copy.deepcopy(first)
        renamed['name'] = '自己取的名称'
        self.store.update({'profiles': [renamed]})
        second = import_and_register(self.root, self.package)['profile']
        self.assertEqual(second['id'], first['id'])
        self.assertEqual(second['name'], renamed['name'])
        self.assertEqual(len(Store(self.root).get()['profiles']), 1)

    def test_register_repairs_existing_import_without_changing_audio_parameters(self):
        from tts_bundle import import_model
        import_model(self.root, self.package)
        old = Store(self.root).get()['voice']
        saved = register_current(self.root, '已导入的声音')
        now = Store(self.root).get()['voice']
        self.assertEqual({k:v for k,v in old.items() if k != 'reference'},
                         {k:v for k,v in now.items() if k != 'reference'})
        self.assertEqual(self.store.resolve(old['reference']).read_bytes(),
                         self.store.resolve(now['reference']).read_bytes())
        apply_saved(self.root, saved['id'])
        self.assertEqual(Store(self.root).get()['voice'], saved['voice'])

    def test_apply_rejects_damaged_reference(self):
        saved = import_and_register(self.root, self.package)['profile']
        self.store.resolve(saved['voice']['reference']).write_bytes(b'damaged')
        with self.assertRaisesRegex(ValueError, '损坏'):
            apply_saved(self.root, saved['id'])

    def test_missing_reference_does_not_create_a_profile(self):
        self.store.update({'voice': {'reference': 'missing.wav'}})
        with self.assertRaises(FileNotFoundError):
            register_current(self.root, '不能保存')
        self.assertEqual(Store(self.root).get()['profiles'], [])


if __name__ == '__main__':
    unittest.main()
