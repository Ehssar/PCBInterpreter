from __future__ import annotations
from enrichment.llm_enrichment import enrich_component

import os
import uuid
from typing import Any

import cv2
import numpy as np
import requests

SESSION = requests.Session()

ROBOFLOW_API_KEY = os.getenv("ROBOFLOW_API_KEY", "")
ROBOFLOW_MODEL_ID = os.getenv("ROBOFLOW_MODEL_ID", "printed-circuit-board/3")
ROBOFLOW_BASE_URL = os.getenv("ROBOFLOW_BASE_URL", "https://serverless.roboflow.com")
ROBOFLOW_TIMEOUT_SECONDS = float(os.getenv("ROBOFLOW_TIMEOUT_SECONDS", "20"))
MIN_CONFIDENCE = float(os.getenv("MIN_CONFIDENCE", "0.4"))

ENABLE_LLM_ENRICHMENT = os.getenv("ENABLE_LLM_ENRICHMENT", "false").lower() == "true"
LLM_ENRICH_MAX_COMPONENTS = int(os.getenv("LLM_ENRICH_MAX_COMPONENTS", "1"))
LLM_ENRICH_MIN_CONFIDENCE = float(os.getenv("LLM_ENRICH_MIN_CONFIDENCE", "0.70"))
LLM_ENRICH_TYPES = {
    t.strip().lower()
    for t in os.getenv("LLM_ENRICH_TYPES", "ic,connector").split(",")
    if t.strip()
}
LLM_USE_NEIGHBORHOOD_CROP = os.getenv("LLM_USE_NEIGHBORHOOD_CROP", "false").lower() == "true"

LABEL_MAP = {
    "resistor": "resistor",
    "capacitor": "capacitor",
    "electrolytic capacitor": "capacitor",
    "ic": "ic",
    "iC": "ic",
    "connector": "connector",
    "diode": "diode",
    "transistor": "transistor",
    "inductor": "inductor",
    "led": "led",
    "button": "unknown",
    "switch": "unknown",
    "clock": "unknown",
    "ferrite bead": "unknown",
    "fuse": "unknown",
    "heatsink": "unknown",
    "jumper": "unknown",
}

from pathlib import Path
from datetime import datetime

DEBUG_SAVE_DETECTIONS = os.getenv("DEBUG_SAVE_DETECTIONS", "true").lower() == "true"
# parent directory of detector.py
BASE_DIR = Path(__file__).resolve().parent.parent
DEBUG_IMAGE_PATH = BASE_DIR / "debug.jpg"


def _save_detection_debug_image(
    image_bgr: np.ndarray,
    components: list[dict[str, Any]]
) -> None:
    if not DEBUG_SAVE_DETECTIONS:
        return None

    debug_img = image_bgr.copy()

    for component in components:
        bbox = component.get("bbox")
        if not bbox or len(bbox) != 4:
            continue

        x, y, w, h = bbox
        x0 = max(0, x)
        y0 = max(0, y)
        x1 = min(debug_img.shape[1], x + w)
        y1 = min(debug_img.shape[0], y + h)

        label = component.get("type", "unknown")
        raw_label = component.get("source_label", "")
        conf = float(component.get("confidence", 0.0))

        # Blue box in BGR
        cv2.rectangle(debug_img, (x0, y0), (x1, y1), (255, 0, 0), 2)

        text = f"{raw_label}->{label} {conf:.2f}" if raw_label else f"{label} {conf:.2f}"
        text_y = max(20, y0 - 8)

        cv2.putText(
            debug_img,
            text,
            (x0, text_y),
            cv2.FONT_HERSHEY_SIMPLEX,
            0.5,
            (255, 0, 0),  # blue in BGR
            1,
            cv2.LINE_AA,
        )

    cv2.imwrite(str(DEBUG_IMAGE_PATH), debug_img)
    print(f"[detector debug] saved {DEBUG_IMAGE_PATH}")

def _bgr_to_jpeg_bytes(image_bgr: np.ndarray) -> bytes:
    ok, encoded = cv2.imencode(".jpg", image_bgr)
    if not ok:
        raise ValueError("Failed to JPEG-encode image for Roboflow request")
    return encoded.tobytes()


def _xy_center_to_xywh(x: float, y: float, w: float, h: float) -> list[int]:
    left = int(round(x - w / 2))
    top = int(round(y - h / 2))
    width = int(round(w))
    height = int(round(h))
    return [left, top, width, height]


def _pretty_name(component_type: str, raw_label: str) -> str:
    if raw_label and raw_label.lower() != component_type.lower():
        return raw_label.title()
    return component_type.replace("_", " ").title()


def _make_default_subtitle(component_type: str, raw_label: str) -> str:
    raw_lower = raw_label.lower().strip()

    if component_type == "resistor":
        return "SMD resistor"

    if component_type == "capacitor":
        if "electrolytic" in raw_lower:
            return "Polarized capacitor"
        return "SMD capacitor"

    if component_type == "ic":
        return "Integrated circuit"

    if component_type == "diode":
        return "Semiconductor diode"

    if component_type == "transistor":
        return "Discrete transistor"

    if component_type == "inductor":
        return "Power inductor"

    if component_type == "led":
        return "Light-emitting diode"

    if component_type == "connector":
        return "Board connector"

    return "PCB component"


def _make_component_label(component_type: str, raw_label: str) -> dict[str, Any]:
    title = _pretty_name(component_type, raw_label)

    return {
        "title": title,
        "subtitle": _make_default_subtitle(component_type, raw_label),
        "visible": True,
        "pinned": False,
    }


def _make_attributes(component_type: str, raw_label: str) -> dict[str, Any]:
    raw_lower = raw_label.lower().strip()

    package = None
    package_confidence = None
    part_family = None
    part_family_confidence = None
    electrical_value = None
    electrical_value_confidence = None
    likely_role = None
    likely_role_confidence = None
    mount_type = None
    pin_count = None
    polarized = None
    marking_text = None

    # Conservative CV-only heuristics for now.
    # These are placeholders so the schema is ready for OCR/LLM enrichment later.
    if component_type in {"resistor", "capacitor", "ic", "diode", "transistor", "inductor", "led"}:
        mount_type = "SMD"

    if component_type == "capacitor":
        if "electrolytic" in raw_lower:
            polarized = True
            likely_role = "bulk capacitor"
            likely_role_confidence = 0.45
        else:
            polarized = False

    if component_type == "ic":
        part_family = "integrated circuit"
        part_family_confidence = 0.35

    return {
        "package": package,
        "package_confidence": package_confidence,
        "marking_text": marking_text,
        "part_family": part_family,
        "part_family_confidence": part_family_confidence,
        "electrical_value": electrical_value,
        "electrical_value_confidence": electrical_value_confidence,
        "likely_role": likely_role,
        "likely_role_confidence": likely_role_confidence,
        "mount_type": mount_type,
        "pin_count": pin_count,
        "polarized": polarized,
    }

def _should_enrich_component(component: dict[str, Any]) -> bool:
    component_type = str(component.get("type", "")).lower()
    confidence = float(component.get("confidence", 0.0))

    return (
        ENABLE_LLM_ENRICHMENT
        and component_type in LLM_ENRICH_TYPES
        and confidence >= LLM_ENRICH_MIN_CONFIDENCE
    )

def _make_enrichment(component_type: str, raw_label: str) -> dict[str, Any]:
    display_name = _pretty_name(component_type, raw_label)

    return {
        "display_name": display_name,
        "one_line_label": f"Likely {component_type}",
        "function_summary": f"Detected {component_type}. Additional functional detail unavailable for this pass.",
        "confidence_note": "Preliminary CV-only result.",
        "ocr_text": None,
        "datasheet_url": None,
        "needs_human_verification": True,
        "datasheet_search_terms": [component_type],
        "attributes": _make_attributes(component_type, raw_label),
    }


def _make_detection(component_type: str, raw_label: str, conf: float) -> dict[str, Any]:
    return {
        "source": "roboflow",
        "model_id": ROBOFLOW_MODEL_ID,
        "raw_model_label": raw_label,
        "normalized_type": component_type,
        "confidence": conf,
    }


def _mock_candidates_for_type(component_type: str) -> list[dict[str, Any]]:
    mock_map = {
        "resistor": [
            {
                "part_number": "RC0603FR-0710KL",
                "confidence": 0.42,
                "datasheet_url": "https://example.com/resistor-datasheet",
            }
        ],
        "capacitor": [
            {
                "part_number": "CL10A106KP8NNNC",
                "confidence": 0.40,
                "datasheet_url": "https://example.com/capacitor-datasheet",
            }
        ],
        "ic": [
            {
                "part_number": "LM358",
                "confidence": 0.35,
                "datasheet_url": "https://example.com/ic-datasheet",
            }
        ],
        "diode": [
            {
                "part_number": "1N4148",
                "confidence": 0.38,
                "datasheet_url": "https://example.com/diode-datasheet",
            }
        ],
        "connector": [
            {
                "part_number": "HDR-2.54-8P",
                "confidence": 0.30,
                "datasheet_url": "https://example.com/connector-datasheet",
            }
        ],
    }

    return mock_map.get(component_type, [])


def _normalize_prediction(pred: dict[str, Any]) -> dict[str, Any] | None:
    raw_label = str(pred.get("class", "unknown")).strip()
    conf = float(pred.get("confidence", 0.0))

    if conf < MIN_CONFIDENCE:
        return None

    component_type = LABEL_MAP.get(raw_label.lower(), "unknown")

    bbox = _xy_center_to_xywh(
        float(pred["x"]),
        float(pred["y"]),
        float(pred["width"]),
        float(pred["height"]),
    )

    return {
        "component_id": str(uuid.uuid4())[:8],
        "type": component_type,
        "resolved_type": component_type,
        "confidence": conf,
        "bbox": bbox,
        "source_label": raw_label,
        "detection": _make_detection(component_type, raw_label, conf),
        "enrichment": _make_enrichment(component_type, raw_label),
        "label": _make_component_label(component_type, raw_label),
        "candidates": _mock_candidates_for_type(component_type),
    }

# HELPER FUNCTIONS FOR Detect Components BGR
def _bbox_iou_xywh(a: list[int], b: list[int]) -> float:
    ax, ay, aw, ah = a
    bx, by, bw, bh = b

    ax2 = ax + aw
    ay2 = ay + ah
    bx2 = bx + bw
    by2 = by + bh

    inter_x1 = max(ax, bx)
    inter_y1 = max(ay, by)
    inter_x2 = min(ax2, bx2)
    inter_y2 = min(ay2, by2)

    inter_w = max(0, inter_x2 - inter_x1)
    inter_h = max(0, inter_y2 - inter_y1)
    inter_area = inter_w * inter_h

    if inter_area <= 0:
        return 0.0

    area_a = aw * ah
    area_b = bw * bh
    union = area_a + area_b - inter_area

    if union <= 0:
        return 0.0

    return inter_area / union


def _dedup_overlapping_components(
    components: list[dict[str, Any]],
    iou_threshold: float = 0.5,
) -> list[dict[str, Any]]:
    """
    Keep only the highest-confidence component among overlapping boxes.

    Assumes components are already sorted by confidence descending.
    """
    kept: list[dict[str, Any]] = []

    for candidate in components:
        candidate_bbox = candidate.get("bbox")
        if not candidate_bbox or len(candidate_bbox) != 4:
            continue

        overlaps_existing = False

        for existing in kept:
            existing_bbox = existing.get("bbox")
            if not existing_bbox or len(existing_bbox) != 4:
                continue

            iou = _bbox_iou_xywh(candidate_bbox, existing_bbox)
            if iou >= iou_threshold:
                overlaps_existing = True
                break

        if not overlaps_existing:
            kept.append(candidate)

    return kept

def detect_components_bgr(image_bgr: np.ndarray) -> dict[str, Any]:
    if not ROBOFLOW_API_KEY:
        raise RuntimeError("ROBOFLOW_API_KEY is not set")

    image_bytes = _bgr_to_jpeg_bytes(image_bgr)

    url = f"{ROBOFLOW_BASE_URL}/{ROBOFLOW_MODEL_ID}"

    response = SESSION.post(
        url,
        params={"api_key": ROBOFLOW_API_KEY},
        files={"file": ("frame.jpg", image_bytes, "image/jpeg")},
        timeout=ROBOFLOW_TIMEOUT_SECONDS,
    )
    response.raise_for_status()

    payload = response.json()
    predictions = payload.get("predictions", [])

    components: list[dict[str, Any]] = []

    for pred in predictions:
        normalized = _normalize_prediction(pred)
        if normalized is not None:
            components.append(normalized)

    components.sort(key=lambda c: float(c.get("confidence", 0.0)), reverse=True)
    components = _dedup_overlapping_components(components, iou_threshold=0.5)

    debug_image_path = _save_detection_debug_image(image_bgr, components)
    if debug_image_path:
        print(f"[detector debug] saved {debug_image_path}")

    enriched_count = 0

    for component in components:
        if enriched_count >= LLM_ENRICH_MAX_COMPONENTS:
            break

        if not _should_enrich_component(component):
            continue

        x, y, w, h = component["bbox"]

        x0 = max(0, x)
        y0 = max(0, y)
        x1 = min(image_bgr.shape[1], x + w)
        y1 = min(image_bgr.shape[0], y + h)

        if x1 <= x0 or y1 <= y0:
            continue

        component_crop = image_bgr[y0:y1, x0:x1]

        neighborhood_crop = None
        if LLM_USE_NEIGHBORHOOD_CROP:
            pad_x = max(w // 2, 16)
            pad_y = max(h // 2, 16)

            nx0 = max(0, x0 - pad_x)
            ny0 = max(0, y0 - pad_y)
            nx1 = min(image_bgr.shape[1], x1 + pad_x)
            ny1 = min(image_bgr.shape[0], y1 + pad_y)

            if nx1 > nx0 and ny1 > ny0:
                neighborhood_crop = image_bgr[ny0:ny1, nx0:nx1]

        try:
            enrichment = enrich_component(
                component=component,
                component_crop_bgr=component_crop,
                neighborhood_crop_bgr=neighborhood_crop,
            )

            component["enrichment"] = enrichment

            if enrichment.get("display_name"):
                component["label"]["title"] = enrichment["display_name"]

            if enrichment.get("one_line_label"):
                component["label"]["subtitle"] = enrichment["one_line_label"]

            allowed_types = {
                "resistor", "capacitor", "ic", "diode",
                "transistor", "inductor", "led", "connector", "unknown"
            }

            raw_type = str(component.get("type", "unknown")).lower()
            enriched_resolved_type = str(enrichment.get("resolved_type", "unknown")).lower()

            if raw_type != "unknown":
                component["resolved_type"] = raw_type
            elif enriched_resolved_type in allowed_types:
                component["resolved_type"] = enriched_resolved_type
            else:
                component["resolved_type"] = "unknown"

            enriched_count += 1

        except Exception as e:
            print(f"[LLM enrichment failed for {component.get('component_id', 'unknown')}] {e}")

    return {"components": components}