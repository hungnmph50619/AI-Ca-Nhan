"""Offline contract tests for the UFO process boundary."""
import importlib.util
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

MODULE_PATH = Path(__file__).resolve().parents[1] / "integration" / "ufo_bridge.py"
spec = importlib.util.spec_from_file_location("ufo_bridge", MODULE_PATH)
bridge = importlib.util.module_from_spec(spec)
spec.loader.exec_module(bridge)


class BridgeTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        (self.root / "ufo").mkdir()
        (self.root / "ufo" / "__main__.py").touch()
        self.python = self.root / "python.exe"
        self.python.touch()

    def invoke(self, **kwargs):
        return bridge.launch(root=self.root, python=self.python,
                             task="notepad-test", request="type exact text",
                             **kwargs)

    @patch.object(bridge.subprocess, "run")
    def test_dry_run_never_launches(self, run):
        result = self.invoke()
        run.assert_not_called()
        self.assertEqual(result["task_verdict"], "INCONCLUSIVE")
        self.assertEqual(result["process_status"], "dry_run")
        self.assertNotIn("type exact text", str(result))

    @patch.object(bridge.subprocess, "run")
    def test_successful_process_not_task_pass(self, run):
        run.return_value.returncode = 0
        result = self.invoke(execute=True)
        self.assertEqual(result["process_status"], "exited")
        self.assertEqual(result["exit_code"], 0)
        self.assertEqual(result["task_verdict"], "INCONCLUSIVE")
        self.assertFalse(result["independent_verification"])
        self.assertEqual(run.call_args.kwargs["cwd"], str(self.root))

    @patch.object(bridge.subprocess, "run")
    def test_nonzero_exit_reported(self, run):
        run.return_value.returncode = 7
        result = self.invoke(execute=True)
        self.assertEqual(result["process_status"], "process_failed")
        self.assertEqual(result["task_verdict"], "INCONCLUSIVE")

    @patch.object(bridge.subprocess, "run")
    def test_timeout_reported(self, run):
        run.side_effect = bridge.subprocess.TimeoutExpired("ufo", 1)
        result = self.invoke(execute=True, timeout=1)
        self.assertEqual(result["process_status"], "timeout")
        self.assertEqual(result["task_verdict"], "INCONCLUSIVE")

    def test_missing_ufo_entrypoint_rejected(self):
        (self.root / "ufo" / "__main__.py").unlink()
        with self.assertRaises(FileNotFoundError):
            self.invoke()


if __name__ == "__main__":
    unittest.main()
