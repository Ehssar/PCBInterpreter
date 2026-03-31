from dotenv import load_dotenv
load_dotenv()
import json
import cv2

from cv.detector import detect_components_bgr


def main():
    image_path = "test_images/Test2.jpg"
    image_bgr = cv2.imread(image_path)

    if image_bgr is None:
        raise RuntimeError(f"Failed to load image: {image_path}")

    result = detect_components_bgr(image_bgr)

    print(json.dumps(result, indent=2))


if __name__ == "__main__":
    main()