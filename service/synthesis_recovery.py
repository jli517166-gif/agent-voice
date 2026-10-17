"""Bounded recovery for transient inference faults, never for missing installation files."""
def retryable(error):
    from jobs import Cancelled
    if isinstance(error,(Cancelled,ValueError,PermissionError)):return False
    message=str(error).lower()
    if any(x in message for x in ('not found','文件缺失','资源缺失','no module','不存在','参考录音','端口','fingerprint','permission denied')):return False
    return isinstance(error,(ConnectionError,TimeoutError)) or any(x in message for x in (
        'out of memory','cuda error','进程已退出','推理超时','timed out','connection reset',
        'connection aborted','远程主机','no audio','silent audio','invalid audio samples','invalid audio duration',
        '静音或无效音频','没有生成音频','音频样本无效','empty audio',
        # A sampled empty/too-short speech-token sequence can reach the
        # vocoder before audio validation. Retry the same voice with a new seed.
        'calculated padded input size per channel'))

def validate_audio(path):
    import numpy as np
    import soundfile as sf
    samples,rate=sf.read(path,dtype='float32')
    if samples.ndim!=1 or rate<8000 or rate>48000 or not len(samples) or not np.isfinite(samples).all():
        raise RuntimeError('模型返回 invalid audio samples')
    if float(np.sqrt(np.mean(samples*samples)))<.0001:
        raise RuntimeError('模型返回 silent audio')
    if len(samples)/rate<.08:raise RuntimeError('模型返回 invalid audio duration')
    from audio_limits import MAX_AUDIO_SECONDS
    if len(samples)/rate>MAX_AUDIO_SECONDS:raise ValueError('生成音频超过 30 分钟，请拆分文本')
    return rate,len(samples)/rate
