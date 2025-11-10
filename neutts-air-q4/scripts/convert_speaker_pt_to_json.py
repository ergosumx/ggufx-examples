#!/usr/bin/env python3
"""Convert NeuTTS Air speaker checkpoints (.pt) into GGUFx-compatible JSON."""

from __future__ import annotations

import argparse
import json
import math
import re
import sys
import unicodedata
from dataclasses import dataclass
from pathlib import Path
from typing import Any, Iterable, Sequence

try:
    import torch  # type: ignore
except ModuleNotFoundError as exc:  # pragma: no cover - runtime diagnostic only
    raise SystemExit(
        "PyTorch is required to convert speaker checkpoints. Install torch first."
    ) from exc


FRAME_RATE_HZ = 50.0


def _to_serializable(value: Any) -> Any:
    if isinstance(value, torch.Tensor):
        if value.ndim == 0:
            return value.item()
        return value.cpu().tolist()
    if isinstance(value, dict):
        return {key: _to_serializable(inner) for key, inner in value.items()}
    if isinstance(value, (list, tuple)):
        return [_to_serializable(item) for item in value]
    if isinstance(value, float) and (math.isnan(value) or math.isinf(value)):
        return 0.0
    return value


@dataclass(frozen=True)
class LoadedCheckpoint:
    kind: str
    payload: Any


def _load_checkpoint(path: Path) -> LoadedCheckpoint:
    checkpoint = torch.load(path, map_location="cpu")

    if isinstance(checkpoint, dict):
        if "words" not in checkpoint:
            raise ValueError("Checkpoint does not contain required 'words' payload.")
        return LoadedCheckpoint("structured", checkpoint)

    if isinstance(checkpoint, torch.Tensor):
        if checkpoint.numel() == 0:
            raise ValueError("Speaker checkpoint tensor is empty.")
        return LoadedCheckpoint("codes", checkpoint.detach().cpu())

    if isinstance(checkpoint, (list, tuple)):
        tensor = torch.tensor(checkpoint, dtype=torch.int32)
        if tensor.numel() == 0:
            raise ValueError("Speaker checkpoint list is empty.")
        return LoadedCheckpoint("codes", tensor)

    raise ValueError(
        f"Unsupported checkpoint payload type '{type(checkpoint).__name__}'."
    )


def _normalise_checkpoint(checkpoint: dict[str, Any]) -> dict[str, Any]:
    result: dict[str, Any] = {
        "version": str(checkpoint.get("version", "0.2")),
        "words": _to_serializable(checkpoint["words"]),
    }
    if "speaker_id" in checkpoint:
        result["speaker_id"] = _to_serializable(checkpoint["speaker_id"])
    if "metadata" in checkpoint:
        result["metadata"] = _to_serializable(checkpoint["metadata"])
    return result


def _flatten_codes(raw_codes: Any) -> list[int]:
    if isinstance(raw_codes, torch.Tensor):
        return [int(x) for x in raw_codes.view(-1).tolist()]
    if isinstance(raw_codes, Iterable):
        return [int(x) for x in raw_codes]
    raise TypeError(f"Unable to flatten codes of type '{type(raw_codes).__name__}'.")


def _normalise_word(token: str) -> str:
    cleaned = token.replace("'", "")
    return cleaned.lower()


def _tokenise_transcript(transcript: str) -> list[str]:
    normalised = unicodedata.normalize("NFKD", transcript)
    ascii_text = normalised.encode("ascii", "ignore").decode("ascii")
    ascii_text = ascii_text.replace("\u2019", "'").replace("\u2018", "'")
    ascii_text = ascii_text.replace("\u201c", '"').replace("\u201d", '"')
    tokens = re.findall(r"[A-Za-z0-9']+", ascii_text)
    words: list[str] = []
    for token in tokens:
        cleaned = _normalise_word(token)
        if cleaned:
            words.append(cleaned)
    if not words:
        raise ValueError("Transcript does not contain any parsable word tokens.")
    return words


def _load_transcript(path: Path) -> tuple[str, list[str]]:
    text = path.read_text(encoding="utf-8").strip()
    words = _tokenise_transcript(text)
    return text, words


def _distribute_codes(total_codes: int, weights: Sequence[int]) -> list[int]:
    if total_codes <= 0:
        raise ValueError("Speaker checkpoint did not contain any codes.")
    if not weights:
        raise ValueError("Transcript token list is empty.")
    total_weight = sum(weights)
    if total_weight <= 0:
        raise ValueError("Transcript token weights are invalid.")

    shares = [weight * total_codes / total_weight for weight in weights]
    counts = [max(1, int(math.floor(share))) for share in shares]

    sum_counts = sum(counts)
    delta = total_codes - sum_counts

    if delta != 0:
        fractional = [share - math.floor(share) for share in shares]
        indexed = sorted(
            enumerate(fractional),
            key=lambda item: item[1],
            reverse=delta > 0,
        )

        idx_list = [index for index, _ in indexed]

        step = 1 if delta > 0 else -1
        remaining = abs(delta)
        pointer = 0
        while remaining > 0 and idx_list:
            idx = idx_list[pointer % len(idx_list)]
            proposed = counts[idx] + step
            if proposed <= 0:
                idx_list.pop(pointer % len(idx_list))
                continue
            counts[idx] = proposed
            remaining -= 1
            pointer += 1

    adjustment = total_codes - sum(counts)
    if adjustment != 0:
        counts[-1] += adjustment

    if any(count <= 0 for count in counts):
        raise ValueError("Code distribution resulted in non-positive segment length.")

    if sum(counts) != total_codes:
        raise ValueError("Failed to distribute codes across transcript tokens.")

    return counts


def _build_words_from_codes(codes: list[int], transcript_tokens: list[str]) -> list[dict[str, Any]]:
    weights = [max(1, len(token)) for token in transcript_tokens]
    counts = _distribute_codes(len(codes), weights)

    words: list[dict[str, Any]] = []
    offset = 0
    for token, count in zip(transcript_tokens, counts):
        segment = codes[offset : offset + count]
        offset += count
        duration = round(count / FRAME_RATE_HZ, 2)
        if duration <= 0:
            duration = round(1.0 / FRAME_RATE_HZ, 2)
        words.append(
            {
                "word": token,
                "duration": duration,
                "codes": segment,
            }
        )
    return words


def convert(
    pt_path: Path,
    json_path: Path,
    transcript_path: Path | None = None,
    speaker_id: str | None = None,
    version: str = "0.2",
) -> dict[str, Any]:
    loaded = _load_checkpoint(pt_path)

    if loaded.kind == "structured":
        payload = _normalise_checkpoint(loaded.payload)
        if speaker_id is not None:
            payload["speaker_id"] = speaker_id
    else:
        if transcript_path is None:
            raise ValueError(
                "Checkpoint contains raw codes. Provide --transcript to map codes to words."
            )

        transcript_raw, tokens = _load_transcript(transcript_path)
        codes = _flatten_codes(loaded.payload)
        words = _build_words_from_codes(codes, tokens)

        payload = {
            "version": version,
            "words": words,
            "metadata": {
                "source_pt": pt_path.name,
                "transcript": transcript_path.name,
                "strategy": "proportional-length",
            },
        }

        if speaker_id is not None:
            payload["speaker_id"] = speaker_id
        else:
            payload["speaker_id"] = transcript_path.stem

        excerpt = transcript_raw[:256].replace("\n", " ")
        payload["metadata"]["transcript_excerpt"] = excerpt

    json_path.parent.mkdir(parents=True, exist_ok=True)
    json_path.write_text(json.dumps(payload, indent=2, ensure_ascii=True), encoding="utf-8")
    return payload


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        description=(
            "Convert NeuTTS Air speaker .pt checkpoints into GGUFx-compatible JSON "
            "artifacts. The JSON can be consumed by the NeuTTS Air GGUFx sample."
        )
    )
    parser.add_argument(
        "checkpoint",
        type=Path,
        help="Path to the speaker .pt file (e.g., dave.pt).",
    )
    parser.add_argument(
        "--output",
        "-o",
        type=Path,
        help=(
            "Optional output JSON path. Defaults to the checkpoint location with "
            "a '.json' extension."
        ),
    )
    parser.add_argument(
        "--transcript",
        "-t",
        type=Path,
        help=(
            "Path to the transcript text that matches the reference audio. "
            "Required when converting raw NeuCodec tensors."
        ),
    )
    parser.add_argument(
        "--speaker-id",
        type=str,
        help="Optional speaker identifier to embed in the JSON payload.",
    )
    parser.add_argument(
        "--version",
        type=str,
        default="0.2",
        help="Speaker payload version to emit when converting raw tensors (default: 0.2).",
    )
    parser.add_argument(
        "--preview",
        action="store_true",
        help="Print a short summary of the converted payload to stdout.",
    )
    return parser


def main(argv: list[str] | None = None) -> int:
    parser = build_parser()
    args = parser.parse_args(argv)

    pt_path: Path = args.checkpoint.expanduser().resolve()
    if not pt_path.exists():
        parser.error(f"Checkpoint not found: {pt_path}")

    json_path = (
        args.output.expanduser().resolve()
        if args.output is not None
        else pt_path.with_suffix(".json")
    )

    transcript_path: Path | None = None
    if args.transcript is not None:
        transcript_path = args.transcript.expanduser().resolve()
        if not transcript_path.exists():
            parser.error(f"Transcript not found: {transcript_path}")

    payload = convert(
        pt_path,
        json_path,
        transcript_path=transcript_path,
        speaker_id=args.speaker_id,
        version=args.version,
    )

    if args.preview:
        words = payload.get("words", [])
        words_length = len(words) if isinstance(words, list) else 0
        first = words[0] if words_length > 0 else None
        print(
            json.dumps(
                {
                    "version": payload.get("version"),
                    "words": words_length,
                    "first_word": first,
                    "output": str(json_path),
                },
                indent=2,
            )
        )

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
