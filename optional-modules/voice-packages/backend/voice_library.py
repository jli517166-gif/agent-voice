"""Connect optional package imports to the host's ordinary saved voices."""
import hashlib
import json
from pathlib import Path
from operation_lock import OperationLock
from state import Store
from file_integrity import digest
from voice_profiles import save_profile, apply_profile, model_inventory


def register_current(root, name):
    """Save and apply the current voice without changing its sound parameters.

    Identity uses content, not archive names or extraction paths, so importing
    the same sound again reuses its saved voice, including a user's rename.
    """
    with OperationLock(root):
        return _register_current(root, name)


def _register_current(root, name):
    store = Store(root)
    voice = store.get()['voice']
    _, fingerprint = model_inventory(store, voice)
    identity = {k: v for k, v in voice.items()
                if k not in ('reference', 'baseModel', 'gptModel')}
    identity.update(referenceHash=digest(store.resolve(voice['reference'])),
                    modelFingerprint=fingerprint)
    ident = 'imported-' + hashlib.sha256(json.dumps(
        identity, sort_keys=True, ensure_ascii=False).encode()).hexdigest()[:32]
    existing = next((p for p in store.get()['profiles'] if p['id'] == ident), None)
    saved = save_profile(store, existing['name'] if existing else name, voice, ident)
    apply_profile(store, ident)
    return saved['profile']

def import_and_register(root, file):
    from tts_bundle import _import_model
    with OperationLock(root):
        result = _import_model(root, file)
        try:
            result['profile'] = _register_current(root, result['name'][:80])
            result['voice'] = result['profile']['voice']
        except Exception as error:
            raise ValueError('音色已导入，但保存到声音方案失败。当前声音仍可使用；'
                             '请在模块中点击“保存当前声音”重试：' + str(error)) from error
        return result


def apply_saved(root, ident):
    with OperationLock(root):
        return apply_profile(Store(Path(root)), ident)
