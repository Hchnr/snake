import pathlib
import struct
import unittest
import wave


class MusicAssetsTest(unittest.TestCase):
    def test_small_distinct_mono_loops_with_quiet_boundaries(self):
        root = pathlib.Path(__file__).resolve().parents[2] / 'unity/Assets/Resources/Music'
        data = []
        for name in ('Home.wav', 'Battle.wav'):
            path = root / name
            self.assertLess(path.stat().st_size, 400_000)
            with wave.open(str(path), 'rb') as track:
                self.assertEqual((track.getnchannels(), track.getsampwidth(), track.getframerate(), track.getnframes()), (1, 2, 22050, 176400))
                raw = track.readframes(track.getnframes())
            samples = struct.unpack('<' + 'h' * (len(raw) // 2), raw)
            self.assertEqual(samples[0], 0)
            self.assertLess(abs(samples[-1]), 328)
            self.assertGreater(max(abs(sample) for sample in samples), 1600)
            self.assertLess(max(abs(sample) for sample in samples), 13107)
            for note in range(1, 32):
                boundary = (note * 22050 + 3) // 4
                transient = abs(samples[boundary] - 2 * samples[boundary - 1] + samples[boundary - 2])
                self.assertLess(transient, 14, f'{name}: abrupt transition at note {note}')
            data.append(raw)
        self.assertNotEqual(data[0], data[1])


if __name__ == '__main__':
    unittest.main()
