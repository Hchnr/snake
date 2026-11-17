#!/usr/bin/env python3
"""Create an integrity-checked snapshot of a running Snake SQLite database."""
import argparse
from contextlib import closing
import os
from pathlib import Path
import sqlite3
import sys
import tempfile
import time
from urllib.parse import quote


def backup(source: Path, destination: Path) -> None:
    source = source.resolve(strict=True)
    if not source.is_file():
        raise ValueError('Source must be an existing regular database file.')
    destination = destination.absolute()
    if not destination.parent.is_dir():
        raise ValueError('Destination directory must already exist.')
    if os.path.lexists(destination):
        raise FileExistsError('Destination already exists; refusing to overwrite it.')
    # An exclusive temporary file in the destination directory allows a final
    # atomic, no-overwrite hard link even if another backup wins the race.
    fd, name = tempfile.mkstemp(prefix='.snake-backup-', suffix='.tmp', dir=destination.parent)
    temporary = Path(name)
    os.close(fd)
    try:
        uri = 'file:' + quote(str(source), safe='/') + '?mode=ro'
        deadline = time.monotonic() + 30
        def progress(status, remaining, total):
            if time.monotonic() > deadline:
                raise TimeoutError('Backup did not finish within 30 seconds.')
        with closing(sqlite3.connect(uri, uri=True, timeout=5)) as src:
            with closing(sqlite3.connect(temporary, timeout=5)) as dst:
                src.backup(dst, pages=256, progress=progress, sleep=.1)
                # Make the published snapshot self-contained, with no WAL sidecars.
                dst.execute('PRAGMA journal_mode=DELETE')
                result = [row[0] for row in dst.execute('PRAGMA integrity_check')]
                if result != ['ok']:
                    raise ValueError('SQLite integrity check failed.')
                schema = dst.execute("SELECT value FROM metadata WHERE key='schema'").fetchone()
                tables = {row[0] for row in dst.execute("SELECT name FROM sqlite_master WHERE type='table'")}
                if not schema or schema[0] != '1' or not {'players', 'identity_links', 'runs', 'rewards', 'ads'}.issubset(tables):
                    raise ValueError('Snapshot is not a supported Snake database.')
        with open(temporary, 'rb') as data:
            os.fsync(data.fileno())
        os.link(temporary, destination)
    finally:
        temporary.unlink(missing_ok=True)
        for suffix in ('-wal', '-shm', '-journal'):
            Path(str(temporary) + suffix).unlink(missing_ok=True)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('source', type=Path, help='Existing SQLite database, including any live WAL')
    parser.add_argument('destination', type=Path, help='New snapshot file; must not already exist')
    args = parser.parse_args()
    try:
        backup(args.source, args.destination)
    except (OSError, sqlite3.Error, ValueError, TimeoutError) as error:
        print(f'Backup failed: {error}', file=sys.stderr)
        return 1
    print(f'Backup verified: {args.destination}')
    return 0


if __name__ == '__main__':
    sys.exit(main())
