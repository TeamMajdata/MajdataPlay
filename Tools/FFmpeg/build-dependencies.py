#!/usr/bin/env python3
"""Fetch pinned native dependencies and build tools without system installation."""
from __future__ import annotations
import hashlib
import json
import os
from pathlib import Path
import platform
import shutil
import subprocess
import sys
import tarfile
import urllib.request
import zipfile

HERE = Path(__file__).resolve().parent
LOCK = json.loads((HERE / 'dependencies.lock.json').read_text())
BUILD_ROOT = Path(os.environ.get('FFMPEG_BUILD_ROOT', str(HERE / '.build'))).resolve()
SHARED = BUILD_ROOT / 'toolchains'


def download_verified(spec):
    downloads = BUILD_ROOT / 'downloads'
    downloads.mkdir(parents=True, exist_ok=True)
    archive = downloads / spec['url'].rsplit('/', 1)[-1]
    if not archive.is_file():
        temporary = archive.with_suffix(archive.suffix + '.download')
        print('DOWNLOAD ' + spec['url'], flush=True)
        with urllib.request.urlopen(spec['url'], timeout=60) as response, temporary.open('wb') as output:
            shutil.copyfileobj(response, output)
        if hashlib.sha256(temporary.read_bytes()).hexdigest() != spec['sha256']:
            raise RuntimeError('Downloaded dependency SHA256 mismatch: ' + str(temporary))
        temporary.replace(archive)
    if hashlib.sha256(archive.read_bytes()).hexdigest() != spec['sha256']:
        raise RuntimeError('Dependency archive SHA256 mismatch: ' + str(archive))
    return archive


def dav1d_source():
    return SHARED / ('dav1d-' + LOCK['dav1d']['version'])


def encoder_source(name):
    """Locate a dependency by its immutable official Git object identity."""
    return SHARED / (name + '-' + LOCK['softwareEncoders'][name]['commit'][:12])


def verify_encoder_source(name):
    """Reject changed commits, tracked edits, or additional source files."""
    spec = LOCK['softwareEncoders'][name]
    root = encoder_source(name)
    if not (root / '.git').is_dir():
        raise RuntimeError('Pinned ' + name + ' source is missing; run a non-probe build')
    git = ['git', '--no-optional-locks', '-c', 'core.autocrlf=false', '-c', 'core.filemode=false', '-C', str(root)]
    commit = subprocess.check_output(git + ['rev-parse', 'HEAD'], text=True).strip()
    if commit != spec['commit']:
        raise RuntimeError('Pinned ' + name + ' commit mismatch: ' + commit)
    if name == 'x265':
        # The upstream resource/version generator uses git describe, even for
        # static Windows builds. Authenticate its release label to the same commit.
        tag_commit = subprocess.check_output(git + ['rev-parse', 'refs/tags/' + spec['tag'] + '^{commit}'], text=True).strip()
        if tag_commit != spec['commit']:
            raise RuntimeError('Pinned x265 release tag does not identify the locked commit')
    if subprocess.run(git + ['diff', '--quiet', 'HEAD', '--'], check=False).returncode:
        raise RuntimeError('Pinned ' + name + ' contains tracked source modifications')
    # All builds are out of tree. Ignored additions are also rejected because generated
    # headers or globbed source files can otherwise override authenticated Git contents.
    for arguments in [['ls-files', '--others', '--exclude-standard'],
                      ['ls-files', '--others', '--ignored', '--exclude-standard']]:
        if subprocess.check_output(git + arguments, text=True).strip():
            raise RuntimeError('Pinned ' + name + ' contains additional source files')
    for filename in spec['licenseFiles']:
        if not (root / filename).is_file():
            raise RuntimeError('Pinned ' + name + ' license file is missing: ' + filename)


def ensure_encoder_source(name):
    """Fetch only the pinned official commit into a private, verified source cache."""
    spec = LOCK['softwareEncoders'][name]
    root = encoder_source(name)
    if not root.exists():
        SHARED.mkdir(parents=True, exist_ok=True)
        subprocess.run(['git', '-c', 'core.autocrlf=false', 'init', str(root)], check=True)
        subprocess.run(['git', '-C', str(root), 'remote', 'add', 'origin', spec['repository']], check=True)
    if not (root / '.git').is_dir():
        raise RuntimeError('The ' + name + ' source cache is not a Git checkout: ' + str(root))
    has_commit = subprocess.run(['git', '-C', str(root), 'rev-parse', '--verify', 'HEAD'],
                                stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, check=False).returncode == 0
    if not has_commit:
        subprocess.run(['git', '-C', str(root), 'fetch', '--depth', '1', 'origin', spec['commit']], check=True)
        subprocess.run(['git', '-c', 'core.autocrlf=false', '-C', str(root), 'checkout', '--detach', 'FETCH_HEAD'], check=True)
    if name == 'x265':
        tag = 'refs/tags/' + spec['tag']
        has_tag = subprocess.run(['git', '-C', str(root), 'rev-parse', '--verify', tag],
                                 stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, check=False).returncode == 0
        if not has_tag:
            subprocess.run(['git', '-C', str(root), 'fetch', '--depth', '1', 'origin', tag + ':' + tag], check=True)
    verify_encoder_source(name)


def meson_command():
    launcher = SHARED / ('meson-' + LOCK['meson']['version']) / 'meson.py'
    if not launcher.is_file():
        raise RuntimeError('Pinned Meson is missing; run a non-probe build to download it')
    return [sys.executable, str(launcher)]


def ensure_dav1d():
    archive = download_verified(LOCK['dav1d'])
    root = dav1d_source()
    SHARED.mkdir(parents=True, exist_ok=True)
    with tarfile.open(archive) as package:
        members = package.getmembers()
        for item in members:
            path = (SHARED / item.name).resolve()
            if (path != root.resolve() and root.resolve() not in path.parents) or not (item.isdir() or item.isfile()):
                raise RuntimeError('Unexpected dav1d archive member: ' + item.name)
        if not root.exists():
            package.extractall(SHARED)
        # Recheck extracted source against the authenticated archive; never silently build edits.
        for item in members:
            if item.isfile():
                path = SHARED / item.name
                if not path.is_file() or path.read_bytes() != package.extractfile(item).read():
                    raise RuntimeError('Pinned dav1d source was modified: ' + str(path))


def ensure_meson():
    archive = download_verified(LOCK['meson'])
    root = SHARED / ('meson-' + LOCK['meson']['version'])
    root.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(archive) as package:
        for item in package.infolist():
            path = (root / item.filename).resolve()
            if root.resolve() not in path.parents:
                raise RuntimeError('Unexpected Meson archive member: ' + item.filename)
            if not item.is_dir():
                contents = package.read(item)
                if path.is_file() and path.read_bytes() != contents:
                    raise RuntimeError('Pinned Meson source was modified: ' + str(path))
                if not path.exists():
                    path.parent.mkdir(parents=True, exist_ok=True)
                    path.write_bytes(contents)
    (root / 'meson.py').write_text('from mesonbuild.mesonmain import main\n'
                                 'if __name__ == "__main__":\n    raise SystemExit(main())\n')


def vulkan_headers():
    return SHARED / ('Vulkan-Headers-' + LOCK['vulkanHeaders']['tag']) / 'include'


def llvm_archive():
    host = platform.system()
    machine = platform.machine().lower()
    key = 'Darwin-universal' if host == 'Darwin' else host + '-' + ('x86_64' if machine in ('amd64', 'x86_64') else machine)
    return LOCK['llvmMingw']['archives'].get(key)


def llvm_root(cache):
    archive = llvm_archive()
    if not archive:
        return None
    name = archive['file'].removesuffix('.tar.xz').removesuffix('.zip')
    return Path(cache) / 'toolchains' / name


def verify_vulkan_headers():
    root = vulkan_headers().parent
    if not (root / '.git').is_dir():
        raise RuntimeError('Pinned Vulkan-Headers are missing; run a non-probe build to download them')
    commit = subprocess.check_output(['git', '-C', str(root), 'rev-parse', 'HEAD'], text=True).strip()
    if commit != LOCK['vulkanHeaders']['commit']:
        raise RuntimeError('Pinned Vulkan-Headers commit mismatch: ' + commit)
    dirty = subprocess.check_output(['git', '--no-optional-locks', '-c', 'core.autocrlf=false', '-c', 'core.filemode=false',
                                    '-C', str(root), 'status', '--porcelain', '--untracked-files=no'], text=True).strip()
    if dirty:
        raise RuntimeError('Pinned Vulkan-Headers contain tracked modifications')


def ensure_vulkan_headers():
    spec = LOCK['vulkanHeaders']
    root = vulkan_headers().parent
    if not root.exists():
        SHARED.mkdir(parents=True, exist_ok=True)
        subprocess.run(['git', '-c', 'core.autocrlf=false', 'clone', '--depth', '1', '--branch', spec['tag'],
                        spec['repository'], str(root)], check=True)
    verify_vulkan_headers()

def nvcodec_headers():
    return SHARED / ('nv-codec-headers-' + LOCK['nvCodecHeaders']['tag'])


def verify_nvcodec_headers():
    root = nvcodec_headers()
    if not (root / '.git').is_dir():
        raise RuntimeError('Pinned NVENC headers are missing; run a non-probe build to download them')
    commit = subprocess.check_output(['git', '-C', str(root), 'rev-parse', 'HEAD'], text=True).strip()
    if commit != LOCK['nvCodecHeaders']['commit']:
        raise RuntimeError('Pinned NVENC headers commit mismatch: ' + commit)
    dirty = subprocess.check_output(['git', '--no-optional-locks', '-c', 'core.autocrlf=false', '-c', 'core.filemode=false',
                                    '-C', str(root), 'status', '--porcelain', '--untracked-files=no'], text=True).strip()
    if dirty:
        raise RuntimeError('Pinned NVENC headers contain tracked modifications')


def ensure_nvcodec_headers():
    spec = LOCK['nvCodecHeaders']
    root = nvcodec_headers()
    if not root.exists():
        SHARED.mkdir(parents=True, exist_ok=True)
        subprocess.run(['git', '-c', 'core.autocrlf=false', 'clone', '--depth', '1', '--branch', spec['tag'],
                        spec['repository'], str(root)], check=True)
    verify_nvcodec_headers()

def amf_source():
    return SHARED / ('AMF-' + LOCK['amfHeaders']['tag'])


def amf_headers():
    return SHARED / ('AMF-' + LOCK['amfHeaders']['tag'] + '-include')


def verify_amf_headers():
    root = amf_source()
    if not (root / '.git').is_dir():
        raise RuntimeError('Pinned AMF headers are missing; run a non-probe build to download them')
    commit = subprocess.check_output(['git', '-C', str(root), 'rev-parse', 'HEAD'], text=True).strip()
    if commit != LOCK['amfHeaders']['commit']:
        raise RuntimeError('Pinned AMF headers commit mismatch: ' + commit)
    # Compare contents: a sparse checkout can retain stat-only changes after Windows CRLF conversion.
    comparison = subprocess.run(['git', '--no-optional-locks', '-c', 'core.autocrlf=false', '-c', 'core.filemode=false',
                                 '-C', str(root), 'diff', '--quiet', 'HEAD', '--'], check=False)
    if comparison.returncode:
        raise RuntimeError('Pinned AMF headers contain tracked modifications')
    original = root / 'amf/public/include'
    if not (original / 'core/Version.h').is_file():
        raise RuntimeError('Pinned AMF header checkout is incomplete')
    for file in original.rglob('*'):
        if file.is_file():
            installed = amf_headers() / 'AMF' / file.relative_to(original)
            if not installed.is_file() or installed.read_bytes() != file.read_bytes():
                raise RuntimeError('AMF include cache differs from pinned sources; repair the generated include cache before retrying: ' + str(installed))


def ensure_amf_headers():
    spec = LOCK['amfHeaders']
    root = amf_source()
    if not root.exists():
        SHARED.mkdir(parents=True, exist_ok=True)
        subprocess.run(['git', '-c', 'core.autocrlf=false', 'clone', '--depth', '1', '--filter=blob:none', '--sparse',
                        '--branch', spec['tag'], spec['repository'], str(root)], check=True)
        subprocess.run(['git', '-c', 'core.autocrlf=false', '-C', str(root), 'sparse-checkout', 'set', 'amf/public/include'], check=True)
    destination = amf_headers() / 'AMF'
    if not destination.exists():
        shutil.copytree(root / 'amf/public/include', destination)
    verify_amf_headers()


def ensure_llvm(cache):
    if os.environ.get('LLVM_MINGW') or os.environ.get('FFMPEG_D3D12_HEADERS'):
        return
    spec = llvm_archive()
    if not spec:
        return  # An explicitly installed host compiler may still support the target.
    root = llvm_root(cache)
    marker = root / '.majdata-archive-sha256'
    if marker.is_file() and marker.read_text().strip() == spec['sha256']:
        return
    downloads = BUILD_ROOT / 'downloads'
    downloads.mkdir(parents=True, exist_ok=True)
    archive = downloads / spec['file']
    legacy = HERE / '.build/llvm-mingw.zip'
    if not archive.is_file() and spec['file'].endswith('.zip') and legacy.is_file():
        if hashlib.sha256(legacy.read_bytes()).hexdigest() == spec['sha256']:
            shutil.copy2(legacy, archive)
    if not archive.is_file():
        url = LOCK['llvmMingw']['repository'] + '/releases/download/' + LOCK['llvmMingw']['version'] + '/' + spec['file']
        temporary = archive.with_suffix(archive.suffix + '.download')
        print('DOWNLOAD ' + url, flush=True)
        with urllib.request.urlopen(url, timeout=60) as response, temporary.open('wb') as output:
            shutil.copyfileobj(response, output)
        if hashlib.sha256(temporary.read_bytes()).hexdigest() != spec['sha256']:
            raise RuntimeError('Downloaded toolchain archive SHA256 mismatch: ' + str(temporary))
        temporary.replace(archive)
    digest = hashlib.sha256(archive.read_bytes()).hexdigest()
    if digest != spec['sha256']:
        raise RuntimeError('Toolchain archive SHA256 mismatch: ' + str(archive))
    destination = root.parent
    destination.mkdir(parents=True, exist_ok=True)
    # Archives are authenticated above; independently reject paths outside this toolchain root.
    def check_name(name):
        resolved = (destination / name).resolve()
        if resolved != root.resolve() and root.resolve() not in resolved.parents:
            raise RuntimeError('Unexpected archive path: ' + name)
    if archive.suffix == '.zip':
        with zipfile.ZipFile(archive) as package:
            for item in package.infolist():
                check_name(item.filename)
            package.extractall(destination)
    else:
        with tarfile.open(archive) as package:
            for item in package.getmembers():
                check_name(item.name)
                if item.issym() or item.islnk():
                    check_name(str(Path(item.name).parent / item.linkname) if item.issym() else item.linkname)
            package.extractall(destination)
    marker.write_text(spec['sha256'] + '\n')


def prepare(targets, cache):
    ensure_dav1d()
    ensure_meson()
    for name in LOCK['softwareEncoders']:
        ensure_encoder_source(name)
    if any(target.startswith(('win-', 'linux-', 'android-')) for target in targets):
        ensure_vulkan_headers()
    if any(target.startswith(('win-', 'linux-')) for target in targets):
        ensure_nvcodec_headers()
    if any(target.startswith('win-') for target in targets):
        ensure_amf_headers()
        ensure_llvm(cache)


def verify_d3d12_overlay():
    value = os.environ.get('FFMPEG_D3D12_HEADERS')
    if not value:
        return None
    root = Path(value).resolve()
    for name, expected in LOCK['d3d12HeaderOverlay']['files'].items():
        file = root / name
        if not file.is_file() or hashlib.sha256(file.read_bytes()).hexdigest() != expected:
            raise RuntimeError('D3D12 header overlay does not match the pinned LLVM-MinGW headers: ' + str(file))
    return root
