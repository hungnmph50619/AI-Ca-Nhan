#!/usr/bin/env python3
"""So sánh A/B ngoại tuyến trên CÙNG bộ ảnh ROI minimap đã xác minh.

Hai tệp JSON phải có chính xác cùng mã mẫu và cùng nhãn chuẩn. Chỉ dự đoán
được thay đổi. Không đọc ảnh, không gọi Gemini và không chấm điểm chung
cho một mô hình khi thiếu bộ ảnh kiểm thử độc lập.
"""
from __future__ import annotations

import argparse
import json
import math
import sys
from pathlib import Path

from evaluate_minimap_boxes import evaluate, validate_box

MAX_SAMPLES = 2000
MAX_FILE_BYTES = 1024 * 1024


def _verified_index(data: object, title: str) -> dict[str, dict]:
    if not isinstance(data, list) or not 1 <= len(data) <= MAX_SAMPLES:
        raise ValueError(f"{title}: cần danh sách 1–{MAX_SAMPLES} mẫu.")
    indexed: dict[str, dict] = {}
    for record in data:
        if not isinstance(record, dict) or set(record) != {
            "id", "reviewed", "truth_box", "predicted_box"
        }:
            raise ValueError(f"{title}: mỗi mẫu chỉ có bốn trường id, reviewed, truth_box và predicted_box.")
        sample_id = record["id"]
        if not isinstance(sample_id, str) or not sample_id or len(sample_id) > 64 or sample_id in indexed:
            raise ValueError(f"{title}: mã mẫu trống, quá dài hoặc trùng.")
        if record["reviewed"] is not True:
            raise ValueError(f"{title}: mẫu {sample_id} chưa được người dùng xác minh.")
        validate_box(record["truth_box"], sample_id, "truth_box")
        validate_box(record["predicted_box"], sample_id, "predicted_box")
        indexed[sample_id] = record
    return indexed


def compare_datasets(before: object, after: object, minimum_iou: float = .5) -> dict:
    if not isinstance(minimum_iou, (int, float)) or not math.isfinite(minimum_iou) or not 0 < minimum_iou <= 1:
        raise ValueError("Ngưỡng IoU phải là số hữu hạn, lớn hơn 0 và không vượt quá 1.")

    a = _verified_index(before, "Bộ A")
    b = _verified_index(after, "Bộ B")
    if set(a) != set(b):
        missing_b = sorted(set(a) - set(b))
        missing_a = sorted(set(b) - set(a))
        raise ValueError(
            f"Hai bộ phải có cùng mã mẫu. Chỉ trong A: {missing_b[:5]}; chỉ trong B: {missing_a[:5]}."
        )
    for sample_id, record in a.items():
        if record["truth_box"] != b[sample_id]["truth_box"]:
            raise ValueError(
                f"{sample_id}: nhãn chuẩn khác nhau. Chỉ so sánh hai dự đoán trên cùng một nhãn đã xác minh."
            )

    # Thứ tự JSON không ảnh hưởng phép so sánh: sắp theo mã mẫu để ghép cặp.
    keys = sorted(a)
    report_a = evaluate([a[key] for key in keys], minimum_iou)
    report_b = evaluate([b[key] for key in keys], minimum_iou)
    rows = []
    changed = 0
    matching = {"true_positive", "true_negative"}
    transitions = {"corrected": 0, "regressed": 0, "both_match": 0, "both_mismatch": 0}
    for result_a, result_b in zip(report_a["results"], report_b["results"]):
        sample_id = result_a["id"]
        assert result_b["id"] == sample_id
        prediction_changed = a[sample_id]["predicted_box"] != b[sample_id]["predicted_box"]
        if prediction_changed:
            changed += 1
        a_matches = result_a["outcome"] in matching
        b_matches = result_b["outcome"] in matching
        if a_matches and b_matches:
            transition = "both_match"
        elif not a_matches and b_matches:
            transition = "corrected"
        elif a_matches and not b_matches:
            transition = "regressed"
        else:
            transition = "both_mismatch"
        transitions[transition] += 1
        rows.append({
            "id": sample_id,
            "transition": transition,
            "prediction_changed": prediction_changed,
            "a_outcome": result_a["outcome"],
            "b_outcome": result_b["outcome"],
            "a_iou": result_a["iou"],
            "b_iou": result_b["iou"],
        })

    def summary(report: dict) -> dict:
        return {name: report[name] for name in
                ("samples", "tp", "fp", "fn", "tn", "precision", "recall", "f1")}

    return {
        "samples": len(keys),
        "minimum_iou": minimum_iou,
        "changed_predictions": changed,
        "transitions": transitions,
        "a": summary(report_a),
        "b": summary(report_b),
        "per_sample": rows,
        "note": "Chỉ so sánh tọa độ trên cùng mẫu, không phải đánh giá chất lượng tổng quát hay kết quả trận đấu.",
    }


def _read_json(path: Path) -> object:
    if path.stat().st_size > MAX_FILE_BYTES:
        raise ValueError(f"{path.name}: tệp vượt giới hạn 1 MB.")
    return json.loads(path.read_text(encoding="utf-8"))


def main() -> int:
    parser = argparse.ArgumentParser(description="So sánh hai bộ dự đoán minimap trên cùng nhãn chuẩn.")
    parser.add_argument("dataset_a", type=Path, help="JSON đã xác minh với dự đoán A")
    parser.add_argument("dataset_b", type=Path, help="JSON đã xác minh với dự đoán B")
    parser.add_argument("--min-iou", type=float, default=.5, help="Ngưỡng IoU, mặc định 0.5")
    args = parser.parse_args()
    try:
        report = compare_datasets(
            _read_json(args.dataset_a), _read_json(args.dataset_b), args.min_iou
        )
    except (OSError, UnicodeError, json.JSONDecodeError, ValueError) as exc:
        print(f"Không thể so sánh: {exc}", file=sys.stderr)
        return 2
    print(json.dumps(report, indent=2, ensure_ascii=False))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
