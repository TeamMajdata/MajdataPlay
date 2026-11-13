"""Enable Unity iOS append mode only when an Xcode export already exists."""

import os
from pathlib import Path
import sys


APPEND_PARAMETER = "-AcceptExternalModificationsToPlayer"


def custom_parameters(export_directory):
    """Return the append flag for an existing, nonempty Xcode project file."""
    project_file = export_directory / "Unity-iPhone.xcodeproj/project.pbxproj"
    if project_file.is_file() and project_file.stat().st_size > 0:
        return APPEND_PARAMETER
    return ""


def prepare_export(export_directory, output_file=None):
    """Select the export mode and publish it as a GitHub Actions step output."""
    parameters = custom_parameters(export_directory)
    output = "custom_parameters=" + parameters + "\n"
    if output_file is None:
        print(output, end="")
    else:
        with output_file.open("a", encoding="utf-8") as destination:
            destination.write(output)
    return parameters


if __name__ == "__main__":
    try:
        if len(sys.argv) != 2:
            raise ValueError("Usage: prepare_ios_export.py EXPORT_DIRECTORY")
        output_path = os.environ.get("GITHUB_OUTPUT")
        prepare_export(Path(sys.argv[1]), Path(output_path) if output_path else None)
    except (OSError, ValueError) as error:
        print(f"::error::{error}", file=sys.stderr)
        sys.exit(1)
