"""Public interface cues must be distinct, short, quiet PCM, never TTS assets."""
import array,hashlib,sys,unittest,wave
from pathlib import Path

ROOT=Path(__file__).resolve().parents[1]

class InterfaceSoundAssets(unittest.TestCase):
    def test_nine_distinct_low_level_pcm_cues(self):
        fingerprints=set()
        for index in range(9):
            with self.subTest(step=index),wave.open(str(ROOT/f'assets/sfx/step-{index}.wav'),'rb') as clip:
                self.assertEqual((clip.getnchannels(),clip.getsampwidth(),clip.getframerate()),(1,2,32000))
                self.assertLessEqual(clip.getnframes()/clip.getframerate(),.8)
                self.assertGreater(clip.getnframes()/clip.getframerate(),.1)
                data=clip.readframes(clip.getnframes());fingerprints.add(hashlib.sha256(data).hexdigest());samples=array.array('h',data)
                if sys.byteorder!='little':samples.byteswap()
                peak=max(abs(s) for s in samples)/32768
                self.assertGreater(peak,.02);self.assertLessEqual(peak,.131)
                self.assertLess(max(abs(s) for s in samples[-128:])/32768,.005)
        self.assertEqual(len(fingerprints),9)

if __name__=='__main__':unittest.main()
