from fastapi import FastAPI, File, UploadFile
from fastapi.responses import JSONResponse
import uvicorn

app = FastAPI()

@app.get("/health")
def health():
    return {"status": "ok"}

@app.post("/analyze")
async def analyze_image(file: UploadFile = File(...)):
    contents = await file.read()

    print(f"Received {len(contents)} bytes")

    # Mock response
    return JSONResponse({
        "components": [
            {
                "component_id": "comp_001",
                "type": "resistor",
                "confidence": 0.93,
                "bbox": [120, 240, 60, 20],
                "candidates": [
                    {
                        "part_number": "R-10K-0603",
                        "confidence": 0.82,
                        "datasheet_url": "https://example.com/datasheet"
                    }
                ]
            }
        ]
    })

if __name__ == "__main__":
    uvicorn.run(app, host="0.0.0.0", port=8000)
