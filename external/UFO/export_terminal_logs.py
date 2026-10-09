"""Export snapshots of both PowerShell transcript files into one ZIP.
Run from anywhere: python export_terminal_logs.py --folder C:\\...\\terminal-logs
"""
import argparse
import datetime as dt
import json
import os
from pathlib import Path
import time
import zipfile

NAMES = ('terminal-1-ufo.txt', 'terminal-2-powershell.txt')


def read_snapshot(path):
    # A normal read often works while PowerShell still writes the transcript.
    last_error = None
    for attempt in range(3):
        try:
            with path.open('rb') as f:
                return f.read(), None
        except OSError as exc:
            last_error = f'{type(exc).__name__}: {exc}'
            time.sleep(0.2 * (attempt + 1))
    return None, last_error


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--folder', default=str(Path(__file__).resolve().parent / 'terminal-logs'))
    parser.add_argument('--out', default=None)
    args = parser.parse_args()
    folder = Path(args.folder).expanduser().resolve()
    stamp = dt.datetime.now().strftime('%Y%m%d-%H%M%S')
    output = Path(args.out).expanduser().resolve() if args.out else folder.parent / f'terminal-logs-{stamp}.zip'
    if output.parent == folder:
        raise SystemExit('Please save ZIP outside the source folder.')
    manifest = {'created_at_local': dt.datetime.now().isoformat(timespec='seconds'),
                'source_folder': str(folder), 'files': {}}
    contents = {}
    for name in NAMES:
        path = folder / name
        if not path.is_file():
            manifest['files'][name] = {'status': 'missing'}
            continue
        data, error = read_snapshot(path)
        if data is None:
            manifest['files'][name] = {'status': 'unreadable', 'error': error}
            continue
        contents[name] = data
        manifest['files'][name] = {'status': 'captured', 'bytes': len(data),
                                    'snapshot_only': True}
    output.parent.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(output, 'w', compression=zipfile.ZIP_DEFLATED) as z:
        for name, data in contents.items():
            z.writestr(name, data)
        z.writestr('manifest.json', json.dumps(manifest, ensure_ascii=False, indent=2))
    print('ZIP:', output)
    for name, status in manifest['files'].items():
        print(f"{name}: {status['status']}" + (f" ({status['bytes']} bytes)" if 'bytes' in status else ''))
    if len(contents) != len(NAMES):
        print('WARNING: ZIP is incomplete. Check paths/locks; do not treat as complete evidence.')
        return 2
    print('BOTH LOG SNAPSHOTS CAPTURED. If processes are still running, rerun later for newer output.')
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
