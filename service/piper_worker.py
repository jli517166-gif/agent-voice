"""Piper's own environment; stdout is reserved for local JSON responses."""
import argparse
import contextlib
import hashlib
import json
import os
from pathlib import Path
import shutil
import sys
import tempfile
import uuid
import wave


def reply(value):
    sys.stdout.write(json.dumps(value, ensure_ascii=False) + '\n')
    sys.stdout.flush()


def phoneme_data(source):
    """espeak's Windows native file API cannot open non-ASCII data paths."""
    source = Path(source)
    if os.name != 'nt' or str(source).isascii():
        return source
    import ctypes
    buffer = ctypes.create_unicode_buffer(32768)
    if ctypes.windll.kernel32.GetShortPathNameW(str(source), buffer, len(buffer)) and buffer.value.isascii():
        return Path(buffer.value)
    # Preserve the installed wheel. Only its small pronunciation data is cached;
    # voice weights and the Python environment remain in the selected engine.
    cache = Path(tempfile.gettempdir()) / 'agent-voice-piper-data'
    if not str(cache).isascii():
        raise ValueError('Piper 发音库需要英文缓存路径，请设置纯英文的临时目录后重试')
    revision = hashlib.sha256()
    for file in sorted(source.rglob('*')):
        if file.is_file():
            revision.update(str(file.relative_to(source)).encode('utf-8'))
            revision.update(str(file.stat().st_size).encode())
            revision.update(str(file.stat().st_mtime_ns).encode())
    target = cache / revision.hexdigest()[:20]
    if not (target / 'phontab').is_file():
        cache.mkdir(parents=True, exist_ok=True)
        staging = cache / ('.prepare-' + uuid.uuid4().hex)
        try:
            shutil.copytree(source, staging)
            try: staging.rename(target)
            except FileExistsError:
                if not (target / 'phontab').is_file(): raise
        finally:
            if staging.exists():
                if staging.resolve().parent != cache.resolve():
                    raise ValueError('Piper 缓存清理路径异常')
                shutil.rmtree(staging)
    return target


def main():
    sys.stdin.reconfigure(encoding='utf-8')
    sys.stdout.reconfigure(encoding='utf-8')
    parser = argparse.ArgumentParser()
    parser.add_argument('--model', required=True)
    args = parser.parse_args()
    try:
        with contextlib.redirect_stdout(sys.stderr):
            from piper import PiperVoice, SynthesisConfig
            from piper.phonemize_espeak import ESPEAK_DATA_DIR
            voice = PiperVoice.load(args.model, use_cuda=False, espeak_data_dir=phoneme_data(ESPEAK_DATA_DIR))
        reply({'ready': True})
    except Exception as error:
        reply({'ready': False, 'error': str(error)[:2000]})
        return 1
    for line in sys.stdin:
        try:
            value = json.loads(line)
            speed = value['speed']
            if isinstance(speed, bool) or not isinstance(speed, (int, float)) or not .7 <= speed <= 1.3:
                raise ValueError('语速超出范围')
            with contextlib.redirect_stdout(sys.stderr):
                with wave.open(value['output'], 'wb') as audio:
                    voice.synthesize_wav(value['text'], audio, syn_config=SynthesisConfig(length_scale=1 / speed))
            reply({'ok': True})
        except Exception as error:
            reply({'ok': False, 'error': str(error)[:2000]})
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
