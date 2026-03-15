from pathlib import Path
from typing import Any
import uuid
import torch

MODEL_PATH = Path(__file__).resolve().parent / "models" / "pcb_components_best.pt"
YOLOV5_PATH = Path(__file__).resolve().parents[1] / "yolov5"

LABEL_MAP = {
    "resistor": "resistor",
    "capacitor": "capacitor",
    "ic": "ic",
    "connector": "connector",
    "diode": "diode",
    "transistor": "transistor",
    "inductor": "inductor",
}

_model = None


def load_model():
    global _model

    if _model is None:
        if not MODEL_PATH.exists():
            raise FileNotFoundError(f"Model weights not found at {MODEL_PATH}")

        if not YOLOV5_PATH.exists():
            raise FileNotFoundError(f"YOLOv5 repo not found at {YOLOV5_PATH}")

        _model = torch.hub.load(
            str(YOLOV5_PATH),
            "custom",
            path=str(MODEL_PATH),
            source="local"
        )
        _model.conf = 0.25

    return _model

def _xyxy_to_xywh(x1: float, y1: float, x2: float, y2: float) -> list[int]:
    x = int(round(x1))
    y = int(round(y1))
    w = int(round(x2 - x1))
    h = int(round(y2 - y1))
    return [x, y, w, h]


def detect_components_bgr(image_bgr) -> dict[str, Any]:
    model = load_model()
    results = model(image_bgr)
    preds = results.pandas().xyxy[0]

    components = []

    for _, row in preds.iterrows():
        raw_name = str(row["name"]).lower().strip()
        component_type = LABEL_MAP.get(raw_name, raw_name)

        bbox = _xyxy_to_xywh(
            row["xmin"],
            row["ymin"],
            row["xmax"],
            row["ymax"]
        )

        components.append({
            "component_id": str(uuid.uuid4())[:8],
            "type": component_type,
            "confidence": float(row["confidence"]),
            "bbox": bbox,
            "candidates": []
        })

    return {"components": components}