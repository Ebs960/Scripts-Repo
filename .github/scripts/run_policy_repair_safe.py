#!/usr/bin/env python3
"""Compatibility runner for repair_policy_content.py.

Unity serializes some enum arrays as packed little-endian hex and wraps long string
values onto continuation lines. Patch those parser edge cases at runtime, then run
the existing deterministic full-roster policy repair.
"""
from __future__ import annotations

import re
import repair_policy_content as repair


def policy_tags(text: str) -> list[int]:
    raw = repair.scalar(text, "policyTags")
    if raw and raw != "[]":
        if re.fullmatch(r"[0-9a-fA-F]+", raw) and len(raw) % 8 == 0:
            data = bytes.fromhex(raw)
            return [int.from_bytes(data[i:i + 4], "little") for i in range(0, len(data), 4)]
        try:
            return [int(raw)]
        except ValueError:
            pass

    values: list[int] = []
    for item in repair.array_items(text, "policyTags"):
        try:
            values.append(int(item))
        except ValueError:
            pass
    return values


def primary_tag(text: str) -> int:
    tags = policy_tags(text)
    return tags[0] if tags and tags[0] in repair.TAG_NAMES else 0


def description_text(text: str) -> str:
    lines = text.splitlines()
    for i, line in enumerate(lines):
        if not line.startswith("  description:"):
            continue
        parts = [line.split(":", 1)[1].strip()]
        j = i + 1
        while j < len(lines) and lines[j].startswith("    "):
            parts.append(lines[j].strip())
            j += 1
        return " ".join(part for part in parts if part).strip()
    return ""


def safe_set_scalar(text: str, field: str, value: str, after: str | None = None) -> str:
    if field != "description":
        return original_set_scalar(text, field, value, after)

    lines = text.splitlines()
    for i, line in enumerate(lines):
        if not line.startswith("  description:"):
            continue
        j = i + 1
        while j < len(lines) and lines[j].startswith("    "):
            j += 1
        lines[i:j] = [f"  description: {value}"]
        return "\n".join(lines) + ("\n" if text.endswith("\n") else "")
    raise RuntimeError("Missing description field")


def is_boilerplate(text: str) -> bool:
    desc = description_text(text).lower()
    return not desc or any(marker in desc for marker in repair.BOILERPLATE_MARKERS)


def safe_make_description(name: str, tag: int, text: str) -> str:
    # Avoid ':' in an unquoted Unity YAML scalar. The em dash is visually cleaner too.
    lead = repair.DOMAIN_LEADS.get(tag, repair.DOMAIN_LEADS[0])
    return f"{name} {lead}. Gameplay impact — {repair.gameplay_summary(text)}."


def safe_repair_policy(path, research):
    # A primitive policy is only a true start policy if it does not already carry a
    # structural gate. This prevents contradictory availableFromStart + prerequisite data.
    text = path.read_text(encoding="utf-8")
    name = repair.scalar(text, "policyName") or path.stem
    temporarily_remove = name in repair.START_POLICIES and repair.has_gate(text)
    if temporarily_remove:
        repair.START_POLICIES.remove(name)
    try:
        return original_repair_policy(path, research)
    finally:
        if temporarily_remove:
            repair.START_POLICIES.add(name)


original_set_scalar = repair.set_scalar
original_repair_policy = repair.repair_policy
repair.primary_tag = primary_tag
repair.set_scalar = safe_set_scalar
repair.is_boilerplate = is_boilerplate
repair.make_description = safe_make_description
repair.repair_policy = safe_repair_policy

# The currently misspelled asset has policyName "Algorithm Regulation" and is a true
# no-op. Give it an explicit digital-regulation tradeoff instead of a generic fallback.
repair.NO_OP_OVERRIDES["Algorithm Regulation"] = {
    "cyberDefenseModifier": 0.12,
    "corruptionModifier": -0.05,
    "productionModifier": -0.03,
}
repair.GATE_HINTS["Algorithm Regulation"] = ["algorithm", "artificial intelligence", "ai"]

if __name__ == "__main__":
    raise SystemExit(repair.main())
