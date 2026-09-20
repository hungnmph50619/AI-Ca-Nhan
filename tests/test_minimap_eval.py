"""Kiểm thử tự động, không dùng ảnh thật hoặc API bên ngoài."""
import importlib.util
import math
from pathlib import Path
import unittest


MODULE_PATH = Path(__file__).resolve().parents[1] / "scripts" / "evaluate_minimap_boxes.py"
SPEC = importlib.util.spec_from_file_location("minimap_evaluator", MODULE_PATH)
evaluator = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(evaluator)


def sample(sample_id, truth, predicted, reviewed=True):
    return {"id": sample_id, "reviewed": reviewed,
            "truth_box": truth, "predicted_box": predicted}


class MinimapEvaluationTests(unittest.TestCase):
    def test_identical_boxes_and_correct_absence(self):
        report = evaluator.evaluate([
            sample("found", [100, 100, 400, 400], [100, 100, 400, 400]),
            sample("absent", None, None)
        ])
        self.assertEqual((report["tp"], report["tn"], report["fp"], report["fn"]), (1, 1, 0, 0))
        self.assertEqual(report["precision"], 1)
        self.assertEqual(report["recall"], 1)
        self.assertEqual(report["results"][0]["iou"], 1)

    def test_mismatched_box_counts_as_false_positive_and_false_negative(self):
        report = evaluator.evaluate([
            sample("wrong", [0, 0, 200, 200], [800, 800, 1000, 1000])
        ])
        self.assertEqual((report["tp"], report["fp"], report["fn"]), (0, 1, 1))
        self.assertEqual(report["results"][0]["outcome"], "mismatched_box")
        self.assertEqual(report["results"][0]["iou"], 0)

    def test_missing_and_spurious_predictions(self):
        report = evaluator.evaluate([
            sample("miss", [10, 10, 200, 200], None),
            sample("spurious", None, [10, 10, 200, 200])
        ])
        self.assertEqual((report["tp"], report["fp"], report["fn"]), (0, 1, 1))
        self.assertEqual(report["results"][0]["outcome"], "false_negative")
        self.assertEqual(report["results"][1]["outcome"], "false_positive")

    def test_half_overlap_threshold(self):
        report = evaluator.evaluate([
            sample("partial", [0, 0, 200, 200], [100, 0, 300, 200])
        ], minimum_iou=0.3)
        self.assertEqual(report["tp"], 1)
        self.assertAlmostEqual(report["results"][0]["iou"], 1/3, places=6)

    def test_unreviewed_duplicate_invalid_boxes_and_threshold_rejected(self):
        invalid_data = [
            [sample("unreviewed", None, None, False)],
            [sample("x", None, None), sample("x", None, None)],
            [sample("float", None, [0.0, 0, 100, 100])],
            [sample("reversed", None, [100, 0, 50, 100])],
            [sample("missing", None, None) | {"predicted_box": "not-a-box"}],
        ]
        for data in invalid_data:
            with self.subTest(data=data), self.assertRaises(ValueError):
                evaluator.evaluate(data)
        for bad in [0, 1.1, -1, float("nan")]:
            with self.subTest(bad=bad), self.assertRaises(ValueError):
                evaluator.evaluate([sample("x", None, None)], minimum_iou=bad)


if __name__ == "__main__":
    unittest.main()
