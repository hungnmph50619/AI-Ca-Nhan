"""UFO-first process boundary for PersonalAI (v0.1).

Runs the unmodified upstream UFO CLI only when explicitly enabled.
Process exit is never interpreted as task verification.
"""
from __future__ import annotations

import argparse
import json
import os
from pathlib import Path
import subprocess
import sys
from datetime import datetime, timezone


def launch(*, root: Path, python: Path, task: str, request: str,
           execute: bool = False, timeout: int = 600) -> dict:
    if not task or not task.strip() or not request or not request.strip():
        raise ValueError("task and request must not be empty")
    if timeout < 1:
        raise ValueError("timeout must be positive")
    root = root.resolve()
    python = python.resolve()
    if not (root / "ufo" / "__main__.py").is_file():
        raise FileNotFoundError("UFO entrypoint not found in root")
    if not python.is_file():
        raise FileNotFoundError("Python executable not found")

    command = [str(python), "-m", "ufo", "--task", task,
               "--mode", "normal", "--request", request, "--log-level", "INFO"]
    result = {
        "schema": "personalai.ufo.bridge.v1",
        "engine": "microsoft-ufo",
        "task": task,
        "executed": False,
        "process_status": "dry_run",
        "task_verdict": "INCONCLUSIVE",
        "independent_verification": False,
        "exit_code": None,
        "command": [str(python), "-m", "ufo", "--task", task,
                    "--mode", "normal", "--request", "[REDACTED]", "--log-level", "INFO"],
    }
    if not execute:
        return result
    result["executed"] = True
    result["started_at_utc"] = datetime.now(timezone.utc).isoformat()
    try:
        # Inherit stdio: keeps UFO interactive security confirmations functional.
        completed = subprocess.run(command, cwd=str(root), timeout=timeout,
                                   check=False)
        result["exit_code"] = completed.returncode
        result["process_status"] = (
            "exited" if completed.returncode == 0 else "process_failed")
    except subprocess.TimeoutExpired:
        result["process_status"] = "timeout"
    except OSError as exc:
        result["process_status"] = "launch_failed"
        result["error_type"] = type(exc).__name__
    result["finished_at_utc"] = datetime.now(timezone.utc).isoformat()
    # Task PASS requires external evidence; never infer it from an agent FINISH.
    return result


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="UFO-first integration boundary")
    parser.add_argument("--ufo-root", type=Path, required=True)
    parser.add_argument("--python", type=Path, required=True)
    parser.add_argument("--task", required=True)
    parser.add_argument("--request", required=True)
    parser.add_argument("--execute", action="store_true")
    parser.add_argument("--timeout", type=int, default=600)
    parser.add_argument("--result", type=Path)
    args = parser.parse_args(argv)
    try:
        output = launch(root=args.ufo_root, python=args.python, task=args.task,
                        request=args.request, execute=args.execute,
                        timeout=args.timeout)
    except (OSError, ValueError) as exc:
        print(json.dumps({"process_status": "invalid_configuration",
                          "error_type": type(exc).__name__}), file=sys.stderr)
        return 2
    rendered = json.dumps(output, ensure_ascii=False, indent=2)
    print(rendered)
    if args.result:
        args.result.parent.mkdir(parents=True, exist_ok=True)
        args.result.write_text(rendered + "\n", encoding="utf-8")
    return 0 if output["process_status"] in ("dry_run", "exited") else 1


if __name__ == "__main__":
    raise SystemExit(main())
