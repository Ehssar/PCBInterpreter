from __future__ import annotations

import os
import uuid
from typing import Any

import cv2
import numpy as np
import requests

ROBOFLOW_API_KEY = os.getenv("ROBOFLOW_API_KEY", "")
ROBOFLOW_MODEL_ID = os.getenv("ROBOFLOW_MODEL_ID", "printed-circuit-board/3")
ROBOFLOW_BASE_URL = os.getenv("ROBOFLOW_BASE_URL", "https://serverless.roboflow.com")
ROBOFLOW_TIMEOUT_SECONDS = float(os.getenv("ROBOFLOW_TIMEOUT_SECONDS", "20"))
MIN_CONFIDENCE = float(os.getenv("MIN_CONFIDENCE", "0.25"))

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
        "confidence": conf,
        "bbox": bbox,
        "candidates": [],
        "source_label": raw_label,
    }


def detect_components_bgr(image_bgr: np.ndarray) -> dict[str, Any]:
    if not ROBOFLOW_API_KEY:
        raise RuntimeError("ROBOFLOW_API_KEY is not set")

    image_bytes = _bgr_to_jpeg_bytes(image_bgr)

    url = f"{ROBOFLOW_BASE_URL}/{ROBOFLOW_MODEL_ID}"

    response = requests.post(
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

    return {"components": components}