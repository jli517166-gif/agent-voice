import json
import sys
import tempfile
import unittest
from support_temp import TemporaryDirectory
from pathlib import Path
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'service'))
from state import Store
from resource_plan import resource_plan


class ResourcePlanTests(unittest.TestCase):
    def test_empty_directories_are_missing_and_show_pinned_sources_and_targets(self):
        with TemporaryDirectory() as tmp:
            store = Store(tmp)
            cfg = store.get()
            store.resolve(cfg['engines']['cosyModel']).mkdir(parents=True)
            with patch('urllib.request.urlopen', side_effect=AssertionError('inspection must be offline')):
                plan = resource_plan(store, 'cosyvoice')
            self.assertFalse(plan['resourcesReady'])
            row = plan['items'][0]
            self.assertIn('llm.pt', row['missingModelFiles'])
            self.assertIn('/resolve/', row['modelUrl'])
            self.assertEqual(len(row['modelRevision']), 40)
            self.assertTrue(Path(row['model']).is_relative_to(store.root))
            self.assertIn('sourceMissing', row)

    def test_complete_resources_are_ready_without_runtime_imports(self):
        with TemporaryDirectory() as tmp:
            store = Store(tmp)
            row = resource_plan(store, 'cosyvoice')['items'][0]
            for relative in row['missingModelFiles']:
                relative = relative.replace('*', 'weights.safetensors')
                path = Path(row['model']) / relative
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_bytes(b'fixture')
            for relative in ('cosyvoice/cli/cosyvoice.py', 'third_party/Matcha-TTS/matcha/utils/audio.py'):
                path = Path(row['source']) / relative
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_bytes(b'fixture')
            self.assertTrue(resource_plan(store, 'cosyvoice')['resourcesReady'])

    def test_external_model_is_never_a_download_destination(self):
        with TemporaryDirectory() as tmp, TemporaryDirectory() as external:
            store = Store(tmp)
            store.update({'engines': {'cosyModel': external}})
            row = resource_plan(store, 'cosyvoice')['items'][0]
            self.assertEqual(row['configuredModel'], external)
            self.assertNotEqual(row['model'], external)
            self.assertTrue(Path(row['model']).is_relative_to(store.root))

    def test_all_has_two_families_and_unknown_engine_is_rejected(self):
        with TemporaryDirectory() as tmp:
            store = Store(tmp)
            self.assertEqual([r['id'] for r in resource_plan(store, 'all')['items']], ['cosyvoice', 'gpt-sovits'])
            with self.assertRaises(ValueError):
                resource_plan(store, 'unknown')

    def test_cosy_tokenizer_assets_are_required_not_only_weights(self):
        with TemporaryDirectory() as tmp:
            row = resource_plan(Store(tmp), 'cosyvoice')['items'][0]
            self.assertTrue({'CosyVoice-BlankEN/tokenizer_config.json', 'CosyVoice-BlankEN/merges.txt', 'CosyVoice-BlankEN/vocab.json'} <= set(row['missingModelFiles']))
