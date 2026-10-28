from pathlib import Path
import unittest


class DownloadUiContractTests(unittest.TestCase):
    def test_fractional_download_speed_is_not_discarded(self):
        source = (Path(__file__).resolve().parents[1] / 'ui' / 'DownloadJob.cs').read_text(encoding='utf-8-sig')
        self.assertIn('double speed=DecimalNumber(data,"bytesPerSecond")', source)
        self.assertIn('CultureInfo.InvariantCulture', source)
        self.assertIn('Bytes((long)speed)+"/s"', source)


if __name__ == '__main__':
    unittest.main()
