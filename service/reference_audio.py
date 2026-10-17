"""Check GPT reference conditions before loading a large GPU model."""
def validate_gpt_reference(path,ffmpeg=None):
    import numpy as np
    import soundfile as sf
    try:
        info=sf.info(path)
        if not 3<=info.duration<=10:raise ValueError('GPT 参考录音必须为 3～10 秒，请选择完整短句；当前 %.2f 秒'%info.duration)
        samples,_=sf.read(path,dtype='float32')
    except (RuntimeError,sf.LibsndfileError):
        # Some libsndfile builds cannot decode M4A. Upstream accepts it via FFmpeg.
        import subprocess
        from pathlib import Path
        from pipeline import hidden_process
        if not ffmpeg or not Path(ffmpeg).is_file():raise ValueError('参考音频无法读取，请转换为 WAV 或配置 FFmpeg')
        result=subprocess.run([str(ffmpeg),'-v','error','-i',str(path),'-t','10.1','-ar','16000','-ac','1','-f','f32le','pipe:1'],capture_output=True,timeout=30,**hidden_process())
        if result.returncode:raise ValueError('参考音频解码失败，请使用可正常播放的 WAV 文件')
        samples=np.frombuffer(result.stdout,dtype='<f4')
        if not 3<=len(samples)/16000<=10:raise ValueError('GPT 参考录音必须为 3～10 秒完整短句')
    if not len(samples) or not np.isfinite(samples).all() or float(np.sqrt(np.mean(samples*samples)))<.0001:
        raise ValueError('参考录音是静音或含无效样本，请更换清晰的短句录音')
