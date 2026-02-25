# PCB Interpreter (AR + AI)

Quest 3/3S Mixed Reality app that analyzes PCB components via passthrough camera
and server-side computer vision.

## Architecture
Unity Client → FastAPI Backend → CV Pipeline → Search

## Structure
- backend/ : FastAPI server
- Unity-PassthroughCameraApiSamples-main/ : Unity client