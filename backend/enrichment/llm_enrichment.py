from __future__ import annotations

import base64
import json
import os
from typing import Any

import cv2
import numpy as np
from openai import OpenAI

OPENAI_ENRICH_MODEL = os.getenv("OPENAI_ENRICH_MODEL", "gpt-5.4-mini")
OPENAI_ENRICH_IMAGE_DETAIL = os.getenv("OPENAI_ENRICH_IMAGE_DETAIL", "low")

_client: OpenAI | None = None


def _get_client() -> OpenAI:
    global _client
    if _client is None:
        _client = OpenAI()
    return _client


def _bgr_to_jpeg_bytes(image_bgr: np.ndarray, quality: int = 90) -> bytes:
    ok, encoded = cv2.imencode(".jpg", image_bgr, [int(cv2.IMWRITE_JPEG_QUALITY), quality])
    if not ok:
        raise ValueError("Failed to JPEG-encode image for LLM enrichment")
    return encoded.tobytes()


def _jpeg_bytes_to_data_url(image_bytes: bytes) -> str:
    b64 = base64.b64encode(image_bytes).decode("utf-8")
    return f"data:image/jpeg;base64,{b64}"


def _image_bgr_to_data_url(image_bgr: np.ndarray, quality: int = 90) -> str:
    return _jpeg_bytes_to_data_url(_bgr_to_jpeg_bytes(image_bgr, quality=quality))


def _sanitize_string_list(value: Any) -> list[str]:
    if not isinstance(value, list):
        return []
    out: list[str] = []
    for item in value:
        if isinstance(item, str) and item.strip():
            out.append(item.strip())
    return out


def _normalize_enrichment_payload(payload: dict[str, Any]) -> dict[str, Any]:
    """
    Makes the LLM response safe for your backend + Unity pipeline.
    """
    attributes = payload.get("attributes") or {}

    return {
        "resolved_type": payload.get("resolved_type"),
        "display_name": payload.get("display_name"),
        "one_line_label": payload.get("one_line_label"),
        "function_summary": payload.get("function_summary"),
        "confidence_note": payload.get("confidence_note"),
        "ocr_text": payload.get("ocr_text"),
        "datasheet_url": payload.get("datasheet_url"),
        "needs_human_verification": bool(payload.get("needs_human_verification", True)),
        "datasheet_search_terms": _sanitize_string_list(payload.get("datasheet_search_terms")),
        "attributes": {
            "package": attributes.get("package"),
            "package_confidence": float(attributes["package_confidence"]) if attributes.get("package_confidence") is not None else None,
            "marking_text": attributes.get("marking_text"),
            "part_family": attributes.get("part_family"),
            "part_family_confidence": float(attributes["part_family_confidence"]) if attributes.get("part_family_confidence") is not None else None,
            "electrical_value": attributes.get("electrical_value"),
            "electrical_value_confidence": float(attributes["electrical_value_confidence"]) if attributes.get("electrical_value_confidence") is not None else None,
            "likely_role": attributes.get("likely_role"),
            "likely_role_confidence": float(attributes["likely_role_confidence"]) if attributes.get("likely_role_confidence") is not None else None,
            "mount_type": attributes.get("mount_type"),
            "pin_count": int(attributes["pin_count"]) if attributes.get("pin_count") is not None else None,
            "polarized": attributes.get("polarized"),
        },
    }


ENRICHMENT_SCHEMA: dict[str, Any] = {
    "type": "object",
    "additionalProperties": False,
    "required": [
        "resolved_type",
        "display_name",
        "one_line_label",
        "function_summary",
        "confidence_note",
        "ocr_text",
        "datasheet_url",
        "needs_human_verification",
        "datasheet_search_terms",
        "attributes",
    ],
    "properties": {
        "resolved_type": {"type": ["string", "null"]},
        "display_name": {"type": ["string", "null"]},
        "one_line_label": {"type": ["string", "null"]},
        "function_summary": {"type": ["string", "null"]},
        "confidence_note": {"type": ["string", "null"]},
        "ocr_text": {"type": ["string", "null"]},
        "datasheet_url": {"type": ["string", "null"]},
        "needs_human_verification": {"type": "boolean"},
        "datasheet_search_terms": {
            "type": "array",
            "items": {"type": "string"},
        },
        "attributes": {
            "type": "object",
            "additionalProperties": False,
            "required": [
                "package",
                "package_confidence",
                "marking_text",
                "part_family",
                "part_family_confidence",
                "electrical_value",
                "electrical_value_confidence",
                "likely_role",
                "likely_role_confidence",
                "mount_type",
                "pin_count",
                "polarized",
            ],
            "properties": {
                "package": {"type": ["string", "null"]},
                "package_confidence": {"type": ["number", "null"]},
                "marking_text": {"type": ["string", "null"]},
                "part_family": {"type": ["string", "null"]},
                "part_family_confidence": {"type": ["number", "null"]},
                "electrical_value": {"type": ["string", "null"]},
                "electrical_value_confidence": {"type": ["number", "null"]},
                "likely_role": {"type": ["string", "null"]},
                "likely_role_confidence": {"type": ["number", "null"]},
                "mount_type": {"type": ["string", "null"]},
                "pin_count": {"type": ["integer", "null"]},
                "polarized": {"type": ["boolean", "null"]},
            },
        },
    },
}


def enrich_component(
    component: dict[str, Any],
    component_crop_bgr: np.ndarray,
    neighborhood_crop_bgr: np.ndarray | None = None,
) -> dict[str, Any]:
    """
    Drop-in backend helper.

    Inputs:
      - component: one normalized component dict from detector.py
      - component_crop_bgr: tight crop around the detected component
      - neighborhood_crop_bgr: optional wider crop for local context

    Returns:
      - enrichment dict matching your schema
    """
    client = _get_client()

    component_type = component.get("type", "unknown")
    source_label = component.get("source_label", "unknown")
    confidence = float(component.get("confidence", 0.0))
    bbox = component.get("bbox", [0, 0, 0, 0])

    component_image_url = _image_bgr_to_data_url(component_crop_bgr)
    neighborhood_image_url = (
        _image_bgr_to_data_url(neighborhood_crop_bgr)
        if neighborhood_crop_bgr is not None
        else None
    )

    developer_prompt = (
        "You enrich PCB component detections for an AR assistant.\n"
        "Use the detector's type and source_label as a strong prior, but do not invent exact part numbers.\n"
        "Your output will appear on small floating labels, so be concise, practical, and conservative.\n"
        "Primary evidence: tight component crop.\n"
        "Secondary evidence: neighborhood crop.\n"
        "If uncertain, leave fields null and set needs_human_verification to true.\n"
        "The field ocr_text means only text physically printed on the component body itself.\n"
        "Do not include PCB silkscreen, board labels, logos, watermark text, packaging text, or any background text.\n"
        "Do not guess unreadable markings.\n"
        "Avoid repeating the same information across display_name, one_line_label, function_summary, and confidence_note.\n"
        "Field expectations:\n"
        "- resolved_type is the canonical category used for UI filtering.\n"
        "It must be exactly one of: resistor, capacitor, ic, diode, transistor, inductor, led, connector, unknown.\n"
        "Use the detector type when it is clearly correct.\n"
        "If detector type is unknown but the component is visually identifiable, assign the best canonical category.\n"
        "If uncertain, use unknown.\n"
        "- display_name: short noun phrase, ideally 2 to 4 words.\n"
        "- one_line_label: compact user-facing label, ideally no more than 6 words.\n"
        "- function_summary: one or two short sentences, ideally 12 to 22 words total.\n"
        "  Prefer 15 to 18 words when that improves clarity.\n"
        "  Use the available space to say the component's likely role on this board.\n"
        "  Do not pad with generic filler or textbook definitions.\n"
        "- datasheet_search_terms: 1 to 3 short search phrases.\n"
        "Return only valid JSON matching the supplied schema."
    )

    user_context = {
        "detector_type": component_type,
        "source_label": source_label,
        "detector_confidence": confidence,
        "bbox": bbox,
    }

    user_text = (
        "Analyze this PCB component and return structured enrichment.\n"
        f"Context:\n{json.dumps(user_context, ensure_ascii=False)}\n\n"
        "Good behavior:\n"
        "- Prefer broad, useful identities like 'Ceramic Capacitor', 'SOIC-8 IC', or 'Likely Microcontroller'.\n"
        "- Only provide electrical_value when supported by visible marking or very strong evidence.\n"
        "- likely_role may be things like decoupling capacitor, pull-up resistor, voltage regulator, connector.\n"
        "- datasheet_search_terms should be short search phrases, not URLs.\n"
        "- ocr_text must contain only markings printed directly on the component itself.\n"
        "- Ignore text printed elsewhere on the PCB or elsewhere in the image.\n"
        "- Do not repeat the same phrase across multiple fields unless necessary.\n"
        "- If the role is uncertain, say 'likely' briefly rather than overstating confidence.\n"
    )

    content: list[dict[str, Any]] = [
        {"type": "input_text", "text": user_text},
        {
            "type": "input_image",
            "image_url": component_image_url,
            "detail": OPENAI_ENRICH_IMAGE_DETAIL,
        },
    ]

    if neighborhood_image_url is not None:
        content.append(
            {
                "type": "input_text",
                "text": "Neighborhood crop for local PCB context.",
            }
        )
        content.append(
            {
                "type": "input_image",
                "image_url": neighborhood_image_url,
                "detail": OPENAI_ENRICH_IMAGE_DETAIL,
            }
        )

    response = client.responses.create(
        model=OPENAI_ENRICH_MODEL,
        input=[
            {
                "role": "developer",
                "content": [{"type": "input_text", "text": developer_prompt}],
            },
            {
                "role": "user",
                "content": content,
            },
        ],
        text={
            "format": {
                "type": "json_schema",
                "name": "pcb_component_enrichment",
                "strict": True,
                "schema": ENRICHMENT_SCHEMA,
            }
        },
    )

    payload = json.loads(response.output_text)
    return _normalize_enrichment_payload(payload)