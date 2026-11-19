"""Run an isolated headless diagnostic and reap its process group on timeout."""
import os
import signal
import subprocess


def run_headless(command, *, env, timeout):
    if os.name != 'posix':
        raise RuntimeError('Headless process-group cleanup requires a POSIX system (macOS/Linux).')
    process = subprocess.Popen(command, env=env, stdout=subprocess.PIPE, stderr=subprocess.PIPE,
                               text=True, start_new_session=True)
    try:
        stdout, stderr = process.communicate(timeout=timeout)
    except BaseException as error:
        try:
            os.killpg(process.pid, signal.SIGTERM)
        except ProcessLookupError:
            pass
        try:
            stdout, stderr = process.communicate(timeout=5)
        except subprocess.TimeoutExpired:
            try:
                os.killpg(process.pid, signal.SIGKILL)
            except ProcessLookupError:
                pass
            stdout, stderr = process.communicate()
        # A descendant may have closed its pipes yet ignored SIGTERM.
        try:
            os.killpg(process.pid, signal.SIGKILL)
        except ProcessLookupError:
            pass
        if isinstance(error, subprocess.TimeoutExpired):
            raise subprocess.TimeoutExpired(command, timeout, output=stdout, stderr=stderr) from error
        raise
    return subprocess.CompletedProcess(command, process.returncode, stdout, stderr)
