#!/usr/bin/env python3
# SPDX-License-Identifier: GPL-3.0-only
# Copyright (c) 2026 Fr4ctalNoise and Project A contributors.
"""
Packages a built plugin for release. Standard library only.

  python3 build/package.py <dll-folder> <out-folder>

Writes to <out-folder>:
  project-a_<version>.zip          what Jellyfin installs: the DLL, meta.json and licence files
  project-a_<version>_source.zip   every tracked file, under a top-level src/
  manifest-entry.json              the version entry for manifest.json (checksum, URL, changelog)

Fails (exit 1) if the install zip would contain anything but the expected files: in
particular no second copy of SQLite or any Jellyfin assembly (see the project file).
"""

import datetime
import hashlib
import json
import pathlib
import re
import subprocess
import sys
import zipfile

ROOT = pathlib.Path(__file__).resolve().parent.parent
REPO_URL = "https://github.com/Fr4ctalNoise/Project-A"


def read_version() -> str:
    props = (ROOT / "Directory.Build.props").read_text(encoding="utf-8")
    m = re.search(r"<Version>(\d+\.\d+\.\d+\.\d+)</Version>", props)
    if not m:
        sys.exit("Version not found in Directory.Build.props")
    return m.group(1)


def read_build_yaml() -> dict:
    """Parses build.yaml's simple shape: key: "value", key: > (folded block), key: (list of "- item")."""
    data, key, folded, items = {}, None, None, None
    for raw in (ROOT / "build.yaml").read_text(encoding="utf-8").splitlines():
        if raw.lstrip().startswith("#") or not raw.strip():
            continue
        if raw.startswith("  ") and key:
            line = raw.strip()
            if items is not None and line.startswith("- "):
                items.append(line[2:].strip().strip('"'))
            elif folded is not None:
                folded.append(line)
            continue
        key, _, value = raw.partition(":")
        key, value = key.strip(), value.strip()
        folded = items = None
        if value == ">":
            folded = []
            data[key] = folded
        elif value == "":
            items = []
            data[key] = items
        else:
            data[key] = value.strip('"')
    return {k: (" ".join(v) if isinstance(v, list) and k != "artifacts" else v) for k, v in data.items()}


def changelog_for(version: str) -> str:
    text = (ROOT / "CHANGELOG.md").read_text(encoding="utf-8")
    m = re.search(rf"^## {re.escape(version)} .*?$(.*?)(?=^## |\Z)", text, re.M | re.S)
    if not m:
        sys.exit(f"CHANGELOG.md has no '## {version} ...' section")
    return m.group(1).strip()


def main() -> None:
    if len(sys.argv) != 3:
        sys.exit(__doc__)
    dll_dir, out = pathlib.Path(sys.argv[1]), pathlib.Path(sys.argv[2])
    out.mkdir(parents=True, exist_ok=True)
    version = read_version()
    meta = read_build_yaml()
    changelog = changelog_for(version)
    timestamp = datetime.datetime.now(datetime.timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")

    # meta.json: what Jellyfin reads from the installed plugin folder.
    # autoUpdate false: Project A never updates itself; you choose when.
    plugin_meta = {
        "category": meta["category"],
        "changelog": changelog,
        "description": meta["description"],
        "guid": meta["guid"],
        "name": meta["name"],
        "overview": meta["overview"],
        "owner": meta["owner"],
        "targetAbi": meta["targetAbi"],
        "timestamp": timestamp,
        "version": version,
        "status": "Active",
        "autoUpdate": False,
        "assemblies": [],
    }

    files = {name: dll_dir / name for name in meta["artifacts"]}
    files["meta.json"] = None
    files["LICENSE"] = ROOT / "LICENSE"
    files["THIRD_PARTY_NOTICES.md"] = ROOT / "THIRD_PARTY_NOTICES.md"
    for lic in sorted((ROOT / "licenses").glob("*.txt")):
        files[f"licenses/{lic.name}"] = lic

    zip_path = out / f"project-a_{version}.zip"
    with zipfile.ZipFile(zip_path, "w", zipfile.ZIP_DEFLATED) as z:
        for name, src in files.items():
            if src is None:
                z.writestr(name, json.dumps(plugin_meta, indent=2) + "\n")
            else:
                if not src.is_file():
                    sys.exit(f"Missing file for the zip: {src}")
                z.write(src, name)

    # Check the zip: exactly the expected entries; one DLL, ours; nothing SQLite or Jellyfin.
    with zipfile.ZipFile(zip_path) as z:
        names = sorted(z.namelist())
    expected = sorted(files)
    if names != expected:
        sys.exit(f"Unexpected zip contents:\n  got      {names}\n  expected {expected}")
    dlls = [n for n in names if n.lower().endswith(".dll")]
    if dlls != ["Jellyfin.Plugin.ProjectA.dll"]:
        sys.exit(f"The zip must contain only Project A's DLL, found {dlls}")
    if any(re.search(r"sqlite|jellyfin\.(controller|model|data|common)|mediabrowser", n, re.I) for n in names):
        sys.exit(f"The zip contains a server-provided library: {names}")
    for sibling in sorted(dll_dir.glob("*.dll")):
        if sibling.name not in dlls:
            print(f"note: build output also has {sibling.name} (not packaged)")

    checksum = hashlib.md5(zip_path.read_bytes()).hexdigest()  # Jellyfin's catalog uses MD5
    entry = {
        "version": version,
        "changelog": changelog,
        "targetAbi": meta["targetAbi"],
        "sourceUrl": f"{REPO_URL}/releases/download/v{version}/{zip_path.name}",
        "checksum": checksum,
        "timestamp": timestamp,
    }
    (out / "manifest-entry.json").write_text(json.dumps(entry, indent=2) + "\n", encoding="utf-8")

    # Source zip: every tracked file under a top-level src/.
    tracked = subprocess.run(["git", "ls-files"], cwd=ROOT, check=True, capture_output=True, text=True).stdout.split("\n")
    src_zip = out / f"project-a_{version}_source.zip"
    with zipfile.ZipFile(src_zip, "w", zipfile.ZIP_DEFLATED) as z:
        for rel in filter(None, tracked):
            z.write(ROOT / rel, f"src/{rel}")

    print(f"{zip_path.name}  md5 {checksum}")
    for n in names:
        print(f"  {n}")
    print(f"{src_zip.name}  ({len([t for t in tracked if t])} files under src/)")


if __name__ == "__main__":
    main()
