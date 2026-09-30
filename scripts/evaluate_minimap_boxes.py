#!/usr/bin/env python3
"""Đánh giá ngoại tuyến tọa độ khung minimap trên bộ ảnh đã gán nhãn thủ công.

Không đọc ảnh, không gọi Gemini, không truy cập trận đấu đang diễn ra.
Dữ liệu đầu vào gồm các tọa độ 0..1000 từ những ảnh đã lưu và được con người xác minh.
"""
from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path


def validate_box(value: object, sample_id: str, field: str) -> tuple[int, int, int, int] | None:
    if value is None:
        return None
    if not isinstance(value, list) or len(value) != 4:
        raise ValueError(f"{sample_id}: {field} phải là null hoặc mảng 4 số nguyên.")
    if any(type(x) is not int or x < 0 or x > 1000 for x in value):
        raise ValueError(f"{sample_id}: {field} phải chứa số nguyên trong khoảng 0..1000.")
    x1, y1, x2, y2 = value
    if x1 >= x2 or y1 >= y2:
        raise ValueError(f"{sample_id}: {field} có tọa độ góc không hợp lệ.")
    return x1, y1, x2, y2


def intersection_over_union(
    truth: tuple[int, int, int, int],
    predicted: tuple[int, int, int, int],
) -> float:
    ix1 = max(truth[0], predicted[0])
    iy1 = max(truth[1], predicted[1])
    ix2 = min(truth[2], predicted[2])
    iy2 = min(truth[3], predicted[3])
    overlap = max(0, ix2 - ix1) * max(0, iy2 - iy1)
    truth_area = (truth[2] - truth[0]) * (truth[3] - truth[1])
    predicted_area = (predicted[2] - predicted[0]) * (predicted[3] - predicted[1])
    return overlap / (truth_area + predicted_area - overlap)


def evaluate(data: object, minimum_iou: float = 0.5) -> dict:
    if not 0 < minimum_iou <= 1:
        raise ValueError("Ngưỡng IoU phải lớn hơn 0 và không vượt quá 1.")
    if not isinstance(data, list) or not data:
        raise ValueError("Bộ kiểm thử phải là mảng JSON không rỗng.")

    tp = fp = fn = tn = 0
    results: list[dict] = []
    known_ids: set[str] = set()
    for item in data:
        if not isinstance(item, dict):
            raise ValueError("Mỗi mẫu phải là một đối tượng JSON.")
        sample_id = item.get("id")
        if not isinstance(sample_id, str) or not sample_id.strip() or sample_id in known_ids:
            raise ValueError("Mỗi mẫu phải có id không rỗng và không trùng lặp.")
        known_ids.add(sample_id)
        if item.get("reviewed") is not True:
            raise ValueError(f"{sample_id}: nhãn tham chiếu phải được con người xác minh (reviewed=true).")
        if "truth_box" not in item or "predicted_box" not in item:
            raise ValueError(f"{sample_id}: thiếu truth_box hoặc predicted_box.")
        truth = validate_box(item["truth_box"], sample_id, "truth_box")
        predicted = validate_box(item["predicted_box"], sample_id, "predicted_box")
        iou = intersection_over_union(truth, predicted) if truth and predicted else None
        if truth is None and predicted is None:
            tn += 1
            outcome = "true_negative"
        elif truth is not None and predicted is not None and iou >= minimum_iou:
            tp += 1
            outcome = "true_positive"
        elif truth is None:
            fp += 1
            outcome = "false_positive"
        elif predicted is None:
            fn += 1
            outcome = "false_negative"
        else:
            # Một khung sai vừa báo nhầm vùng, vừa bỏ sót vùng minimap thực.
            fp += 1
            fn += 1
            outcome = "mismatched_box"
        results.append({"id": sample_id, "iou": round(iou, 6) if iou is not None else None,
                        "outcome": outcome})
    precision = tp / (tp + fp) if tp + fp else None
    recall = tp / (tp + fn) if tp + fn else None
    f1 = 2 * precision * recall / (precision + recall) if precision is not None and recall is not None and precision + recall else None
    return {
        "samples": len(data), "minimum_iou": minimum_iou,
        "tp": tp, "fp": fp, "fn": fn, "tn": tn,
        "precision": round(precision, 6) if precision is not None else None,
        "recall": round(recall, 6) if recall is not None else None,
        "f1": round(f1, 6) if f1 is not None else None,
        "results": results,
    }


def main() -> int:
    parser = argparse.ArgumentParser(description="Đánh giá ngoại tuyến khung minimap đã gán nhãn.")
    parser.add_argument("dataset", type=Path, help="JSON chứa nhãn tham chiếu và dự đoán")
    parser.add_argument("--min-iou", type=float, default=0.5, help="Ngưỡng IoU, mặc định 0.5")
    args = parser.parse_args()
    try:
        data = json.loads(args.dataset.read_text(encoding="utf-8"))
        report = evaluate(data, args.min_iou)
    except (OSError, UnicodeError, json.JSONDecodeError, ValueError) as exc:
        print(f"Không thể đánh giá: {exc}", file=sys.stderr)
        return 2
    print(json.dumps(report, ensure_ascii=False, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
