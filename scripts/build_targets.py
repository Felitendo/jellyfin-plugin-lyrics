#!/usr/bin/env python3
"""Build one plugin artifact per supported Jellyfin line from a single source tree.

jprm reads exactly one build.yaml and bakes its ``targetAbi`` and ``framework`` into the
zip's meta.json, so the only way to get an artifact per Jellyfin line is to run it twice
with a rewritten build.yaml. The MSBuild side of the switch travels in the environment
because jprm assembles a fixed ``dotnet publish`` command line with no room for ``-p:``.

Both artifacts carry the same plugin version and differ only in targetAbi, which is what
lets one manifest serve both server generations - see scripts/update_manifest.py.

jprm logs "Found multiple instances of the TargetFramework tag, bailing" once per run. That
is expected and harmless: it is jprm declining to rewrite our conditional TargetFramework
properties, which is exactly what we want it to do.
"""

import argparse
import os
import re
import shutil
import subprocess
import sys
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parent.parent
BUILD_YAML = REPO_ROOT / "build.yaml"
BUILD_PROPS = REPO_ROOT / "Directory.Build.props"
PROJECT_DIR = REPO_ROOT / "Jellyfin.Plugin.Lyrics"

# Jellyfin 12 first: update_manifest.py relies on that order, and building it first means a
# breakage on the forward-looking line surfaces before the legacy build burns time.
TARGETS = (
    {"key": "jf12", "target_abi": "12.0.0.0", "framework": "net10.0", "suffix": "jf12"},
    {"key": "jf10", "target_abi": "10.11.0.0", "framework": "net9.0", "suffix": "jf10.11"},
)


def rewrite_build_yaml(target):
    text = BUILD_YAML.read_text(encoding="utf-8")
    text, abi_count = re.subn(
        r'^targetAbi:.*$', 'targetAbi: "{}"'.format(target["target_abi"]), text, flags=re.MULTILINE)
    text, fw_count = re.subn(
        r'^framework:.*$', 'framework: "{}"'.format(target["framework"]), text, flags=re.MULTILINE)
    if abi_count != 1 or fw_count != 1:
        sys.exit("build.yaml must contain exactly one targetAbi and one framework line "
                 "(found {} and {})".format(abi_count, fw_count))
    BUILD_YAML.write_text(text, encoding="utf-8")


def read_version():
    match = re.search(r'^version:\s*"?([^"\s]+)"?\s*$', BUILD_YAML.read_text(encoding="utf-8"), re.MULTILINE)
    if not match:
        sys.exit("No version found in build.yaml")
    # Pad to four components the way jprm names its own artifacts.
    parts = match.group(1).split(".")
    parts += ["0"] * (4 - len(parts))
    return ".".join(parts[:4])


def build(target, output_dir):
    # jprm runs `dotnet clean --framework=X` before restoring, which fails outright when obj/
    # still holds the previous target's assets file. Start each target from a clean slate.
    for stale in (PROJECT_DIR / "bin", PROJECT_DIR / "obj"):
        shutil.rmtree(stale, ignore_errors=True)

    env = dict(os.environ, JellyfinTarget=target["key"])
    result = subprocess.run(
        ["jprm", "--verbosity=info", "plugin", "build", str(REPO_ROOT), "--output", str(output_dir)],
        cwd=REPO_ROOT, env=env, text=True, capture_output=True)
    sys.stderr.write(result.stderr)
    if result.returncode != 0:
        sys.stderr.write(result.stdout)
        sys.exit("jprm failed for target {}".format(target["key"]))

    # jprm echoes the artifact path on stdout as its last line.
    lines = [line.strip() for line in result.stdout.splitlines() if line.strip()]
    if not lines:
        sys.exit("jprm produced no artifact path for target {}".format(target["key"]))
    return Path(lines[-1])


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", default=str(REPO_ROOT / "artifacts"),
                        help="Directory the renamed artifacts are written to")
    args = parser.parse_args()

    output_dir = Path(args.output).resolve()
    output_dir.mkdir(parents=True, exist_ok=True)

    version = read_version()
    # jprm rewrites both files in place; put them back so a local run leaves no diff behind.
    originals = {path: path.read_text(encoding="utf-8") for path in (BUILD_YAML, BUILD_PROPS)}

    produced = []
    try:
        for target in TARGETS:
            rewrite_build_yaml(target)
            artifact = build(target, output_dir)
            # Both runs emit the same <slug>_<version>.zip, so name the kept copy by ABI.
            final = output_dir / "lyrics_{}_{}.zip".format(version, target["suffix"])
            if final.exists():
                final.unlink()
            shutil.move(str(artifact), str(final))
            # jprm drops <zip>.md5sum and <zip>.meta.json beside the artifact under the same
            # base name; carry them along or the second target would overwrite the first's.
            for sidecar in (".md5sum", ".meta.json"):
                source = artifact.with_name(artifact.name + sidecar)
                if source.is_file():
                    shutil.move(str(source), str(final.with_name(final.name + sidecar)))
            produced.append((target["key"], final))
            print("{}: {}".format(target["key"], final))
    finally:
        for path, text in originals.items():
            path.write_text(text, encoding="utf-8")

    github_output = os.environ.get("GITHUB_OUTPUT")
    if github_output:
        with open(github_output, "a", encoding="utf-8") as handle:
            handle.write("version={}\n".format(version))
            for key, path in produced:
                handle.write("{}_artifact={}\n".format(key, path))


if __name__ == "__main__":
    main()
