#!/usr/bin/env python3
"""Add one release to manifest.json as two entries - one per supported Jellyfin line.

`jprm repo add` cannot do this: update_plugin_manifest() drops any existing entry whose
version matches the one being added, so a second call for the same version would delete the
first artifact's entry instead of sitting next to it.

Two entries with the same version work because Jellyfin filters by ABI before it sorts:

    .Where(x => string.IsNullOrEmpty(x.TargetAbi) || Version.Parse(x.TargetAbi) <= appVer)
    ...
    foreach (var v in availableVersions.OrderByDescending(x => x.VersionNumber))

A 10.11 server never sees the 12.0.0.0 entry. A 12 server sees both, and since LINQ's
OrderByDescending is a stable sort and the caller takes the first result, the entry listed
first in the manifest wins the tie - hence Jellyfin 12 is written before Jellyfin 10.11.
"""

import argparse
import hashlib
import json
import sys
from datetime import datetime, timezone
from pathlib import Path

import yaml

REPO_ROOT = Path(__file__).resolve().parent.parent
BUILD_YAML = REPO_ROOT / "build.yaml"
MANIFEST = REPO_ROOT / "manifest.json"

# Order matters: the first matching entry wins on a server that can install both.
TARGET_ABIS = (("jf12", "12.0.0.0"), ("jf10", "10.11.0.0"))


def full_version(version):
    """Pad to four components; Jellyfin is not a fan of short version numbers."""
    parts = str(version).split(".")
    parts += ["0"] * (4 - len(parts))
    return ".".join(parts[:4])


def md5(path):
    digest = hashlib.md5()  # noqa: S324 - manifest checksums are defined as md5 by Jellyfin
    with open(path, "rb") as handle:
        for chunk in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for key, _ in TARGET_ABIS:
        parser.add_argument("--{}-zip".format(key), required=True, help="Built artifact for {}".format(key))
        parser.add_argument("--{}-url".format(key), required=True, help="Public download URL for {}".format(key))
    args = parser.parse_args()

    build_cfg = yaml.safe_load(BUILD_YAML.read_text(encoding="utf-8"))
    version = full_version(build_cfg["version"])
    timestamp = datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")

    manifest = json.loads(MANIFEST.read_text(encoding="utf-8"))
    plugin = next((item for item in manifest if item.get("guid") == str(build_cfg["guid"]).lower()), None)
    if plugin is None:
        sys.exit("No plugin with guid {} in manifest.json".format(build_cfg["guid"]))

    for field in ("name", "description", "overview", "owner", "category"):
        plugin[field] = build_cfg[field]

    kept = [entry for entry in plugin["versions"] if full_version(entry["version"]) != version]
    dropped = len(plugin["versions"]) - len(kept)
    if dropped:
        print("Replacing {} existing entr{} for {}".format(dropped, "y" if dropped == 1 else "ies", version))

    new_entries = []
    for key, target_abi in TARGET_ABIS:
        zip_path = Path(getattr(args, "{}_zip".format(key)))
        if not zip_path.is_file():
            sys.exit("Artifact not found: {}".format(zip_path))
        new_entries.append({
            "version": version,
            "changelog": build_cfg["changelog"],
            "targetAbi": target_abi,
            "sourceUrl": getattr(args, "{}_url".format(key)),
            "checksum": md5(zip_path),
            "timestamp": timestamp,
        })
        print("{}: targetAbi {} -> {}".format(key, target_abi, zip_path.name))

    plugin["versions"] = new_entries + kept
    MANIFEST.write_text(json.dumps(manifest, indent=4, ensure_ascii=False) + "\n", encoding="utf-8")
    print("manifest.json now has {} entries".format(len(plugin["versions"])))


if __name__ == "__main__":
    main()
