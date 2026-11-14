#!/usr/bin/env python3
"""Verify that the Go Incus API surface has been translated to the C# client."""
from __future__ import annotations

import json
import re
import sys
from pathlib import Path


def parse_csharp(root: Path):
    classes: dict[str, set[str]] = {}
    class_kinds: dict[str, str] = {}
    members: dict[str, set[str]] = {}
    property_types: dict[str, dict[str, str]] = {}
    publics: set[str] = set()

    class_re = re.compile(r"public\s+(?:sealed\s+|static\s+|partial\s+|abstract\s+)*class\s+(\w+)")
    enum_re = re.compile(r"public\s+enum\s+(\w+)")
    prop_re = re.compile(
        r'\[JsonPropertyName\("([^"]+)"\)\]\s*(?:\[[^\]]*\]\s*)*public\s+'
        r"((?:static\s+)?(?:required\s+)?(?:readonly\s+)?[^;{=]+?)\s+(\w+)\s*(?:\{|=>)",
        re.MULTILINE,
    )
    method_re = re.compile(
        r"public\s+(?:static\s+)?(?:async\s+)?(?:override\s+)?(?:virtual\s+)?"
        r"(?:[\w<>,\[\]\?\.\(\)\s]+?)\s+(\w+)\s*(?:<[^>]*>)?\s*\(",
        re.MULTILINE,
    )

    for file in sorted(root.rglob("*.cs")):
        if "/obj/" in file.as_posix() or "/bin/" in file.as_posix():
            continue
        text = file.read_text(encoding="utf-8", errors="replace")
        text = re.sub(r"//.*", "", text)
        text = re.sub(r"/\*.*?\*/", "", text, flags=re.DOTALL)

        hits = []
        for match in class_re.finditer(text):
            name = match.group(1)
            hits.append((match.start(), name))
            classes.setdefault(name, set())
            class_kinds[name] = "class"
            members.setdefault(name, set())
        for match in enum_re.finditer(text):
            name = match.group(1)
            hits.append((match.start(), name))
            classes.setdefault(name, set())
            class_kinds[name] = "enum"
            members.setdefault(name, set())
        hits.sort()

        for index, (start, name) in enumerate(hits):
            end = hits[index + 1][0] if index + 1 < len(hits) else len(text)
            body = text[start:end]
            for prop in prop_re.finditer(body):
                classes[name].add(prop.group(1))
                property_types.setdefault(name, {})[prop.group(1)] = prop.group(2).strip()

        for match in method_re.finditer(text):
            publics.add(match.group(1).lower())

    return classes, class_kinds, members, publics, property_types


def go_category(go_type: str) -> str:
    value = go_type.strip()
    while value.startswith("*"):
        value = value[1:]

    if value.startswith("[]") or re.match(r"^\[\d+\]", value):
        return "array"
    if value.startswith("map["):
        return "map"
    if value == "time.Time":
        return "time"
    if value == "json.RawMessage":
        return "raw"
    if value in {"interface{}", "any"}:
        return "any"
    if value == "string":
        return "string"
    if value == "bool":
        return "bool"
    if value in {
        "int", "int8", "int16", "int32", "int64",
        "uint", "uint8", "uint16", "uint32", "uint64",
        "float32", "float64",
    }:
        return "number"

    return "object"


def csharp_category(csharp_type: str) -> str:
    value = csharp_type.replace(" ", "").replace("?", "")
    if value.startswith("global::"):
        value = value[7:]
    if value.startswith(("List<", "IList<", "IEnumerable<")) or value.endswith("[]"):
        return "array"
    if value.startswith(("Dictionary<", "IDictionary<", "IReadOnlyDictionary<")):
        return "map"
    if value in {"DateTimeOffset", "DateTime"}:
        return "time"
    if value == "JsonElement":
        return "raw"
    if value in {"object", "dynamic"}:
        return "any"
    if value == "string":
        return "string"
    if value == "bool":
        return "bool"
    if value in {
        "int", "long", "uint", "ulong", "short", "ushort",
        "byte", "sbyte", "double", "float", "decimal",
    }:
        return "number"

    return "object"


def normalize_go_field(field):
    name = field.get("jsonName") or ""
    if not name:
        name = field.get("goName", "")
    return name, field.get("goName", "")


def main():
    manifest_path = Path(sys.argv[1] if len(sys.argv) > 1 else "/tmp/opencode/verify/manifest.json")
    src_root = Path(sys.argv[2] if len(sys.argv) > 2 else "src/Mossie.Incus.Client")
    manifest = json.loads(manifest_path.read_text())

    classes, class_kinds, _, publics, property_types = parse_csharp(src_root)

    missing_types = []
    missing_fields = []
    extra_fields = []
    type_mismatches = []
    checked_types = 0
    checked_fields = 0

    for t in manifest["types"]:
        name = t["name"]
        kind = t["kind"]
        if kind == "interface":
            continue

        if name not in classes:
            missing_types.append({"type": name, "kind": kind, "file": t["file"]})
            continue

        if kind != "struct":
            continue

        checked_types += 1
        csharp_names = classes[name]
        go_names = set()
        for field in t.get("fields", []):
            if field.get("ignored"):
                continue
            json_name, go_name = normalize_go_field(field)
            if not json_name:
                continue
            go_names.add(json_name)
            checked_fields += 1
            if json_name not in csharp_names:
                missing_fields.append({
                    "type": name,
                    "field": json_name,
                    "go_field": go_name,
                    "source": field.get("source", name),
                    "go_type": field.get("type", ""),
                })
            else:
                csharp_type = property_types.get(name, {}).get(json_name)
                if csharp_type is not None:
                    go_cat = go_category(field.get("type", ""))
                    cs_cat = csharp_category(csharp_type)
                    if go_cat not in {"object", "any"} and cs_cat not in {"object", "any"} and go_cat != cs_cat:
                        type_mismatches.append({
                            "type": name,
                            "field": json_name,
                            "go_type": field.get("type", ""),
                            "csharp_type": csharp_type,
                            "go_category": go_cat,
                            "csharp_category": cs_cat,
                        })

        for extra in sorted(csharp_names - go_names):
            extra_fields.append({"type": name, "field": extra})

    method_report = {}
    for receiver, methods in sorted(manifest["methods"].items()):
        if receiver not in {"ProtocolIncus", "ProtocolSimpleStreams", "ProtocolOCI"}:
            continue
        missing = []
        present = []
        for method in methods:
            name = method["name"]
            candidates = [name]
            if name.endswith("WithContext"):
                candidates.append(name[: -len("WithContext")])

            found = any((candidate + "Async").lower() in publics or candidate.lower() in publics for candidate in candidates)
            if found:
                present.append(name)
            else:
                missing.append({"name": name, "file": method["file"]})

        method_report[receiver] = {
            "total": len(methods),
            "missing": missing,
            "present": len(present),
        }

    report = {
        "apiTypes": {
            "total": len([t for t in manifest["types"] if t["kind"] != "interface"]),
            "checkedStructs": checked_types,
            "missingTypes": missing_types,
            "checkedFields": checked_fields,
            "missingFields": missing_fields,
            "extraFields": extra_fields,
            "typeMismatches": type_mismatches,
        },
        "protocolMethods": method_report,
    }

    print(json.dumps(report, indent=2))


if __name__ == "__main__":
    main()
