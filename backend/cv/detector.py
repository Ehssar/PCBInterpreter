from pathlib import Path
from typing import Any
import uuid

import numpy as np
from ultralytics import YOLO

MODEL_PATH = Path(__file__).resolve().parent / "models" / "pcb_components_best.pt"

LABEL_MAP = {
    "resistor": "resistor",
    "capacitor": "capacitor",
    "ic": "ic",
    "connector": "connector",
    "diode": "diode",
    "transistor": "transistor",
    "inductor": "inductor",
}

_model: YOLO | None = None


def load_model() -> YOLO:
    global _model

    if _model is None:
        if not MODEL_PATH.exists():
            raise FileNotFoundError(f"Model weights not found at {MODEL_PATH}")

        # Long-term solution: use Ultralytics directly instead of torch.hub + local yolov5 repo
        _model = YOLO(str(MODEL_PATH))

    return _model


def _xyxy_to_xywh(x1: float, y1: float, x2: float, y2: float) -> list[int]:
    x = int(round(x1))
    y = int(round(y1))
    w = int(round(x2 - x1))
    h = int(round(y2 - y1))
    return [x, y, w, h]


def detect_components_bgr(image_bgr: np.ndarray) -> dict[str, Any]:
    model = load_model()

    # Ultralytics accepts numpy images directly for prediction
    results = model.predict(
        source=image_bgr,
        conf=0.25,
        verbose=False
    )

    result = results[0]
    components = []

    if result.boxes is None or len(result.boxes) == 0:
        return {"components": components}

    boxes_xyxy = result.boxes.xyxy.cpu().numpy()
    boxes_cls = result.boxes.cls.cpu().numpy()
    boxes_conf = result.boxes.conf.cpu().numpy()
    names = result.names

    for xyxy, cls_id, conf in zip(boxes_xyxy, boxes_cls, boxes_conf):
        x1, y1, x2, y2 = xyxy.tolist()

        raw_name = str(names[int(cls_id)]).lower().strip()
        component_type = LABEL_MAP.get(raw_name, raw_name)

        bbox = _xyxy_to_xywh(x1, y1, x2, y2)

        components.append({
            "component_id": str(uuid.uuid4())[:8],
            "type": component_type,
            "confidence": float(conf),
            "bbox": bbox,
            "candidates": []
        })

    return {"components": components}