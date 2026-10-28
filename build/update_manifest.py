#!/usr/bin/env python3
# SPDX-License-Identifier: GPL-3.0-only
# Copyright (c) 2026 Fr4ctalNoise and Project A contributors.
"""
Adds a release to manifest.json, the catalog file Jellyfin reads from
https://raw.githubusercontent.com/Fr4ctalNoise/Project-A/main/manifest.json

  python3 build/update_manifest.py <manifest-entry.json>

Newest version first. Re-running for the same version replaces its entry. Standard library only.
"""

import json
import pathlib
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent
sys.path.insert(0, str(ROOT / "build"))
from package import read_build_yaml  # noqa: E402


def version_key(v: str) -> tuple:
    return tuple(int(p) for p in v.split("."))


def main() -> None:
    if len(sys.argv) != 2:
        sys.exit(__doc__)
    entry = json.loads(pathlib.Path(sys.argv[1]).read_text(encoding="utf-8"))
    meta = read_build_yaml()
    path = ROOT / "manifest.json"
    manifest = json.loads(path.read_text(encoding="utf-8")) if path.exists() else []

    plugin = next((p for p in manifest if p.get("guid") == meta["guid"]), None)
    if plugin is None:
        plugin = {"guid": meta["guid"], "versions": []}
        manifest.append(plugin)
    plugin.update({
        "name": meta["name"],
        "description": meta["description"],
        "overview": meta["overview"],
        "owner": meta["owner"],
        "category": meta["category"],
    })
    versions = [v for v in plugin.get("versions", []) if v.get("version") != entry["version"]]
    versions.append(entry)
    plugin["versions"] = sorted(versions, key=lambda v: version_key(v["version"]), reverse=True)

    path.write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    print(f"manifest.json: {meta['name']} now lists {[v['version'] for v in plugin['versions']]}")


if __name__ == "__main__":
    main()
