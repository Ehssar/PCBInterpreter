from fastapi import FastAPI, File, UploadFile
from fastapi.responses import JSONResponse
import time
import hashlib
import uvicorn
import numpy as np
import cv2

from cv.detector import detect_components_bgr

app = FastAPI()

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

@app.get("/health")
def health():
    return {"status": "ok"}


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

    try:
        arr = np.frombuffer(contents, np.uint8)
        image_bgr = cv2.imdecode(arr, cv2.IMREAD_COLOR)

        if image_bgr is None:
            fallback_reason = "invalid_image_decode"
            raise ValueError("OpenCV could not decode uploaded image")

        result = detect_components_bgr(image_bgr)

        dt_ms = int((time.time() - t0) * 1000)

        print(
            f"Received {n} bytes | sha={h} | {file.filename} {file.content_type} "
            f"| components={len(result.get('components', []))} | mode=model | {dt_ms}ms"
        )

        return JSONResponse({
            "request_id": h,
            "timing_ms": dt_ms,
            "image_bytes": n,
            "mode": "model",
            "components": result.get("components", [])
        })

    except Exception as e:
        dt_ms = int((time.time() - t0) * 1000)

        if fallback_reason is None:
            fallback_reason = f"{type(e).__name__}: {str(e)}"

        mock_component = make_mock_component(h)

        print(
            f"FALLBACK | sha={h} | {file.filename} {file.content_type} "
            f"| bytes={n} | mode=mock | reason={fallback_reason} | {dt_ms}ms"
        )

        return JSONResponse({
            "request_id": h,
            "timing_ms": dt_ms,
            "image_bytes": n,
            "mode": "mock",
            "fallback_reason": fallback_reason,
            "components": [mock_component]
        })

if __name__ == "__main__":
    uvicorn.run(app, host="0.0.0.0", port=8000)