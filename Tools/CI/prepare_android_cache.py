"""Invalidate exported IL2CPP caches whose Bee graphs target another workspace."""

import json
from pathlib import Path
import shutil
import sys


def prepare_cache(export_directory):
    """Drop only relocated native caches, retaining caches for the current export."""
    library = Path(export_directory).resolve() / "unityLibrary"
    build_root = library / "build"
    removed = 0
    for graph in sorted(build_root.glob("il2cpp_*/il2cpp_cache/buildstate/bee.dag.json")):
        cache = graph.parent.parent
        if not cache.resolve().is_relative_to(build_root) or cache.is_symlink():
            raise RuntimeError(f"Refusing to remove an IL2CPP cache outside {build_root}.")
        content = json.loads(graph.read_text(encoding="utf-8"))
        roots = set()
        for node in content["Nodes"]:
            for output in node.get("Outputs", []):
                if "/src/main/jniLibs/" in output and output.endswith("/libil2cpp.so"):
                    output_root = Path(output.split("/src/main/jniLibs/", 1)[0])
                    if not output_root.is_absolute():
                        raise RuntimeError(f"Expected absolute IL2CPP output paths in {graph}.")
                    roots.add(output_root.resolve())
        if not roots:
            raise RuntimeError(f"No IL2CPP library output found in {graph}; review the Bee graph layout.")
        if roots != {library}:
            shutil.rmtree(cache)
            removed += 1
            print(f"Removed relocated IL2CPP cache: {cache}")
    print(f"Android IL2CPP cache preparation: {removed} relocated caches removed.")
    return removed


if __name__ == "__main__":
    try:
        if len(sys.argv) != 2:
            raise ValueError("Usage: prepare_android_cache.py EXPORT_DIRECTORY")
        prepare_cache(Path(sys.argv[1]))
    except (OSError, RuntimeError, ValueError, KeyError) as error:
        print(f"::error::{error}", file=sys.stderr)
        sys.exit(1)
