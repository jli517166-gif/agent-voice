"""Keep Windows scanner delays out of assertions; persistent leaks still fail."""
import tempfile
import time


class TemporaryDirectory(tempfile.TemporaryDirectory):
    def cleanup(self):
        for attempt in range(6):
            try:
                return super().cleanup()
            except OSError as error:
                if getattr(error, 'winerror', None) not in (32, 145) or attempt == 5:
                    raise
                time.sleep(.1 * (attempt + 1))
