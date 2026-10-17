"""The same soxr HQ playback conversion, without librosa's first-use JIT imports."""
import math

def resample(audio,source_rate,target_rate):
    if source_rate==target_rate:return audio
    import numpy as np
    import soxr
    if audio.ndim!=1:raise ValueError('TTS 播放音频必须为单声道')
    result=soxr.resample(audio,in_rate=source_rate,out_rate=target_rate,quality='soxr_hq')
    # Match librosa.resample defaults: ceil length, zero-pad/trim, preserve dtype.
    length=int(math.ceil(len(audio)*float(target_rate)/source_rate))
    if len(result)<length:result=np.pad(result,(0,length-len(result)))
    return result[:length].astype(audio.dtype,copy=False)
