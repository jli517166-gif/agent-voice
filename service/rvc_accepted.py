"""Compatibility adapter preserving the accepted offline VC call and parameters.

Contains no personal model, reference or hard-coded private installation path.
The standard pitch/formant adapter remains independent.
"""
import os
from pathlib import Path
import sys


class AcceptedEngine:
    def __init__(self, config, previous=None):
        from unittest.mock import patch
        from configs.config import Config
        from infer.modules.vc.modules import VC
        model = Path(config['model'])
        os.environ['weight_root'] = str(model.parent)
        # Config parses command-line switches belonging to the launcher.
        with patch.object(sys, 'argv', sys.argv[:1]):
            options = Config()
        self.vc = VC(options)
        self.vc.get_vc(model.name)
        if getattr(self.vc, 'net_g', None) is None:
            raise ValueError('认可版模型加载失败')

    def convert(self, source, config, output, check):
        import numpy as np
        import soundfile as sf
        check()
        info, (rate, audio) = self.vc.vc_single(
            0, str(source), config['pitch'], None, config['f0_method'],
            config['index'], '', config['index_rate'], 3, 0,
            config['rms_mix_rate'], config['protect'])
        if 'Success' not in info or audio is None or not np.isfinite(audio).all():
            raise RuntimeError('认可版兼容转换失败：' + info)
        check()
        sf.write(str(output), audio, rate)
        return int(rate), len(audio) / rate
