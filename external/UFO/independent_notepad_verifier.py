"""Independent, read-only Notepad benchmark observer for Windows.

Requires pywinauto in the Python environment. Run BEFORE launching the operator.
An initially open Notepad makes the run INCONCLUSIVE (non-clean baseline).
"""
import argparse
import ctypes
import json
import os
import sys
import time
from datetime import datetime, timezone
from pathlib import Path

from pywinauto import Desktop

EXPECTED_DEFAULT = 'PersonalAI benchmark text entry'
user32 = ctypes.WinDLL('user32', use_last_error=True)
user32.IsWindow.argtypes = [ctypes.c_void_p]
user32.IsWindow.restype = ctypes.c_bool
user32.GetWindowThreadProcessId.argtypes = [ctypes.c_void_p, ctypes.POINTER(ctypes.c_ulong)]
user32.GetWindowThreadProcessId.restype = ctypes.c_ulong


def timestamp():
    return datetime.now(timezone.utc).isoformat()


def write_atomic(path, data):
    temp = path.with_suffix('.tmp')
    temp.write_text(json.dumps(data, indent=2, ensure_ascii=False), encoding='utf-8')
    os.replace(temp, path)


def notepad_windows():
    windows = []
    for w in Desktop(backend='uia').windows():
        try:
            if w.element_info.class_name == 'Notepad':
                windows.append(w)
        except Exception:
            pass
    return windows


def identity(w):
    return {'pid': int(w.element_info.process_id), 'hwnd': int(w.handle), 'title': w.window_text()}


def same_window_exists(target):
    hwnd = target['hwnd']
    if not user32.IsWindow(ctypes.c_void_p(hwnd)):
        return False
    pid = ctypes.c_ulong()
    user32.GetWindowThreadProcessId(ctypes.c_void_p(hwnd), ctypes.byref(pid))
    return pid.value == target['pid']


def read_document(w):
    records = []
    for control in w.descendants():
        try:
            kind = control.element_info.control_type
        except Exception:
            continue
        if kind not in ('Edit', 'Document'):
            continue
        for name in ('ValuePattern', 'TextPattern'):
            try:
                obj = control.iface_value if name == 'ValuePattern' else control.iface_text
                if obj is None:
                    continue
                value = obj.CurrentValue if name == 'ValuePattern' else obj.DocumentRange.GetText(-1)
                if isinstance(value, str):
                    records.append({'pattern': name, 'control_type': kind, 'value': value})
            except Exception:
                pass
    return records


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--expected', default=EXPECTED_DEFAULT)
    parser.add_argument('--seconds', type=int, default=180)
    parser.add_argument('--interval', type=float, default=0.5)
    parser.add_argument('--output', default='notepad-verification.json')
    args = parser.parse_args()
    if args.seconds <= 0 or args.interval <= 0:
        parser.error('seconds and interval must be positive')
    output = Path(args.output).resolve()
    output.parent.mkdir(parents=True, exist_ok=True)
    report = {
        'schema_version': 1, 'started_at': timestamp(), 'expected_text': args.expected,
        'baseline': [], 'target': None, 'first_exact_at': None,
        'exact_text_evidence': [], 'closure_observed_at': None,
        'last_observed_values': [], 'errors': [], 'verdict': 'INCONCLUSIVE',
        'reason': 'Monitoring has not finished',
        'save_behavior': 'NOT_VERIFIED',
    }
    try:
        baseline = notepad_windows()
        report['baseline'] = [identity(w) for w in baseline]
    except Exception as e:
        report['errors'].append(f'Baseline discovery: {type(e).__name__}: {e}')
        write_atomic(output, report)
        print('INCONCLUSIVE: cannot inspect baseline', flush=True)
        return 2

    if baseline:
        report['reason'] = 'Notepad already open at baseline; clean operator launch cannot be established'
        write_atomic(output, report)
        print('INCONCLUSIVE: close all Notepad windows, then restart observer', flush=True)
        return 2

    print(f'OBSERVING for up to {args.seconds}s; start UFO now. Output: {output}', flush=True)
    deadline = time.monotonic() + args.seconds
    target = None
    last_error = None
    while time.monotonic() < deadline:
        try:
            if target is None:
                candidates = notepad_windows()
                if len(candidates) == 1:
                    target = identity(candidates[0])
                    report['target'] = target
                    print(f'TARGET DETECTED: pid={target["pid"]} hwnd={target["hwnd"]}', flush=True)
                elif len(candidates) > 1:
                    report['reason'] = 'Multiple Notepad windows appeared; target ambiguous'
                    break
            if target is not None:
                if not same_window_exists(target):
                    report['closure_observed_at'] = timestamp()
                    print('WINDOW DISAPPEARED', flush=True)
                    break
                windows = [w for w in notepad_windows() if identity(w)['hwnd'] == target['hwnd']
                           and identity(w)['pid'] == target['pid']]
                if windows:
                    records = read_document(windows[0])
                    report['last_observed_values'] = records
                    if any(x['value'] == args.expected for x in records):
                        if report['first_exact_at'] is None:
                            report['first_exact_at'] = timestamp()
                            report['exact_text_evidence'] = [x for x in records if x['value'] == args.expected]
                            print('EXACT TEXT OBSERVED', flush=True)
        except Exception as e:
            error = f'{type(e).__name__}: {e}'
            if error != last_error:
                report['errors'].append(error)
                last_error = error
        write_atomic(output, report)
        time.sleep(args.interval)

    if report['errors']:
        report['reason'] = 'Observation encountered errors; cannot establish a reliable result'
    elif not target:
        report['reason'] = report['reason'] if 'ambiguous' in report['reason'] else 'No unique Notepad window observed'
    elif report['first_exact_at'] is None:
        report['reason'] = 'Exact text never observed before exit/timeout'
    elif not report['closure_observed_at']:
        report['reason'] = 'Exact text observed, but original window closure not established'
    else:
        # Full task PASS requires separate reliable evidence that no file was saved.
        report['reason'] = 'Exact text and original window closure observed; save behavior unverified'
    report['verdict'] = 'INCONCLUSIVE'
    report['text_check'] = 'PASS' if report['first_exact_at'] else 'INCONCLUSIVE'
    report['window_check'] = 'PASS' if report['closure_observed_at'] else 'INCONCLUSIVE'
    report['finished_at'] = timestamp()
    write_atomic(output, report)
    print(f'TEXT={report["text_check"]}; WINDOW={report["window_check"]}; FULL_TASK=INCONCLUSIVE', flush=True)
    print(report['reason'], flush=True)
    return 0

if __name__ == '__main__':
    sys.exit(main())
