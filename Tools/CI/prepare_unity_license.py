"""Activate Unity once for the macOS runner account, without returning its license."""

import base64
import binascii
import os
from pathlib import Path
import re
import subprocess
import sys
import tempfile
import xml.etree.ElementTree as ET


def serial_from_environment(environment):
    """Read a serial directly or decode GameCI's legacy ULF DeveloperData."""
    serial = environment.get("UNITY_SERIAL", "")
    if not serial:
        try:
            license_xml = ET.fromstring(environment.get("UNITY_LICENSE", ""))
            developer_data = license_xml.find(".//DeveloperData")
            if developer_data is None:
                raise ValueError()
            serial = base64.b64decode(
                developer_data.attrib["Value"], validate=True
            )[4:].decode("ascii")
        except (ET.ParseError, KeyError, ValueError, binascii.Error):
            raise RuntimeError("Set UNITY_SERIAL or a valid UNITY_LICENSE secret.") from None
    if not re.fullmatch(r"[A-Za-z0-9]{2}(?:-[A-Za-z0-9]{4}){5}", serial):
        raise RuntimeError("The Unity license serial has an invalid format.")
    return serial


def has_license(license_file):
    """Check for a readable ULF; Unity checks its entitlement during each build."""
    try:
        return ET.parse(license_file).find(".//License") is not None
    except (OSError, ET.ParseError):
        return False


def prepare_license(editor, license_directory, state_directory, environment, run=None):
    """Reuse an installed ULF or activate it, keeping sensitive logs private."""
    if run is None:
        run = subprocess.run
    license_file = license_directory / "Unity_lic.ulf"
    if has_license(license_file):
        print("Reusing the installed Unity license (Unity validates it at build time).")
        return
    if not license_directory.is_dir() or not os.access(license_directory, os.W_OK):
        raise RuntimeError(
            "An administrator must create the Unity license directory first:\n"
            "sudo install -d -o majplay-ci -g staff -m 755 "
            "'/Library/Application Support/Unity'"
        )
    serial = serial_from_environment(environment)
    email = environment.get("UNITY_EMAIL", "")
    password = environment.get("UNITY_PASSWORD", "")
    if not email or not password:
        raise RuntimeError("Set UNITY_EMAIL and UNITY_PASSWORD for first-time activation.")
    log_file = state_directory / "license-activation.log"
    # Unity can include account details in its log. Never print or upload this file.
    with log_file.open("w"):
        log_file.chmod(0o600)
    with tempfile.TemporaryDirectory(prefix="activation-", dir=state_directory) as project:
        result = run(
            [str(editor), "-batchmode", "-nographics", "-quit",
             "-serial", serial, "-username", email, "-password", password,
             "-createProject", project, "-logFile", str(log_file)],
            stdout=subprocess.DEVNULL,
            stderr=subprocess.DEVNULL,
            timeout=300,
            check=False,
        )
    if result.returncode != 0 or not has_license(license_file):
        raise RuntimeError(
            f"Unity activation failed (exit {result.returncode}). "
            f"Inspect the private runner log at {log_file}; do not upload it."
        )
    license_file.chmod(0o600)
    print("Unity license activated and retained for subsequent runner jobs.")


def main():
    """Serialize first activation across both runner services on this Mac."""
    import fcntl

    version = os.environ["UNITY_VERSION"]
    editor = Path(f"/Applications/Unity/Hub/Editor/{version}/Unity.app/Contents/MacOS/Unity")
    if not editor.is_file():
        raise RuntimeError(f"Required Unity Editor is missing: {editor}")
    state_directory = Path.home() / "Library/Application Support/MajdataPlayCI"
    state_directory.mkdir(parents=True, exist_ok=True, mode=0o700)
    with (state_directory / "license.lock").open("a") as lock:
        fcntl.flock(lock, fcntl.LOCK_EX)
        prepare_license(editor, Path("/Library/Application Support/Unity"),
                        state_directory, os.environ)


if __name__ == "__main__":
    try:
        main()
    except (OSError, RuntimeError, subprocess.TimeoutExpired) as error:
        # TimeoutExpired contains the command, including credentials: never format it.
        message = "Unity activation timed out; inspect the private runner log." if isinstance(
            error, subprocess.TimeoutExpired
        ) else str(error)
        print(f"::error::{message}", file=sys.stderr)
        sys.exit(1)
