#!/usr/bin/env python3
"""Audit and repair the full PolicyData asset roster.

The script is intentionally conservative:
- Existing nonzero gameplay effects are preserved, except clearly extreme percentage values.
- Only true no-op policies receive a small effect package based on their primary policy domain.
- Existing progression gates are preserved.
- Ungated policies are bound to an exact/similar existing TechData or CultureData asset where possible;
  a small explicit whitelist is allowed from game start.
- Boilerplate descriptions are replaced with text that states the actual configured gameplay impact.
"""

from __future__ import annotations

import re
from dataclasses import dataclass
from pathlib import Path
from typing import Iterable

ROOT = Path(__file__).resolve().parents[2]
POLICIES = ROOT / "Policies"

FLOAT_EFFECTS = [
    "attackBonus", "meleeAttackBonus", "rangedAttackBonus", "cityAttackBonus", "defenseBonus", "movementBonus",
    "foodModifier", "productionModifier", "goldModifier", "scienceModifier", "cultureModifier", "faithModifier",
    "populationGrowthModifier", "migrationAttractionModifier", "warWearinessModifier", "corruptionModifier", "unrestModifier",
    "administrativeEfficiencyModifier", "distanceLoyaltyPenaltyModifier", "policyPointGenerationModifier",
    "domesticTradeModifier", "foreignTradeModifier", "laborProductivityModifier", "unemploymentUnhappinessModifier",
    "reinforcementSpeedModifier", "militaryUpkeepModifier", "cyberDefenseModifier", "cyberOffenseModifier",
    "espionageDefenseModifier", "orbitalProductionModifier", "interplanetaryTradeModifier", "planetaryLoyaltyModifier",
    "planetaryDefenseModifier", "herdStarvationPercentReduction",
]
INT_EFFECTS = ["tradeRouteCapacityBonus", "additionalGovernorSlots"]
ARRAY_EFFECTS = [
    "tileYieldBonuses", "buildingBonuses", "unitYieldBonuses", "unitBonuses", "equipmentYieldBonuses",
    "workerYieldBonuses", "workerBonuses", "diseaseBonuses", "attritionBonuses", "cityBonuses",
    "nonStateReligionUnhappinessModifiers", "herdYieldBonuses", "unlockedGovernorTraits", "governorOpinionEffects",
]
GATE_ARRAYS = ["requiredTechs", "requiredCultures", "requiredGovernments", "religiousRequirementGroups", "requiredPolicies"]

TAG_NAMES = {
    0: "Administration", 1: "Agriculture", 2: "Colonial", 3: "Economy", 4: "Education",
    5: "Environment", 6: "Infrastructure", 7: "Labor", 8: "Law", 9: "Military",
    10: "Religion", 11: "Rights", 12: "Security", 13: "Trade", 14: "Welfare",
    15: "Digital", 16: "Synthetic", 17: "Genetics", 18: "Space",
}

# TechCategory enum values: General, Military, Economic, Cultural, Religious, Scientific, Infrastructure, Political.
PREFERRED_RESEARCH_CATEGORY = {
    0: 7, 1: 2, 2: 7, 3: 2, 4: 3, 5: 5, 6: 6, 7: 3, 8: 7, 9: 1,
    10: 4, 11: 3, 12: 1, 13: 2, 14: 3, 15: 5, 16: 5, 17: 5, 18: 5,
}

START_POLICIES = {
    "Elder Deliberation",
    "Oral Law",
    "Ritual Calendar",
    "Tribal Tribute",
    "War Chief's Retinue",
}

# Used only if the policy is otherwise a true no-op. Values are intentionally modest.
NO_OP_OVERRIDES: dict[str, dict[str, float | int]] = {
    "Oral Law": {"cultureModifier": 0.06, "policyPointGenerationModifier": 0.05},
    "Factory Acts": {"unrestModifier": -0.06, "populationGrowthModifier": 0.03, "laborProductivityModifier": -0.02},
    "Trade Unions": {"laborProductivityModifier": 0.04, "unrestModifier": -0.06, "goldModifier": -0.03},
    "Conscription": {"reinforcementSpeedModifier": 0.15, "militaryUpkeepModifier": -0.05, "unrestModifier": 0.06, "warWearinessModifier": 0.05},
    "AI Rights": {"scienceModifier": 0.04, "laborProductivityModifier": 0.05, "unrestModifier": -0.05},
    "Standing Army": {"attackBonus": 0.08, "defenseBonus": 0.05, "militaryUpkeepModifier": 0.10},
    "Public Sanitation": {"populationGrowthModifier": 0.08, "foodModifier": 0.03},
    "State Bureaucracy": {"administrativeEfficiencyModifier": 0.10, "corruptionModifier": -0.05, "policyPointGenerationModifier": 0.05},
    "Women's Suffrage": {"cultureModifier": 0.05, "migrationAttractionModifier": 0.03, "unrestModifier": -0.04},
    "System Governors": {"planetaryLoyaltyModifier": 0.10, "administrativeEfficiencyModifier": 0.05},
    "Solar Free Trade": {"interplanetaryTradeModifier": 0.15, "goldModifier": 0.08},
    "Naval Impressment": {"militaryUpkeepModifier": -0.05, "unrestModifier": 0.05, "rangedAttackBonus": 0.05},
    "Central Banking": {"goldModifier": 0.08, "corruptionModifier": -0.02},
    "Free Trade": {"foreignTradeModifier": 0.12, "goldModifier": 0.08, "domesticTradeModifier": -0.03},
    "Mercantilism": {"domesticTradeModifier": 0.08, "foreignTradeModifier": -0.05, "goldModifier": 0.06},
    "Protectionism": {"domesticTradeModifier": 0.10, "foreignTradeModifier": -0.10, "productionModifier": 0.04},
    "Natural Rights": {"cultureModifier": 0.05, "unrestModifier": -0.05, "migrationAttractionModifier": 0.03},
    "Abolitionism": {"laborProductivityModifier": 0.05, "migrationAttractionModifier": 0.06, "unrestModifier": 0.02},
    "Genetic Privacy": {"scienceModifier": -0.03, "unrestModifier": -0.04},
    "Digital Privacy": {"cyberDefenseModifier": 0.08, "scienceModifier": -0.02, "unrestModifier": -0.03},
    "Navigation Acts": {"foreignTradeModifier": 0.08, "goldModifier": 0.05, "tradeRouteCapacityBonus": 1},
    "Imperial Roads": {"movementBonus": 0.10, "domesticTradeModifier": 0.06, "administrativeEfficiencyModifier": 0.04},
}

FALLBACK_EFFECTS: dict[int, dict[str, float | int]] = {
    0: {"administrativeEfficiencyModifier": 0.08, "corruptionModifier": -0.05},
    1: {"foodModifier": 0.10, "productionModifier": -0.03},
    2: {"distanceLoyaltyPenaltyModifier": -0.10, "goldModifier": 0.05, "unrestModifier": 0.04},
    3: {"goldModifier": 0.08, "productionModifier": 0.03, "unrestModifier": 0.03},
    4: {"scienceModifier": 0.08, "goldModifier": -0.04},
    5: {"foodModifier": 0.05, "productionModifier": -0.04},
    6: {"productionModifier": 0.06, "domesticTradeModifier": 0.04},
    7: {"laborProductivityModifier": 0.06, "unrestModifier": -0.04, "goldModifier": -0.03},
    8: {"corruptionModifier": -0.06, "unrestModifier": -0.04, "policyPointGenerationModifier": 0.03},
    9: {"attackBonus": 0.05, "reinforcementSpeedModifier": 0.05, "militaryUpkeepModifier": 0.05},
    10: {"faithModifier": 0.10, "cultureModifier": 0.03},
    11: {"migrationAttractionModifier": 0.05, "unrestModifier": -0.05, "goldModifier": -0.02},
    12: {"defenseBonus": 0.05, "espionageDefenseModifier": 0.08, "cultureModifier": -0.02},
    13: {"domesticTradeModifier": 0.05, "foreignTradeModifier": 0.08, "goldModifier": 0.05},
    14: {"populationGrowthModifier": 0.04, "unrestModifier": -0.05, "goldModifier": -0.05},
    15: {"scienceModifier": 0.05, "cyberDefenseModifier": 0.08},
    16: {"laborProductivityModifier": 0.08, "scienceModifier": 0.05, "unrestModifier": 0.03},
    17: {"populationGrowthModifier": 0.05, "scienceModifier": 0.05},
    18: {"orbitalProductionModifier": 0.08, "planetaryLoyaltyModifier": 0.04, "goldModifier": -0.04},
}

DOMAIN_LEADS = {
    0: "reorganizes state administration and the institutions that carry out public policy",
    1: "changes how the state organizes farming, food supply, and rural production",
    2: "changes how overseas territories and distant possessions are governed",
    3: "changes the state's role in markets, finance, and production",
    4: "changes how education and public knowledge are organized",
    5: "changes how development is balanced against environmental and resource pressures",
    6: "changes how the state builds and operates shared infrastructure",
    7: "changes the legal and economic relationship between workers, employers, and the state",
    8: "changes the institutions used to make, enforce, and interpret law",
    9: "changes how armed forces are raised, organized, and sustained",
    10: "changes the formal relationship between religion and public authority",
    11: "changes the rights, protections, and political standing recognized by the state",
    12: "changes how the state protects itself against internal and external threats",
    13: "changes the rules governing merchants, trade routes, and commercial exchange",
    14: "expands or restructures public social provision",
    15: "changes how digital systems, data, and networked institutions are governed",
    16: "changes the role of synthetic people and automated systems in society",
    17: "changes how genetic technologies are regulated and used",
    18: "changes how off-world settlements, industry, and interplanetary institutions are governed",
}

# Hints are searched against existing TechData/CultureData names; if none match, an era/category fallback is used.
GATE_HINTS: dict[str, list[str]] = {
    "Census Officials": ["census", "bureaucracy", "administration"],
    "Citizen Assembly": ["citizenship", "republic", "democracy", "civic"],
    "Citizen Militia": ["militia", "citizenship"],
    "Civic Education": ["education", "civic", "citizenship"],
    "Constitutional Rights": ["constitution", "natural rights", "rights"],
    "Crown Workshops": ["workshop", "guild"],
    "Elite Estates": ["aristocracy", "nobility", "estate"],
    "Feudal Levy": ["feudal", "levy"],
    "Holy Law": ["religion", "theology", "law"],
    "Imperial Roads": ["roads", "road"],
    "Jury Courts": ["jury", "courts", "law"],
    "Manorial Dues": ["feudal", "manor"],
    "Mercenary Contracts": ["mercenary", "contracts"],
    "Merchant Privileges": ["merchant", "trade"],
    "Military Aristocracy": ["aristocracy", "nobility"],
    "Monastic Schools": ["monastic", "monastery", "education"],
    "Noble Retinues": ["nobility", "retinue", "feudal"],
    "Religious Tolerance": ["tolerance", "enlightenment", "pluralism"],
    "Royal Granaries": ["granary", "agriculture"],
    "Royal Intendants": ["administration", "bureaucracy"],
    "Sacred Kingship": ["monarchy", "kingship", "religion"],
    "Scutage": ["feudal", "coinage"],
    "Senatorial Patronage": ["republic", "senate"],
    "Serfdom": ["feudal", "manor"],
    "Slavery": ["slavery", "bronze"],
    "Printing Licenses": ["printing", "press"],
    "Freedom of the Press": ["printing", "press"],
    "Natural Rights": ["natural rights", "enlightenment"],
    "Separation of Powers": ["separation", "constitution", "enlightenment"],
    "Universal Suffrage": ["suffrage", "democracy"],
    "Women's Suffrage": ["suffrage", "women"],
    "Factory Acts": ["factory", "industrial"],
    "Trade Unions": ["union", "labor", "industrial"],
    "Minimum Wage": ["labor", "industrial"],
    "Eight Hour Workday": ["labor", "industrial"],
    "Public Transit": ["public transit", "mass transit"],
    "National Railways": ["railway", "railroad"],
    "Public Sanitation": ["sanitation", "public health"],
    "Municipal Water Systems": ["water", "sanitation"],
    "AI Governance": ["artificial intelligence", "ai", "machine learning"],
    "AI Rights": ["artificial intelligence", "ai"],
    "AI Weapons Doctrine": ["artificial intelligence", "warbots", "ai"],
    "Algorithim Regulation": ["algorithm", "artificial intelligence", "ai"],
    "Digital Privacy": ["privacy", "internet", "computers"],
    "Digital Identity System": ["internet", "computers", "digital"],
    "National Cyber Command": ["cyber", "internet", "computers"],
    "Universal Internet Access": ["internet"],
    "Germline Modification": ["genetic", "genome", "biotechnology"],
    "Genetic Privacy": ["genetic", "genome", "biotechnology"],
    "Universal Gene Therapy": ["gene", "genetic", "biotechnology"],
    "Orbital Industrialization": ["orbital", "space", "rocketry"],
    "Asteroid Mining Rights": ["asteroid", "space mining", "space"],
    "Planetary Administration": ["planetary", "colonization", "space"],
    "Planetary Autonomy": ["planetary", "colonization", "space"],
    "Planetary Defense Doctrine": ["planetary", "space", "defense"],
    "Interplanetary Citizenship": ["interplanetary", "colonization", "space"],
    "Solar Free Trade": ["interplanetary", "space", "solar"],
    "System Governors": ["interstellar", "space", "colonization"],
}

BOILERPLATE_MARKERS = (
    "apply while active",
    "while the policy is active",
    "durable institution; its benefits",
    "durable institution; its tradeoffs",
    "stated tradeoffs",
)

@dataclass(frozen=True)
class ResearchAsset:
    kind: str  # Tech or Culture
    name: str
    normalized: str
    age: int
    category: int
    cost: int
    guid: str
    path: Path


def normalize(value: str) -> str:
    return re.sub(r"[^a-z0-9]+", " ", value.lower()).strip()


def scalar(text: str, field: str) -> str | None:
    match = re.search(rf"(?m)^  {re.escape(field)}:\s*(.*)$", text)
    return match.group(1).strip() if match else None


def number(text: str, field: str) -> float:
    raw = scalar(text, field)
    if raw is None or raw == "":
        return 0.0
    try:
        return float(raw)
    except ValueError:
        return 0.0


def array_items(text: str, field: str) -> list[str]:
    lines = text.splitlines()
    for i, line in enumerate(lines):
        if not line.startswith(f"  {field}:"):
            continue
        tail = line.split(":", 1)[1].strip()
        if tail == "[]":
            return []
        if tail:
            return [tail]
        out: list[str] = []
        for child in lines[i + 1:]:
            if child.startswith("  -"):
                out.append(child[3:].strip())
                continue
            if child.startswith("    "):
                continue
            break
        return out
    return []


def set_scalar(text: str, field: str, value: str, after: str | None = None) -> str:
    pattern = rf"(?m)^(  {re.escape(field)}:)\s*.*$"
    if re.search(pattern, text):
        return re.sub(pattern, rf"\1 {value}", text, count=1)
    if after:
        anchor = re.search(rf"(?m)^  {re.escape(after)}:.*$", text)
        if anchor:
            end = anchor.end()
            return text[:end] + f"\n  {field}: {value}" + text[end:]
    raise RuntimeError(f"Could not set missing field {field}")


def set_array_reference(text: str, field: str, guid: str) -> str:
    lines = text.splitlines()
    for i, line in enumerate(lines):
        if not line.startswith(f"  {field}:"):
            continue
        # Preserve any existing non-empty array.
        if array_items(text, field):
            return text
        lines[i] = f"  {field}:"
        lines.insert(i + 1, f"  - {{fileID: 11400000, guid: {guid}, type: 2}}")
        return "\n".join(lines) + ("\n" if text.endswith("\n") else "")
    raise RuntimeError(f"Missing expected array field {field}")


def primary_tag(text: str) -> int:
    items = array_items(text, "policyTags")
    if not items:
        return 0
    try:
        return int(items[0])
    except ValueError:
        return 0


def has_effect(text: str) -> bool:
    if any(abs(number(text, field)) > 1e-8 for field in FLOAT_EFFECTS + INT_EFFECTS):
        return True
    return any(array_items(text, field) for field in ARRAY_EFFECTS)


def has_gate(text: str) -> bool:
    if int(number(text, "requiredCityCount")) > 0:
        return True
    return any(array_items(text, field) for field in GATE_ARRAYS)


def is_boilerplate(text: str) -> bool:
    desc = (scalar(text, "description") or "").lower()
    return not desc or any(marker in desc for marker in BOILERPLATE_MARKERS)


def load_research_assets() -> list[ResearchAsset]:
    assets: list[ResearchAsset] = []
    for kind, folder, name_field, age_field, cost_field in [
        ("Tech", ROOT / "Tech", "techName", "techAge", "scienceCost"),
        ("Culture", ROOT / "Culture", "cultureName", "cultureAge", "cultureCost"),
    ]:
        if not folder.exists():
            continue
        for path in folder.rglob("*.asset"):
            text = path.read_text(encoding="utf-8")
            raw_name = scalar(text, name_field) or path.stem
            meta = path.with_suffix(path.suffix + ".meta")
            if not meta.exists():
                continue
            guid_match = re.search(r"(?m)^guid:\s*([a-fA-F0-9]+)\s*$", meta.read_text(encoding="utf-8"))
            if not guid_match:
                continue
            assets.append(ResearchAsset(
                kind=kind,
                name=raw_name,
                normalized=normalize(raw_name),
                age=int(number(text, age_field)),
                category=int(number(text, "category")),
                cost=int(number(text, cost_field)),
                guid=guid_match.group(1),
                path=path,
            ))
    if not assets:
        raise RuntimeError("No TechData/CultureData assets found")
    return assets


def infer_age(name: str, cost: int) -> int:
    n = normalize(name)
    groups = [
        (18, ["interstellar", "system governor"]),
        (17, ["planetary", "interplanetary", "orbital", "asteroid", "solar free trade"]),
        (16, ["synthetic", "germline", "genetic", "gene therapy", "automated bureaucracy", "automation"]),
        (15, ["digital", "cyber", "internet", "algorithm", "algorithim", "ai ", "artificial intelligence", "platform", "remote work", "data localization"]),
        (14, ["public transit", "healthcare", "social housing", "public pension", "unemployment", "civil rights"]),
        (13, ["factory", "industrial", "railway", "trade union", "minimum wage", "eight hour", "workplace safety", "conscription", "professional officer"]),
        (11, ["natural rights", "constitutional", "suffrage", "separation of powers", "freedom of", "abolition"]),
        (10, ["colonial", "chartered compan", "navigation acts", "mercantil", "printing"]),
        (8, ["castle", "noble", "retinue"]),
        (7, ["feudal", "manorial", "serf", "scutage", "guild"]),
        (5, ["senatorial", "citizen assembly", "jury", "imperial roads", "census"]),
        (3, ["sacred kingship", "slavery", "royal granar"]),
        (1, ["elder", "oral law", "ritual calendar", "tribal", "war chief"]),
    ]
    for age, words in groups:
        if any(word in n for word in words):
            return age
    if cost >= 80: return 17
    if cost >= 70: return 16
    if cost >= 60: return 15
    if cost >= 50: return 13
    if cost >= 42: return 11
    if cost >= 34: return 9
    if cost >= 26: return 6
    if cost >= 20: return 3
    return 1


def choose_gate(name: str, tag: int, cost: int, research: list[ResearchAsset]) -> tuple[ResearchAsset, str]:
    target = normalize(name)
    exact = [r for r in research if r.normalized == target]
    if exact:
        # Exact TechData is preferred when both types share a name, except primarily cultural/legal/rights/religion domains.
        prefer_culture = tag in {4, 7, 8, 10, 11, 14}
        exact.sort(key=lambda r: (0 if (r.kind == "Culture") == prefer_culture else 1, r.age, r.name))
        return exact[0], "exact-name"

    hints = [normalize(h) for h in GATE_HINTS.get(name, [])]
    if hints:
        hinted = [r for r in research if any(h == r.normalized or h in r.normalized or r.normalized in h for h in hints)]
        if hinted:
            target_age = infer_age(name, cost)
            preferred = PREFERRED_RESEARCH_CATEGORY.get(tag, 0)
            prefer_culture = tag in {4, 7, 8, 10, 11, 14}
            hinted.sort(key=lambda r: (
                abs(r.age - target_age),
                0 if r.category == preferred else 1,
                0 if (r.kind == "Culture") == prefer_culture else 1,
                r.cost,
                r.name,
            ))
            return hinted[0], "keyword-hint"

    target_age = infer_age(name, cost)
    preferred = PREFERRED_RESEARCH_CATEGORY.get(tag, 0)
    prefer_culture = tag in {4, 7, 8, 10, 11, 14}
    ranked = sorted(research, key=lambda r: (
        abs(r.age - target_age),
        0 if r.category == preferred else 1,
        0 if (r.kind == "Culture") == prefer_culture else 1,
        r.cost,
        r.name,
    ))
    return ranked[0], "era-category-fallback"


def effect_label(field: str) -> str:
    labels = {
        "attackBonus": "Attack", "meleeAttackBonus": "Melee Attack", "rangedAttackBonus": "Ranged Attack",
        "cityAttackBonus": "City Attack", "defenseBonus": "Defense", "movementBonus": "Movement",
        "foodModifier": "Food", "productionModifier": "Production", "goldModifier": "Gold", "scienceModifier": "Science",
        "cultureModifier": "Culture", "faithModifier": "Faith", "populationGrowthModifier": "Population Growth",
        "migrationAttractionModifier": "Migration Attraction", "warWearinessModifier": "War Weariness",
        "corruptionModifier": "Corruption", "unrestModifier": "Unrest", "administrativeEfficiencyModifier": "Administrative Efficiency",
        "distanceLoyaltyPenaltyModifier": "Distance Loyalty Penalty", "policyPointGenerationModifier": "Policy Point Generation",
        "domesticTradeModifier": "Domestic Trade", "foreignTradeModifier": "Foreign Trade",
        "tradeRouteCapacityBonus": "Trade Route Capacity", "laborProductivityModifier": "Labor Productivity",
        "unemploymentUnhappinessModifier": "Unemployment Unhappiness", "reinforcementSpeedModifier": "Reinforcement Speed",
        "militaryUpkeepModifier": "Military Upkeep", "cyberDefenseModifier": "Cyber Defense", "cyberOffenseModifier": "Cyber Offense",
        "espionageDefenseModifier": "Espionage Defense", "orbitalProductionModifier": "Orbital Production",
        "interplanetaryTradeModifier": "Interplanetary Trade", "planetaryLoyaltyModifier": "Planetary Loyalty",
        "planetaryDefenseModifier": "Planetary Defense", "herdStarvationPercentReduction": "Herd Starvation Reduction",
        "additionalGovernorSlots": "Governor Slots",
    }
    return labels.get(field, re.sub(r"([a-z])([A-Z])", r"\1 \2", field).replace("Modifier", "").title())


def gameplay_summary(text: str) -> str:
    bits: list[str] = []
    for field in FLOAT_EFFECTS:
        value = number(text, field)
        if abs(value) <= 1e-8:
            continue
        bits.append(f"{value * 100:+.0f}% {effect_label(field)}")
    for field in INT_EFFECTS:
        value = int(number(text, field))
        if value:
            bits.append(f"{value:+d} {effect_label(field)}")
    array_labels = {
        "tileYieldBonuses": "targeted tile effect", "buildingBonuses": "targeted building effect",
        "unitYieldBonuses": "unit yield effect", "unitBonuses": "unit stat effect",
        "equipmentYieldBonuses": "equipment yield effect", "workerYieldBonuses": "worker yield effect",
        "workerBonuses": "worker stat effect", "diseaseBonuses": "disease effect", "attritionBonuses": "attrition effect",
        "cityBonuses": "city effect", "nonStateReligionUnhappinessModifiers": "religious-tolerance effect",
        "herdYieldBonuses": "herd yield effect", "unlockedGovernorTraits": "governor trait unlock",
        "governorOpinionEffects": "governor opinion effect",
    }
    for field in ARRAY_EFFECTS:
        count = len(array_items(text, field))
        if count:
            label = array_labels[field]
            bits.append(f"{count} {label}{'' if count == 1 else 's'}")
    return "; ".join(bits) if bits else "no configured gameplay effect"


def make_description(name: str, tag: int, text: str) -> str:
    lead = DOMAIN_LEADS.get(tag, DOMAIN_LEADS[0])
    return f"{name} {lead}. Gameplay impact: {gameplay_summary(text)}."


def repair_policy(path: Path, research: list[ResearchAsset]) -> tuple[str, list[str]]:
    text = path.read_text(encoding="utf-8")
    original = text
    name = scalar(text, "policyName") or path.stem
    cost = int(number(text, "policyPointCost"))
    tag = primary_tag(text)
    changes: list[str] = []

    # Serialize the intentional-start distinction on every policy so absence can never mean ambiguity again.
    start = name in START_POLICIES
    previous_start = scalar(text, "availableFromStart")
    text = set_scalar(text, "availableFromStart", "1" if start else "0", after="policyPointCost")
    if previous_start is None:
        changes.append("serialized availableFromStart")

    # One known obviously extreme value discovered during the audit. Treat -1 as an accidental -100%, not a design choice.
    if name == "Scutage" and number(text, "meleeAttackBonus") <= -0.5:
        text = set_scalar(text, "meleeAttackBonus", "-0.1")
        changes.append("corrected meleeAttackBonus from -100% to -10%")

    # Give true no-op policies a modest, domain-appropriate package. Existing nonzero designs are never replaced here.
    if not has_effect(text):
        package = NO_OP_OVERRIDES.get(name, FALLBACK_EFFECTS.get(tag, FALLBACK_EFFECTS[0]))
        for field, value in package.items():
            text = set_scalar(text, field, str(value))
        changes.append("added gameplay effects: " + gameplay_summary(text))

    # Preserve every existing gate. Only genuinely ungated, non-start policies are assigned a gate.
    if not start and not has_gate(text):
        chosen, rationale = choose_gate(name, tag, cost, research)
        field = "requiredTechs" if chosen.kind == "Tech" else "requiredCultures"
        text = set_array_reference(text, field, chosen.guid)
        changes.append(f"added {chosen.kind} gate '{chosen.name}' ({rationale})")

    # Replace the meaningless active-duration boilerplate with an impact-led description.
    if is_boilerplate(text):
        text = set_scalar(text, "description", make_description(name, tag, text))
        changes.append("replaced boilerplate description")

    if text != original:
        path.write_text(text, encoding="utf-8")
    return name, changes


def audit(policy_paths: list[Path]) -> dict[str, list[str]]:
    problems = {"boilerplate": [], "no_effect": [], "ungated": [], "extreme": []}
    for path in policy_paths:
        text = path.read_text(encoding="utf-8")
        name = scalar(text, "policyName") or path.stem
        if is_boilerplate(text):
            problems["boilerplate"].append(name)
        if not has_effect(text):
            problems["no_effect"].append(name)
        intended_start = scalar(text, "availableFromStart") == "1"
        if not has_gate(text) and not intended_start:
            problems["ungated"].append(name)
        for field in FLOAT_EFFECTS:
            value = number(text, field)
            if abs(value) > 0.50:
                problems["extreme"].append(f"{name}: {field}={value}")
    return problems


def main() -> int:
    policy_paths = sorted(POLICIES.glob("*.asset"))
    if not policy_paths:
        raise RuntimeError("No policy assets found")
    research = load_research_assets()
    before = audit(policy_paths)

    changed: list[tuple[str, list[str]]] = []
    for path in policy_paths:
        name, changes = repair_policy(path, research)
        if changes:
            changed.append((name, changes))

    after = audit(policy_paths)

    report = [
        "# Policy Content Audit — Generated",
        "",
        f"Policies audited: **{len(policy_paths)}**",
        f"Research assets available for gating: **{len(research)}**",
        "",
        "## Before repair",
        f"- Boilerplate or empty descriptions: **{len(before['boilerplate'])}**",
        f"- True no-op policies: **{len(before['no_effect'])}**",
        f"- Ungated policies not explicitly marked start-available: **{len(before['ungated'])}**",
        f"- Extreme percentage values (>50% magnitude): **{len(before['extreme'])}**",
        "",
        "## After repair",
        f"- Boilerplate or empty descriptions: **{len(after['boilerplate'])}**",
        f"- True no-op policies: **{len(after['no_effect'])}**",
        f"- Ungated policies not explicitly marked start-available: **{len(after['ungated'])}**",
        f"- Extreme percentage values (>50% magnitude): **{len(after['extreme'])}**",
        "",
        f"Policies changed: **{len(changed)}**",
        "",
        "## Changes by policy",
    ]
    for name, changes in changed:
        report.append(f"### {name}")
        report.extend(f"- {change}" for change in changes)
        report.append("")

    if after["extreme"]:
        report.extend(["## Remaining extreme values", *[f"- {x}" for x in after["extreme"]], ""])
    if after["boilerplate"] or after["no_effect"] or after["ungated"]:
        report.extend(["## Remaining blocking problems"])
        for key in ("boilerplate", "no_effect", "ungated"):
            for item in after[key]:
                report.append(f"- {key}: {item}")
        report.append("")

    (ROOT / "PolicyContentAudit.generated.md").write_text("\n".join(report), encoding="utf-8")

    print(f"Audited {len(policy_paths)} policies; changed {len(changed)}")
    print(f"Before: boilerplate={len(before['boilerplate'])}, no_effect={len(before['no_effect'])}, ungated={len(before['ungated'])}, extreme={len(before['extreme'])}")
    print(f"After: boilerplate={len(after['boilerplate'])}, no_effect={len(after['no_effect'])}, ungated={len(after['ungated'])}, extreme={len(after['extreme'])}")

    return 1 if after["boilerplate"] or after["no_effect"] or after["ungated"] else 0


if __name__ == "__main__":
    raise SystemExit(main())
