"""Keep generated audio unchanged and preserve existing container provenance."""
from aigc import read_wav,read_wav_bytes,label_wav,label_wav_bytes,label_mp3,metadata_for_join

ADD_GENERATION_METADATA=False


def mark_synthesized_wav(path):
    if ADD_GENERATION_METADATA:label_wav(path)


def mark_joined_wav(path,parts):
    if ADD_GENERATION_METADATA or any(read_wav(part) is not None for part in parts):
        label_wav(path,metadata_for_join(parts))


def wav_response(audio):
    return audio if not ADD_GENERATION_METADATA else label_wav_bytes(audio)


def mp3_response(audio,source_wav):
    source=read_wav_bytes(source_wav)
    if source is not None:return label_mp3(audio,source)
    if not ADD_GENERATION_METADATA:return audio
    from aigc import metadata
    return label_mp3(audio,metadata())
