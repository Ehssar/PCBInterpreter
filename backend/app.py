from fastapi import FastAPI, File, UploadFile
from fastapi.responses import JSONResponse
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

    return {
        "component_id": f"comp_{h}",
        "type": "unknown",
        "confidence": 0.50,
        "bbox": [x, y, w, hh],
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
        "timing_ms": timing_ms,
        "image_bytes": image_bytes,
        "mode": mode,
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


@app.post("/analyze")
async def analyze_image(file: UploadFile = File(...)):
    t0 = time.time()
    contents = await file.read()

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


if __name__ == "__main__":
    uvicorn.run(app, host="0.0.0.0", port=8000)