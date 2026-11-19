import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / 'scripts'))
from headless_process import run_headless


class HeadlessProcessTests(unittest.TestCase):
    def test_normal_and_timeout_terminates_descendant(self):
        result = run_headless([sys.executable, '-c', 'print("ok")'], env=os.environ.copy(), timeout=5)
        self.assertEqual((result.returncode, result.stdout.strip()), (0, 'ok'))
        with tempfile.TemporaryDirectory() as directory:
            marker = str(Path(directory) / 'terminated')
            child = ('import os,signal,sys,time; '
                     'signal.signal(signal.SIGTERM,lambda *_:(open(sys.argv[1],"w").write("yes"),os._exit(0))); '
                     'print("ready",flush=True); time.sleep(60)')
            parent = ('import subprocess,sys,time; '
                      'p=subprocess.Popen([sys.executable,"-c",sys.argv[1],sys.argv[2]],stdout=subprocess.PIPE,text=True); '
                      'print(p.stdout.readline(),flush=True); time.sleep(60)')
            with self.assertRaises(subprocess.TimeoutExpired):
                run_headless([sys.executable, '-c', parent, child, marker], env=os.environ.copy(), timeout=2)
            self.assertEqual(Path(marker).read_text(), 'yes')


if __name__ == '__main__':
    unittest.main()
