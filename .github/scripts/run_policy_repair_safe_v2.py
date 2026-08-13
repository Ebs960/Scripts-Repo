#!/usr/bin/env python3
"""Semantic-gate hardened runner for the full policy repair."""
from __future__ import annotations

import run_policy_repair_safe as safe

repair = safe.repair

# Prefer concrete existing research for modern/future policy families.
repair.GATE_HINTS.update({
    "AI Governance": ["computers"],
    "AI Rights": ["computers"],
    "AI Weapons Doctrine": ["computers"],
    "Algorithm Regulation": ["computers"],
    "Digital Privacy": ["internet", "computers"],
    "Digital Identity System": ["internet", "computers"],
    "National Cyber Command": ["internet", "computers"],
    "Universal Internet Access": ["internet"],
    "Open Data": ["internet", "computers"],
    "Data Localization": ["internet", "computers"],
    "Platform Antitrust": ["internet", "computers"],
    "Remote Work Infrastructure": ["internet"],
    "Germline Modification": ["crispr"],
    "Genetic Privacy": ["crispr"],
    "Universal Gene Therapy": ["crispr"],
    "Human Genetic Preservation": ["crispr"],
})


def phrase_match(hint: str, candidate: str) -> bool:
    """Match short hints as tokens; longer hints may match phrases/stems."""
    h = repair.normalize(hint)
    c = repair.normalize(candidate)
    if not h or not c:
        return False
    if len(h) <= 3:
        return h in c.split()
    return h == c or h in c or c in h


def choose_gate(name: str, tag: int, cost: int, research):
    target = repair.normalize(name)
    exact = [r for r in research if r.normalized == target]
    if exact:
        prefer_culture = tag in {4, 7, 8, 10, 11, 14}
        exact.sort(key=lambda r: (
            0 if (r.kind == "Culture") == prefer_culture else 1,
            r.age,
            r.cost,
            r.name,
        ))
        return exact[0], "exact-name"

    target_age = repair.infer_age(name, cost)
    preferred = repair.PREFERRED_RESEARCH_CATEGORY.get(tag, 0)
    prefer_culture = tag in {4, 7, 8, 10, 11, 14}
    hints = repair.GATE_HINTS.get(name, [])
    hinted = [r for r in research if any(phrase_match(h, r.normalized) for h in hints)]
    if hinted:
        hinted.sort(key=lambda r: (
            abs(r.age - target_age),
            0 if r.category == preferred else 1,
            0 if (r.kind == "Culture") == prefer_culture else 1,
            r.cost,
            r.name,
        ))
        return hinted[0], "semantic-hint"

    # Last resort: choose a representative research asset from the intended era/domain.
    ranked = sorted(research, key=lambda r: (
        abs(r.age - target_age),
        0 if r.category == preferred else 1,
        0 if (r.kind == "Culture") == prefer_culture else 1,
        r.cost,
        r.name,
    ))
    return ranked[0], "era-category-fallback"


repair.choose_gate = choose_gate

if __name__ == "__main__":
    raise SystemExit(repair.main())
