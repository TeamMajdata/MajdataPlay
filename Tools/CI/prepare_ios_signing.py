"""Complete and validate the temporary iOS signing keychain before Xcode archives."""

import base64
import hashlib
import os
from pathlib import Path
import plistlib
import re
import subprocess
import sys
import tempfile


APPLE_CERTIFICATES = (
    ("AppleWWDRCAG3.cer", "https://www.apple.com/certificateauthority/AppleWWDRCAG3.cer",
     "dcf21878c77f4198e4b4614f03d696d89c66c66008d4244e1b99161aac91601f"),
    ("AppleRootCA.cer", "https://www.apple.com/appleca/AppleIncRootCertificate.cer",
     "b0b1730ecbc7ff4505142c49f1295e6eda6bcaed7e2c68c5be91b5a11001f024"),
)
PEM_CERTIFICATE = re.compile(r"-----BEGIN CERTIFICATE-----(.*?)-----END CERTIFICATE-----", re.DOTALL)
SIGNING_IDENTITY = re.compile(r"^\s*\d+\)\s+([A-Fa-f0-9]{40})\s", re.MULTILINE)


def prepare_signing(keychain, profile, temporary_directory, run=subprocess.run):
    """Complete the pinned Apple chain and exercise the profile's private signing key."""
    if not keychain.is_absolute():
        raise ValueError("The temporary signing keychain must have an absolute path.")
    existing = run(["/usr/bin/security", "find-certificate", "-a", "-p", str(keychain)],
                   check=True, capture_output=True, text=True).stdout
    fingerprints = {
        hashlib.sha256(base64.b64decode(certificate)).hexdigest()
        for certificate in PEM_CERTIFICATE.findall(existing)
    }
    with tempfile.TemporaryDirectory(prefix="majplay-ios-signing-", dir=temporary_directory) as temporary:
        directory = Path(temporary)
        for filename, url, fingerprint in APPLE_CERTIFICATES:
            if fingerprint in fingerprints:
                print(f"The temporary signing keychain already contains {filename}.")
                continue
            download = run(["/usr/bin/curl", "--fail", "--silent", "--show-error", "--location",
                            "--proto", "=https", "--proto-redir", "=https", "--retry", "2",
                            "--connect-timeout", "15", "--max-time", "60", url],
                           check=True, capture_output=True)
            if hashlib.sha256(download.stdout).hexdigest() != fingerprint:
                raise RuntimeError(f"{filename} has an unexpected SHA-256 fingerprint.")
            downloaded = directory / filename
            downloaded.write_bytes(download.stdout)
            run(["/usr/bin/security", "add-certificates", "-k", str(keychain), str(downloaded)],
                check=True, capture_output=True, text=True)
            print(f"Installed {filename} into the temporary signing keychain.")

        decoded = run(["/usr/bin/security", "cms", "-D", "-i", str(profile)],
                      check=True, capture_output=True).stdout
        certificates = plistlib.loads(decoded).get("DeveloperCertificates", [])
        if not certificates or any(not isinstance(certificate, bytes) for certificate in certificates):
            raise RuntimeError("The provisioning profile has no developer certificates.")
        identities = run(["/usr/bin/security", "find-identity", "-v", "-p", "codesigning", str(keychain)],
                         check=True, capture_output=True, text=True).stdout
        valid = {fingerprint.lower() for fingerprint in SIGNING_IDENTITY.findall(identities)}
        certificate = next((certificate for certificate in certificates
                            if hashlib.sha1(certificate).hexdigest() in valid), None)
        if certificate is None:
            raise RuntimeError("The temporary keychain has no valid signing identity for the provisioning profile.")
        leaf = directory / "distribution.cer"
        leaf.write_bytes(certificate)
        run(["/usr/bin/security", "verify-cert", "-c", str(leaf), "-p", "codeSign", "-L",
             "-k", str(keychain)], check=True, capture_output=True, text=True)
        probe = directory / "signing-probe"
        run(["/usr/bin/xcrun", "clang", "-x", "c", "-", "-o", str(probe)],
            input="int main(void) { return 0; }\n", check=True, capture_output=True, text=True)
        run(["/usr/bin/codesign", "--force", "--sign", hashlib.sha1(certificate).hexdigest(),
             "--timestamp=none", str(probe)], check=True, capture_output=True, text=True)
        run(["/usr/bin/codesign", "--verify", "--strict", str(probe)],
            check=True, capture_output=True, text=True)
    print("Verified the distribution chain, profile identity and codesign private-key access.")


if __name__ == "__main__":
    try:
        prepare_signing(Path(os.environ["CI_KEYCHAIN"] + ".keychain"),
                        Path.home() / "Library/MobileDevice/Provisioning Profiles/appstore.mobileprovision",
                        Path(os.environ["RUNNER_TEMP"]))
    except (KeyError, OSError, RuntimeError, ValueError, subprocess.CalledProcessError) as error:
        print(f"::error::iOS signing preparation failed: {error}", file=sys.stderr)
        sys.exit(1)
