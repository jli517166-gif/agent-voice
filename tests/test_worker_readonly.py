"""Exercise the worker's real upstream initialization guard without loading GPU weights."""
import ast
from pathlib import Path
import sys
import tempfile
import types
import unittest
from unittest.mock import patch


class WorkerReadOnlyTests(unittest.TestCase):
    def test_gpt_initialization_does_not_save_transient_upstream_configuration(self):
        source = Path(__file__).resolve().parents[1] / 'service/local_tts_worker.py'
        tree = ast.parse(source.read_text(encoding='utf-8'))
        node = next(n for n in ast.walk(tree) if isinstance(n, ast.With)
                    and any(isinstance(c, ast.Assign) and any(isinstance(t, ast.Name) and t.id == 'model' for t in c.targets) for c in n.body))
        with tempfile.TemporaryDirectory() as tmp:
            target = Path(tmp) / 'upstream-config.json'
            class Config:
                def __init__(self, value):
                    self.value = value
                def save_configs(self):
                    target.write_text('transient paths')
            def tts(config):
                config.save_configs()
                return object()
            namespace = {'patch': patch, 'torch': types.SimpleNamespace(load=lambda *a: None),
                         'selected_load': lambda *a: None, 'TTS_Config': Config, 'TTS': tts,
                         'home': Path(tmp), 'variant': 'v5turbo', 'acoustic': 'voice.pth'}
            exec(compile(ast.Module(body=[node], type_ignores=[]), str(source), 'exec'), namespace)
            self.assertFalse(target.exists(), 'upstream initialization rewrote its configuration')
