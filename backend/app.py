from fastapi import FastAPI, File, UploadFile
from fastapi.responses import JSONResponse, Response
import os
import time
import hashlib
import uvicorn
import numpy as np
import cv2

from cv.detector import detect_components_bgr

app = FastAPI()


DETECTOR_MODE = os.getenv("DETECTOR_MODE", "model").lower()
ALLOWED_MODES = {"model", "mock", "auto"}


def make_mock_component(h: str):
    seed = int(h[:6], 16)
    x = 50 + (seed % 200)
    y = 80 + ((seed // 3) % 200)
    w = 60 + ((seed // 7) % 80)
    hh = 20 + ((seed // 11) % 40)

    component_type = "unknown"

    return {
        "component_id": f"comp_{h}",
        "type": component_type,
        "confidence": 0.50,
        "bbox": [x, y, w, hh],
        "source_label": "mock",
        "label": {
            "title": "Unknown",
            "subtitle": "Mock detection",
            "visible": False,
            "pinned": False,
        },
        "details": {
            "summary": "Mock component used as fallback",
            "ocr_text": None,
            "datasheet_url": "https://example.com/datasheet",
            "raw_model_label": "mock",
        },
        "candidates": [
            {
                "part_number": f"MOCK-{h}",
                "confidence": 0.50,
                "datasheet_url": "https://example.com/datasheet"
            }
        ]
    }


def get_detector_mode() -> str:
    mode = DETECTOR_MODE if DETECTOR_MODE in ALLOWED_MODES else "model"
    return mode


def detect_with_model(image_bgr: np.ndarray) -> dict:
    """
    Adapter boundary for real CV inference.
    Keep this function stable even if you later swap:
    - Roboflow hosted inference
    - local YOLO
    - custom detector
    - ensemble / OCR-enriched pipeline
    """
    return detect_components_bgr(image_bgr)


def build_analyze_response(
    request_id: str,
    timing_ms: int,
    image_bytes: int,
    mode: str,
    components: list,
    fallback_reason: str | None = None,
):
    payload = {
        "request_id": request_id,
        "board_id": f"board_{request_id}",
        "timing_ms": timing_ms,
        "image_bytes": image_bytes,
        "mode": mode,
        "label_visibility_default": "hidden",
        "component_count": len(components),
        "components": components,
    }

    if fallback_reason is not None:
        payload["fallback_reason"] = fallback_reason

    return payload


@app.get("/health")
def health():
    return {
        "status": "ok",
        "detector_mode": get_detector_mode()
    }

@app.get("/ping")
def ping():
    return {"ok": True}


@app.post("/analyze")
async def analyze_image(file: UploadFile = File(...)):
    t0 = time.time()
    contents = await file.read()

    with open("debug_last_upload.jpg", "wb") as f:
        f.write(contents)

    h = hashlib.sha256(contents).hexdigest()[:12] if contents else "emptyupload"
    n = len(contents)

    if not contents:
        return JSONResponse(
            status_code=400,
            content={"error": "Empty upload"}
        )

    fallback_reason = None
    detector_mode = get_detector_mode()

    try:
        arr = np.frombuffer(contents, np.uint8)
        image_bgr = cv2.imdecode(arr, cv2.IMREAD_COLOR)

        if image_bgr is None:
            fallback_reason = "invalid_image_decode"
            raise ValueError("OpenCV could not decode uploaded image")

        # Explicit mock mode: skip model entirely
        if detector_mode == "mock":
            raise RuntimeError("DETECTOR_MODE=mock")

        result = detect_with_model(image_bgr)
        components = result.get("components", [])

        dt_ms = int((time.time() - t0) * 1000)

        print(
            f"Received {n} bytes | sha={h} | {file.filename} {file.content_type} "
            f"| components={len(components)} | mode=model | detector_mode={detector_mode} | {dt_ms}ms"
        )

        return JSONResponse(
            build_analyze_response(
                request_id=h,
                timing_ms=dt_ms,
                image_bytes=n,
                mode="model",
                components=components
            )
        )

    except Exception as e:
        dt_ms = int((time.time() - t0) * 1000)

        # In "model" mode, still fallback so debugging remains easy.
        # In "auto" mode, this is expected behavior if model is unavailable.
        # In "mock" mode, we intentionally route here.
        if fallback_reason is None:
            fallback_reason = f"{type(e).__name__}: {str(e)}"

        mock_component = make_mock_component(h)

        print(
            f"FALLBACK | sha={h} | {file.filename} {file.content_type} "
            f"| bytes={n} | mode=mock | detector_mode={detector_mode} "
            f"| reason={fallback_reason} | {dt_ms}ms"
        )

        return JSONResponse(
            build_analyze_response(
                request_id=h,
                timing_ms=dt_ms,
                image_bytes=n,
                mode="mock",
                components=[mock_component],
                fallback_reason=fallback_reason
            )
        )

@app.post("/analyze_debug")
async def analyze_debug(file: UploadFile = File(...)):
    contents = await file.read()

    if not contents:
        return JSONResponse(status_code=400, content={"error": "Empty upload"})

    arr = np.frombuffer(contents, np.uint8)
    image_bgr = cv2.imdecode(arr, cv2.IMREAD_COLOR)

    if image_bgr is None:
        return JSONResponse(status_code=400, content={"error": "Invalid image decode"})

    detector_mode = get_detector_mode()
    h = hashlib.sha256(contents).hexdigest()[:12]

    try:
        if detector_mode == "mock":
            raise RuntimeError("DETECTOR_MODE=mock")

        result = detect_with_model(image_bgr)
        components = result.get("components", [])
        mode = "model"

    except Exception as e:
        components = [make_mock_component(h)]
        mode = "mock"

    # ---- DRAW BOXES ----
    annotated = image_bgr.copy()

    for comp in components:
        bbox = comp.get("bbox", [])
        if len(bbox) != 4:
            continue

        x, y, w, h_box = bbox
        label = comp.get("type", "unknown")
        conf = comp.get("confidence", 0.0)

        # Color: red = model, orange = mock
        color = (255, 0, 0) if mode == "model" else (0, 165, 255)

        # Draw rectangle
        cv2.rectangle(
            annotated,
            (x, y),
            (x + w, y + h_box),
            color,
            2
        )

        # Draw label
        text = f"{label} {conf:.2f}"
        text_y = max(20, y - 8)

        cv2.putText(
            annotated,
            text,
            (x, text_y),
            cv2.FONT_HERSHEY_SIMPLEX,
            0.6,
            color,
            2,
            cv2.LINE_AA
        )

    # Encode back to JPEG
    ok, encoded = cv2.imencode(".jpg", annotated)
    if not ok:
        return JSONResponse(status_code=500, content={"error": "Failed to encode image"})

    return Response(
        content=encoded.tobytes(),
        media_type="image/jpeg",
        headers={"X-Detection-Mode": mode}
    )

if __name__ == "__main__":
    uvicorn.run(app, host="0.0.0.0", port=8000)