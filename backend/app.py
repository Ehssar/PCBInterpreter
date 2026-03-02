from fastapi import FastAPI, File, UploadFile
from fastapi.responses import JSONResponse
import time, hashlib
import uvicorn

app = FastAPI()

@app.get("/health")
def health():
    return {"status": "ok"}

@app.post("/analyze")
async def analyze_image(file: UploadFile = File(...)):
    t0 = time.time()
    contents = await file.read()

    h = hashlib.sha256(contents).hexdigest()[:12]
    n = len(contents)

    # Deterministic “fake detection” from hash bytes
    seed = int(h[:6], 16)
    x = 50 + (seed % 200)
    y = 80 + ((seed // 3) % 200)
    w = 60 + ((seed // 7) % 80)
    hh = 20 + ((seed // 11) % 40)

    dt_ms = int((time.time() - t0) * 1000)

    print(f"Received {n} bytes | sha={h} | {file.filename} {file.content_type} | {dt_ms}ms")

    return JSONResponse({
        "request_id": h,
        "timing_ms": dt_ms,
        "image_bytes": n,
        "components": [
            {
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
        ]
    })

if __name__ == "__main__":
    uvicorn.run(app, host="0.0.0.0", port=8000)
