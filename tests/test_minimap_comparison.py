"""So sánh cặp dự đoán chỉ trên tập minimap có cùng ID và nhãn chuẩn."""
import importlib.util
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[1] / "scripts"
import sys
sys.path.insert(0, str(ROOT))
SPEC = importlib.util.spec_from_file_location("minimap_pair_comparator", ROOT / "compare_minimap_evaluations.py")
comparison = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(comparison)


def item(id, truth, predicted, reviewed=True):
    return {"id": id, "reviewed": reviewed, "truth_box": truth, "predicted_box": predicted}


class ComparisonTests(unittest.TestCase):
    def test_same_samples_different_order_and_predictions(self):
        a = [
            item("one", [0, 0, 200, 200], None),
            item("two", None, None),
            item("three", [100, 100, 400, 400], [100, 100, 400, 400]),
        ]
        b = [
            item("three", [100, 100, 400, 400], [100, 100, 400, 400]),
            item("one", [0, 0, 200, 200], [0, 0, 200, 200]),
            item("two", None, [50, 50, 300, 300]),
        ]
        result = comparison.compare_datasets(a, b)
        self.assertEqual(result["samples"], 3)
        self.assertEqual(result["changed_predictions"], 2)
        self.assertEqual((result["a"]["tp"], result["a"]["fn"], result["a"]["fp"]),
                         (1, 1, 0))
        self.assertEqual((result["b"]["tp"], result["b"]["fn"], result["b"]["fp"]),
                         (2, 0, 1))
        self.assertEqual([row["id"] for row in result["per_sample"]], ["one", "three", "two"])
        self.assertEqual(result["per_sample"][0]["a_outcome"], "false_negative")
        self.assertEqual(result["per_sample"][0]["b_outcome"], "true_positive")

    def test_identical_datasets_have_no_changes(self):
        a = [item("x", None, None)]
        report = comparison.compare_datasets(a, a)
        self.assertEqual(report["changed_predictions"], 0)
        self.assertEqual(report["a"], report["b"])

    def test_missing_samples_or_modified_reference_rejected(self):
        a = [item("x", [100, 100, 400, 400], None)]
        bad = [
            [],
            [item("y", [100, 100, 400, 400], None)],
            [item("x", [101, 100, 400, 400], None)],
            [item("x", [100, 100, 400, 400], None, False)],
            [item("x", [100, 100, 400, 400], [0, 0, 1001, 1000])],
            [item("x", [100, 100, 400, 400], None), item("x", [100, 100, 400, 400], None)],
            [item("x", [100, 100, 400, 400], None) | {"extra": "do not accept"}],
        ]
        for other in bad:
            with self.subTest(other=other), self.assertRaises(ValueError):
                comparison.compare_datasets(a, other)

    def test_threshold_and_invalid_numbers(self):
        a = [item("x", [0, 0, 200, 200], [100, 0, 300, 200])]
        self.assertEqual(comparison.compare_datasets(a, a, .3)["a"]["tp"], 1)
        self.assertEqual(comparison.compare_datasets(a, a, .5)["a"]["tp"], 0)
        for bad in [0, -1, 1.1, float("nan"), float("inf"), "0.5"]:
            with self.subTest(bad=bad), self.assertRaises(ValueError):
                comparison.compare_datasets(a, a, bad)

    def test_reports_include_no_images_or_api_keys(self):
        a = [item("sample_01", [0, 0, 200, 200], None)]
        b = [item("sample_01", [0, 0, 200, 200], [0, 0, 200, 200])]
        report = comparison.compare_datasets(a, b)
        self.assertEqual(set(report["per_sample"][0]), {
            "id", "prediction_changed", "a_outcome", "b_outcome", "a_iou", "b_iou"
        })


if __name__ == "__main__":
    unittest.main()
