"""Keep Unity's iOS IL2CPP cache outside Xcode's disposable archive intermediates."""

import json
import os
from pathlib import Path
import re
import sys


ARCHIVE_CACHE = "$CONFIGURATION_TEMP_DIR/artifacts"
PERSISTENT_CACHE = "$PROJECT_DIR/../Il2CppBuildCache/$CONFIGURATION/artifacts"
SHELL_SCRIPT = re.compile(r'shellScript = ("(?:[^"\\]|\\.)*");')


def prepare_cache(project_file, configuration):
    """Patch only the generated IL2CPP phase and migrate its previous cache once."""
    content = project_file.read_text(encoding="utf-8")
    matched = 0

    def patch_script(match):
        nonlocal matched
        script = json.loads(match.group(1))
        if "--compile-cpp" not in script or "--cachedirectory=" not in script:
            return match.group(0)
        if ARCHIVE_CACHE not in script and PERSISTENT_CACHE not in script:
            raise RuntimeError("Unity's generated IL2CPP cache layout changed; review the build phase.")
        matched += 1
        return "shellScript = " + json.dumps(
            script.replace(ARCHIVE_CACHE, PERSISTENT_CACHE), ensure_ascii=False
        ) + ";"

    updated = SHELL_SCRIPT.sub(patch_script, content)
    if matched != 1:
        raise RuntimeError("Expected exactly one Unity IL2CPP build phase in the generated Xcode project.")
    # Both paths are inside this dedicated CI build output, never the user's global cache.
    build_root = project_file.resolve().parent.parent.parent
    source = build_root / (
        "DerivedData/Build/Intermediates.noindex/ArchiveIntermediates/Unity-iPhone/"
        "IntermediateBuildFilesPath/Unity-iPhone.build"
    ) / f"{configuration}-iphoneos/artifacts"
    destination = build_root / "Il2CppBuildCache" / configuration / "artifacts"
    if source.is_dir() and not destination.exists():
        destination.parent.mkdir(parents=True, exist_ok=True)
        source.rename(destination)
        print("Migrated existing IL2CPP objects into the persistent iOS cache.")
    if updated != content:
        project_file.write_text(updated, encoding="utf-8")
    print(f"iOS IL2CPP cache: {destination}")


if __name__ == "__main__":
    try:
        prepare_cache(Path(sys.argv[1]), os.environ.get("CONFIGURATION", "Release"))
    except (OSError, RuntimeError, ValueError) as error:
        print(f"::error::{error}", file=sys.stderr)
        sys.exit(1)
