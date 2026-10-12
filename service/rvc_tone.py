"""Public adapter: offline base audio through the standard RVC pitch/formant path."""
import importlib
from types import SimpleNamespace
from unittest.mock import patch


class ToneEngine:
    def __init__(self, config, previous=None):
        import torch
        if not torch.cuda.is_available():
            raise ValueError('此声音处理需要 NVIDIA 显卡及 CUDA 驱动')
        # gui_v1 imports rtrvc, not the older tools/rvc_for_realtime implementation.
        # Its Manager is only used by parallel Harvest. Serial Harvest needs no
        # queues or child console; no original source/configuration is rewritten.
        with patch('multiprocessing.Manager', return_value=None):
            module = importlib.import_module('infer.lib.rtrvc')
        device = config['device']
        if device == 'auto':
            device = 'cuda:0' if torch.cuda.is_available() else 'cpu'
        if device.startswith('cuda') and not torch.cuda.is_available():
            raise ValueError('CUDA 当前不可用，请选择自动或 CPU')
        half = device.startswith('cuda')
        if half:
            name = torch.cuda.get_device_name(0).upper()
            half = not any(t in name for t in ('P40','P10','1060','1070','1080')) and not ('16' in name and 'V100' not in name)
        options = SimpleNamespace(device=torch.device(device), is_half=half, use_jit=False, dml=False)
        last = previous.rvc if previous and previous.rvc.device == options.device else None
        rate = config['index_rate'] if config['index'] else 0
        from rvc_index import read_index
        with patch('faiss.read_index', side_effect=read_index):
            self.rvc = module.RVC(config['pitch'], config['formant'], config['model'],
                config['index'], rate, 1, None, None, options, last)
        if not all(getattr(self.rvc, key, None) is not None for key in ('net_g','model','tgt_sr')):
            raise ValueError('原 RVC 调音模型加载失败，请检查模型文件和服务日志')

    def convert(self, source, config, output, check):
        import librosa
        import numpy as np
        import soundfile as sf
        import torch
        from infer.lib.audio import load_audio
        rvc = self.rvc
        rvc.change_key(config['pitch'])
        rvc.change_formant(config['formant'])
        audio = load_audio(str(source), 16000)
        if not len(audio) or not np.isfinite(audio).all():
            raise ValueError('底音为空或包含无效采样')
        if len(audio) > 16000 * 600:
            raise ValueError('单次底音最长为 10 分钟，请分段输出')
        peak = np.abs(audio).max()
        if peak > .95:
            audio = audio * (.95 / peak)
        # Bounded offline windows supply the microphone's input buffer. Context
        # is removed after inference, keeping the first/last word and duration.
        frame = 160
        length = len(audio)
        aligned = int(np.ceil(length / frame)) * frame
        audio = np.pad(audio, (0, aligned - length))
        context, block, overlap = 8000, 96000, 640
        padded = np.pad(audio, (context, context), mode='reflect' if length > 1 else 'edge')
        rate = int(rvc.tgt_sr)
        upp = rate // 100
        assembled = np.zeros(aligned // frame * upp, dtype=np.float32)
        written = 0
        for start in range(0, aligned, block):
            check()
            begin = max(0, start - overlap)
            end = min(aligned, start + block)
            window = padded[begin:end + 2 * context]
            tensor = torch.from_numpy(window.astype(np.float32)).to(rvc.device)
            frames = len(window) // frame
            # Original caches are only 1024 frames and retain prior microphone
            # input. Reset per offline window so separate sentences never leak.
            rvc.cache_pitch = torch.zeros(max(1024, frames), device=rvc.device, dtype=torch.long)
            rvc.cache_pitchf = torch.zeros(max(1024, frames), device=rvc.device, dtype=torch.float32)
            count = (end - begin) // frame
            result = rvc.infer(tensor, len(window), context // frame, count, config['f0_method'])
            samples = result.detach().cpu().numpy().astype(np.float32)
            needed = count * upp
            if len(samples) < needed or not np.isfinite(samples).all():
                raise RuntimeError('原 RVC 调音输出长度不足或采样无效')
            samples = samples[:needed]
            offset = begin // frame * upp
            cross = max(0, written - offset)
            if cross:
                fade = np.sin(.5 * np.pi * np.linspace(0, 1, cross)) ** 2
                assembled[offset:written] = assembled[offset:written] * (1 - fade) + samples[:cross] * fade
            assembled[offset + cross:offset + needed] = samples[cross:]
            written = offset + needed
        assembled = assembled[:round(length * rate / 16000)]
        # Same RMS envelope blend as the original GUI, applied to offline output.
        if config['rms_mix_rate'] < 1:
            base = librosa.resample(audio[:length], orig_sr=16000, target_sr=rate)
            def envelope(data):
                rms = librosa.feature.rms(y=data, frame_length=4*upp, hop_length=upp)[0]
                return np.interp(np.arange(len(assembled)), np.linspace(0,len(assembled)-1,len(rms)), rms)
            gain = (envelope(base) / np.maximum(envelope(assembled), 1e-3)) ** (1 - config['rms_mix_rate'])
            assembled *= gain
        check()
        sf.write(str(output), np.clip(assembled, -1, 1), rate, subtype='PCM_16')
        return rate, len(assembled) / rate
