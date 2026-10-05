#!/usr/bin/env python3
"""Pinned FFmpeg source builds; no global installations, no silent ABI upgrades."""
from __future__ import annotations
import argparse
import ctypes
import datetime
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import platform
import re
import shlex
import shutil
import subprocess
import sys
import tempfile
import uuid

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[1]
CACHE = Path(os.environ.get('FFMPEG_BUILD_ROOT', str(HERE / '.build'))).resolve()
SOURCE = CACHE / 'source'
OUTPUT = ROOT / 'Assets/Plugins/MajdataPlay/FFmpeg/Native'
LOCK = json.loads((HERE / 'ffmpeg.lock.json').read_text())
sys.dont_write_bytecode = True
_dependency_spec = importlib.util.spec_from_file_location('ffmpeg_build_dependencies', HERE / 'build-dependencies.py')
DEPENDENCIES = importlib.util.module_from_spec(_dependency_spec)
_dependency_spec.loader.exec_module(DEPENDENCIES)
TARGETS = ['win-x86', 'win-x64', 'linux-x64', 'android-armv7', 'android-arm64',
           'macos-x64', 'macos-arm64', 'ios-arm64', 'ios-simulator-arm64', 'ios-simulator-x64']
HOST = platform.system()
AMF_RATE_CONTROL_PATCH = HERE / 'patches/amf-rate-control.patch'

def write_text_atomic(path, contents):
    """Replace metadata without truncating a file concurrently mapped by Windows or Unity."""
    path = Path(path)
    temporary = path.with_name(path.name + '.' + uuid.uuid4().hex + '.tmp')
    try:
        # Shell scripts and license manifests must retain identical LF bytes on every host.
        with temporary.open('w', encoding='utf-8', newline='\n') as stream:
            stream.write(contents)
        os.replace(temporary, path)
    finally:
        temporary.unlink(missing_ok=True)

def destination_for(target):
    locations = {'win-x86':'Windows/x86', 'win-x64':'Windows/x86_64', 'linux-x64':'Linux/x86_64',
                 'android-armv7':'Android/armeabi-v7a', 'android-arm64':'Android/arm64-v8a',
                 'macos-x64':'macOS/x86_64', 'macos-arm64':'macOS/arm64', 'ios-arm64':'iOS'}
    return OUTPUT / locations[target] if target in locations else CACHE / 'artifacts' / target

def short_path(path):
    value = str(Path(path).resolve())
    if HOST == 'Windows' and Path(value).exists():
        buffer = ctypes.create_unicode_buffer(32768)
        if ctypes.windll.kernel32.GetShortPathNameW(value, buffer, len(buffer)):
            value = buffer.value
    return value.replace('\\', '/')

def posix(path):
    value = short_path(path)
    return '/' + value[0].lower() + value[2:] if HOST == 'Windows' and value[1:2] == ':' else value

def command(args, **kwargs):
    return subprocess.check_output([str(a) for a in args], text=True, **kwargs).strip()

def host_tool(name):
    found = shutil.which(name)
    if found:
        return found
    if HOST == 'Linux':
        for root in [CACHE, HERE / '.build']:
            candidate = root / 'toolchains/linux-packages/usr/bin' / name
            if candidate.is_file():
                return str(candidate)
    return None

def find_bash():
    candidates = [os.environ.get('FFMPEG_BASH'),
                  str(CACHE / 'toolchains/msys64/usr/bin/bash.exe'),
                  str(HERE / '.build/toolchains/msys64/usr/bin/bash.exe'),
                  'C:/msys64/usr/bin/bash.exe', 'C:/Program Files/Git/usr/bin/bash.exe'] if HOST == 'Windows' else ['bash']
    return next((shutil.which(c) for c in candidates if c and shutil.which(c)), None)

def find_ndk():
    candidates = [Path(p) for p in [os.environ.get('ANDROID_NDK_HOME'), os.environ.get('ANDROID_NDK_ROOT')] if p]
    for root in [CACHE, HERE / '.build']:
        candidates += sorted((root / 'toolchains').glob('android-ndk-*'), reverse=True)
    sdk_default = {'Windows': 'AppData/Local/Android/Sdk', 'Linux': 'Android/Sdk', 'Darwin': 'Library/Android/sdk'}[HOST]
    sdk = Path(os.environ.get('ANDROID_SDK_ROOT', os.environ.get('ANDROID_HOME', str(Path.home() / sdk_default))))
    candidates += sorted((sdk / 'ndk').glob('*'), reverse=True)
    if HOST == 'Windows':
        for base in ['C:/Program Files/Unity Editors', 'C:/Program Files/Unity/Hub/Editor']:
            candidates += sorted(Path(base).glob('*/Editor/Data/PlaybackEngines/AndroidPlayer/NDK'), reverse=True)
    elif HOST == 'Darwin':
        candidates += sorted(Path('/Applications/Unity/Hub/Editor').glob('*/Unity.app/Contents/PlaybackEngines/AndroidPlayer/NDK'), reverse=True)
    else:
        candidates += sorted((Path.home() / 'Unity/Hub/Editor').glob('*/Editor/Data/PlaybackEngines/AndroidPlayer/NDK'), reverse=True)
    host_tag = {'Windows':'windows-x86_64','Linux':'linux-x86_64','Darwin':'darwin-x86_64'}[HOST]
    compiler = 'clang.exe' if HOST == 'Windows' else 'clang'
    return next((p for p in candidates if (p / 'source.properties').exists()
                 and (p / 'toolchains/llvm/prebuilt' / host_tag / 'bin' / compiler).exists()), None)

def toolchain(target):
    """Return configure options + POSIX PATH additions, or a concrete skip reason."""
    args, paths = [], []
    if target.startswith('win-'):
        arch, triple = ('x86_64', 'x86_64-w64-mingw32') if target == 'win-x64' else ('x86', 'i686-w64-mingw32')
        candidates = [Path(os.environ['LLVM_MINGW'])] if os.environ.get('LLVM_MINGW') else []
        pinned = DEPENDENCIES.llvm_root(CACHE)
        if pinned:
            candidates.append(pinned)
        for root in [CACHE, HERE / '.build']:
            candidates += sorted((root / 'toolchains').glob('llvm-mingw-*'), reverse=True)
        compiler = next((p / 'bin' / (triple + '-clang' + ('.exe' if HOST == 'Windows' else '')) for p in candidates
                         if (p / 'bin' / (triple + '-clang' + ('.exe' if HOST == 'Windows' else ''))).exists()), None)
        if compiler:
            paths.append(compiler.parent)
            args += ['--cc=' + triple + '-clang', '--cxx=' + triple + '-clang++',
                     '--ar=llvm-ar', '--ranlib=llvm-ranlib', '--nm=llvm-nm', '--strip=llvm-strip',
                     '--windres=' + triple + '-windres', '--extra-ldflags=-static-libgcc']
        elif shutil.which(triple + '-gcc'):
            args += ['--cross-prefix=' + triple + '-', '--extra-ldflags=-static-libgcc']
        else:
            return None, f'Missing {triple} GCC or LLVM_MINGW (portable llvm-mingw root)'
        args += ['--target-os=mingw32', '--arch=' + arch, '--enable-cross-compile', '--enable-w32threads',
                 '--enable-d3d11va', '--enable-d3d12va', '--enable-dxva2', '--enable-schannel']
        overlay = DEPENDENCIES.verify_d3d12_overlay()
        if overlay:
            args += ['--extra-cflags=-I' + shlex.quote(posix(overlay))]
        DEPENDENCIES.verify_amf_headers()
        args += ['--enable-amf', '--disable-decoder=h264_amf,hevc_amf,av1_amf,vp9_amf',
                 '--extra-cflags=-I' + shlex.quote(posix(DEPENDENCIES.amf_headers()))]
    elif target == 'linux-x64':
        if not host_tool('patchelf'):
            return None, 'patchelf is required to verify and set relative ELF runtime paths'
        pkg_config = os.environ.get('PKG_CONFIG', 'pkg-config')
        if not shutil.which(pkg_config):
            return None, 'Linux hardware decoding requires pkg-config, libva development files and libdrm development files'
        if subprocess.run([pkg_config, '--exists', 'libva', 'libdrm'], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL).returncode:
            return None, 'Linux hardware decoding requires pkg-config modules libva and libdrm; install development packages or set PKG_CONFIG_PATH / PKG_CONFIG_SYSROOT_DIR'
        if HOST == 'Linux' and platform.machine().lower() in ('x86_64', 'amd64'):
            args += ['--target-os=linux', '--arch=x86_64', '--cc=' + os.environ.get('CC', 'cc')]
        elif os.environ.get('LINUX_X64_CROSS_PREFIX') and os.environ.get('LINUX_X64_SYSROOT'):
            args += ['--enable-cross-compile', '--target-os=linux', '--arch=x86_64',
                     '--cross-prefix=' + os.environ['LINUX_X64_CROSS_PREFIX'], '--sysroot=' + posix(os.environ['LINUX_X64_SYSROOT'])]
        else:
            return None, 'Needs Linux x64 host or LINUX_X64_CROSS_PREFIX and LINUX_X64_SYSROOT (glibc)'
        args += ['--enable-vaapi', '--enable-libdrm', '--pkg-config=' + pkg_config]
        # Stage sets and verifies ELF RUNPATH with patchelf, avoiding configure/make eval of $ORIGIN.
    elif target.startswith('android-'):
        ndk = find_ndk()
        if not ndk:
            return None, 'Android NDK missing; set ANDROID_NDK_HOME (r27+ recommended)'
        host_tag = {'Windows':'windows-x86_64','Linux':'linux-x86_64','Darwin':'darwin-x86_64'}[HOST]
        bins = ndk / 'toolchains/llvm/prebuilt' / host_tag / 'bin'
        paths.append(bins)
        arm64 = target == 'android-arm64'
        triple = 'aarch64-linux-android' if arm64 else 'armv7a-linux-androideabi'
        api = os.environ.get('ANDROID_API', '23')
        args += ['--target-os=android', '--enable-cross-compile', '--arch=' + ('aarch64' if arm64 else 'arm'),
                 '--cc=clang --target=' + triple + api, '--cxx=clang++ --target=' + triple + api,
                 '--ar=llvm-ar', '--ranlib=llvm-ranlib', '--nm=llvm-nm', '--strip=llvm-strip',
                 '--sysroot=' + posix(bins.parent / 'sysroot'), '--enable-jni', '--enable-mediacodec',
                 '--extra-ldflags=-Wl,-z,max-page-size=16384']
        if not arm64:
            args += ['--cpu=armv7-a', '--extra-cflags=-march=armv7-a -mfloat-abi=softfp -mfpu=neon']
    elif target.startswith(('macos-', 'ios-')):
        if HOST != 'Darwin' or not shutil.which('xcrun'):
            return None, 'Apple SDK and Xcode on macOS required; no redistribution of Apple SDKs'
        ios = target.startswith('ios-')
        simulator = target.startswith('ios-simulator-')
        arch = 'x86_64' if target.endswith('-x64') else 'arm64'
        sdk = 'iphonesimulator' if simulator else ('iphoneos' if ios else 'macosx')
        sysroot = command(['xcrun', '--sdk', sdk, '--show-sdk-path'])
        clang = command(['xcrun', '--sdk', sdk, '--find', 'clang'])
        minimum = os.environ.get('IOS_MIN', '15.0') if ios else os.environ.get('MACOS_MIN', '11.0')
        triple = f'{arch}-apple-ios{minimum}' + ('-simulator' if simulator else '') if ios else f'{arch}-apple-macos{minimum}'
        args += ['--target-os=darwin', '--enable-cross-compile', '--arch=' + ('aarch64' if arch == 'arm64' else arch),
                 '--cc=' + clang, '--sysroot=' + sysroot, '--extra-cflags=-target ' + triple,
                 '--extra-ldflags=-target ' + triple, '--enable-videotoolbox', '--enable-audiotoolbox', '--enable-securetransport']
        if not ios:
            args += ['--install-name-dir=@loader_path']
    if target.startswith(('win-', 'linux-', 'android-')):
        DEPENDENCIES.verify_vulkan_headers()
        args += ['--enable-vulkan', '--disable-vulkan-static',
                 '--extra-cflags=-I' + shlex.quote(posix(DEPENDENCIES.vulkan_headers())),
                 '--disable-hwaccel=apv_vulkan,dpx_vulkan,ffv1_vulkan,prores_raw_vulkan,prores_vulkan']
    if target.startswith(('win-', 'linux-')):
        DEPENDENCIES.verify_nvcodec_headers()
        args += ['--enable-ffnvcodec', '--enable-nvenc', '--disable-nvdec', '--disable-cuvid']
    return (args, paths), None

def amf_rate_control_patch():
    """Identify the reviewed source fix and its runtime-visible build marker."""
    digest = hashlib.sha256(AMF_RATE_CONTROL_PATCH.read_bytes()).hexdigest()
    return {'file': AMF_RATE_CONTROL_PATCH.relative_to(HERE).as_posix(), 'sha256': digest,
            'versionMarker': 'MajdataPlay-AMF-RC-v1-' + digest[:12]}

def source_git(*args, environment=None, quiet=False):
    """Run Git against the cache without shell interpolation or changing its real index."""
    # Older Windows caches may have CRLF checkouts. Normalize them when comparing
    # Git blobs and applying LF patches; newly cloned source remains strictly LF.
    result = subprocess.run(['git', '-c', 'core.autocrlf=input', '-c', 'core.safecrlf=false', '-c', 'core.filemode=false',
                             '-C', str(SOURCE), *[str(arg) for arg in args]], env=environment,
                            stdout=subprocess.DEVNULL if quiet else None, check=False)
    if result.returncode not in (0, 1):
        raise subprocess.CalledProcessError(result.returncode, result.args)
    return result.returncode == 0

def apply_verified_source_patch():
    """Accept exactly the pinned tree or the reviewed patch, rejecting other tracked edits."""
    patch = amf_rate_control_patch()
    if not source_git('diff', '--cached', '--quiet', '--no-ext-diff', 'HEAD', '--', quiet=True):
        raise RuntimeError('FFmpeg source index contains tracked modifications; refusing an unrecorded build')
    # A temporary index describes the exact reviewed tree. Comparing every tracked
    # working-tree file to it also catches edits outside the patch and partial patches.
    with tempfile.TemporaryDirectory(prefix='ffmpeg-patch-index-', dir=CACHE) as directory:
        environment = os.environ.copy()
        environment['GIT_INDEX_FILE'] = str(Path(directory) / 'index')
        if not source_git('read-tree', 'HEAD', environment=environment):
            raise RuntimeError('Cannot prepare the pinned FFmpeg patch verification index')
        if not source_git('apply', '--cached', '--whitespace=error-all', AMF_RATE_CONTROL_PATCH,
                          environment=environment):
            raise RuntimeError('The reviewed AMF patch does not apply to the pinned FFmpeg commit')
        if not source_git('diff', '--quiet', '--no-ext-diff', '--', environment=environment, quiet=True):
            if not source_git('diff', '--quiet', '--no-ext-diff', 'HEAD', '--', quiet=True):
                raise RuntimeError('FFmpeg source contains tracked edits other than the reviewed AMF patch')
            if not source_git('apply', '--check', '--whitespace=error-all', AMF_RATE_CONTROL_PATCH):
                raise RuntimeError('Cannot apply the reviewed AMF patch to the pinned FFmpeg tree')
            if not source_git('apply', '--whitespace=error-all', AMF_RATE_CONTROL_PATCH):
                raise RuntimeError('Applying the reviewed AMF patch failed')
            if not source_git('diff', '--quiet', '--no-ext-diff', '--', environment=environment, quiet=True):
                raise RuntimeError('FFmpeg source differs from the reviewed patched tree')
    print('Verified AMF source patch SHA256 ' + patch['sha256'] + '.', flush=True)
    return patch

def verify_source():
    if not (SOURCE / '.git').exists():
        SOURCE.parent.mkdir(parents=True, exist_ok=True)
        subprocess.run(['git', '-c', 'core.autocrlf=false', 'clone', '--depth', '1', '--branch', LOCK['tag'], LOCK['repository'], str(SOURCE)], check=True)
    actual = command(['git', '-C', SOURCE, 'rev-parse', 'HEAD'])
    if actual != LOCK['commit']:
        raise RuntimeError(f'FFmpeg source commit mismatch: {actual}; expected {LOCK["commit"]}')
    headers = ROOT / LOCK['bindingHeaders']
    checked = 0
    for file in headers.rglob('*.h'):
        if file.name in ('avconfig.h', 'ffversion.h'):
            continue
        original = SOURCE / file.relative_to(headers)
        if not original.is_file() or file.read_bytes().replace(b'\r\n', b'\n') != original.read_bytes().replace(b'\r\n', b'\n'):
            raise RuntimeError(f'Binding header mismatch: {file.relative_to(headers)}; regenerate bindings against pinned source')
        checked += 1
    print(f'Verified source commit and {checked} matching public binding headers.', flush=True)
    return apply_verified_source_patch()

def shell(bash, text, cwd, log):
    with log.open('w', encoding='utf-8') as output:
        subprocess.run([bash, '-c', text], cwd=cwd, stdout=output, stderr=subprocess.STDOUT, check=True)


def dav1d_tools(paths):
    environment = os.environ.copy()
    environment['PATH'] = os.pathsep.join([str(p) for p in paths] + [environment.get('PATH', '')])
    for tool in ['ninja', os.environ.get('PKG_CONFIG', 'pkg-config')]:
        if not shutil.which(tool, path=environment['PATH']):
            raise RuntimeError('AV1 software decoding requires build tool ' + tool)
    DEPENDENCIES.meson_command()
    return environment


def build_dav1d(target, specific, paths, jobs):
    """Build a PIC static decoder with the same target compiler, ABI and SDK as FFmpeg."""
    environment = dav1d_tools(paths)
    flags = dict(arg[2:].split('=', 1) for arg in specific if arg.startswith('--') and '=' in arg)
    cross = flags.get('cross-prefix', '')
    compiler = flags.get('cc', cross + 'gcc')
    cc = [compiler] if Path(compiler).is_file() else shlex.split(compiler)
    ar = flags.get('ar', cross + 'ar')
    c_args, link_args = [], []
    for arg in specific:
        if arg.startswith('--extra-cflags='):
            c_args += shlex.split(arg.split('=', 1)[1])
        elif arg.startswith('--extra-ldflags='):
            link_args += shlex.split(arg.split('=', 1)[1])
    if flags.get('sysroot'):
        c_args += ['--sysroot=' + flags['sysroot']]
        link_args += ['--sysroot=' + flags['sysroot']]
    # Meson runs as a native host tool, while FFmpeg configure runs in Bash.
    # Resolve POSIX drive paths back to native paths for Windows SDK/compiler arguments.
    def native(value):
        return re.sub(r'(^|=|-I|-L)/([a-zA-Z])/', lambda match: match.group(1) + match.group(2) + ':/', value) if HOST == 'Windows' else value
    cc = [native(value) for value in cc]
    c_args = [native(value) for value in c_args]
    link_args = [native(value) for value in link_args]
    if not shutil.which(cc[0], path=environment['PATH']):
        raise RuntimeError('Missing dav1d target compiler: ' + cc[0])
    system = 'windows' if target.startswith('win-') else ('android' if target.startswith('android-') else
             'ios' if target.startswith('ios-') else 'darwin' if target.startswith('macos-') else 'linux')
    family = 'x86' if target == 'win-x86' else 'arm' if target == 'android-armv7' else 'x86_64' if target.endswith('-x64') else 'aarch64'
    nasm = shutil.which('nasm', path=environment['PATH'])
    asm = not family.startswith('x86') or bool(nasm)
    if not asm:
        print('NOTICE ' + target + ': nasm unavailable; dav1d x86 assembly disabled.', flush=True)
    # repr strings use Meson's single-quoted machine-file syntax, not shell interpolation.
    machine = '[binaries]\nc = ' + repr(cc) + '\nar = ' + repr(ar) + '\n'
    if system == 'windows':
        machine += 'windres = ' + repr(flags.get('windres', cross + 'windres')) + '\n'
    machine += '[host_machine]\nsystem = ' + repr(system) + '\ncpu_family = ' + repr(family) + '\n'
    machine += 'cpu = ' + repr('armv7' if family == 'arm' else family) + "\nendian = 'little'\n"
    machine += '[properties]\nneeds_exe_wrapper = true\n'
    machine += '[built-in options]\nc_args = ' + repr(c_args) + '\nc_link_args = ' + repr(link_args) + '\n'
    identity = hashlib.sha256((machine + str(asm) + json.dumps(DEPENDENCIES.LOCK['dav1d']) +
                              json.dumps(DEPENDENCIES.LOCK['meson'])).encode()).hexdigest()[:12]
    work = CACHE / ('dav1d-' + target + '-' + identity)
    prefix = work / 'install'
    work.mkdir(parents=True, exist_ok=True)
    cross_file = work / 'cross.ini'
    write_text_atomic(cross_file, machine)
    meson = DEPENDENCIES.meson_command()
    build = work / 'build'
    setup = meson + ['setup', str(build), str(DEPENDENCIES.dav1d_source()), '--cross-file', str(cross_file),
                     '--prefix', str(prefix), '--libdir', 'lib', '--buildtype', 'release', '--default-library', 'static',
                     '-Db_staticpic=true', '-Dbitdepths=8,16', '-Denable_tools=false', '-Denable_tests=false',
                     '-Denable_examples=false', '-Denable_asm=' + str(asm).lower()]
    if (build / 'meson-private/coredata.dat').is_file():
        setup += ['--reconfigure']
    for command_line, log_name in [(setup, 'configure.log'),
        (meson + ['compile', '-C', str(build), '-j', str(max(1, jobs))], 'build.log'),
        (meson + ['install', '-C', str(build), '--no-rebuild'], 'install.log')]:
        with (work / log_name).open('w', encoding='utf-8') as output:
            subprocess.run(command_line, env=environment, stdout=output, stderr=subprocess.STDOUT, check=True)
    if not (prefix / 'lib/libdav1d.a').is_file():
        raise RuntimeError('Missing static dav1d archive: ' + str(prefix))
    # dav1d's 16-bit implementation decodes high-bit-depth AV1, including 10-bit.
    configuration = (build / 'config.h').read_text(encoding='utf-8')
    for depth in (8, 16):
        if not re.search(r'^#define CONFIG_' + str(depth) + r'BPC 1$', configuration, re.MULTILINE):
            raise RuntimeError('dav1d was built without the ' + str(depth) + '-bit implementation')
    # dav1d and the header-only NVENC SDK live outside an optional Linux dependency sysroot.
    # Keep libva/libdrm searches unchanged when forwarding their pkg-config calls.
    pkg_config = flags.get('pkg-config', os.environ.get('PKG_CONFIG', 'pkg-config'))
    wrapper = work / 'pkg-config.sh'
    nvcodec_case = ''
    if target.startswith(('win-', 'linux-')):
        nvcodec_root = DEPENDENCIES.nvcodec_headers()
        nvcodec_pkgconfig = work / 'nvcodec-pkgconfig'
        nvcodec_pkgconfig.mkdir(parents=True, exist_ok=True)
        entry = (nvcodec_root / 'ffnvcodec.pc.in').read_text().replace('@@PREFIX@@', posix(nvcodec_root))
        write_text_atomic(nvcodec_pkgconfig / 'ffnvcodec.pc', entry)
        nvcodec_case = '  *" ffnvcodec "*)\n    unset PKG_CONFIG_SYSROOT_DIR PKG_CONFIG_LIBDIR\n' \
            + '    export PKG_CONFIG_PATH=' + shlex.quote(posix(nvcodec_pkgconfig)) + '\n    ;;\n'
    write_text_atomic(wrapper, '#!/bin/sh\ncase " $* " in\n  *" dav1d "*)\n'
        '    unset PKG_CONFIG_SYSROOT_DIR PKG_CONFIG_LIBDIR\n'
        '    export PKG_CONFIG_PATH=' + shlex.quote(posix(prefix / 'lib/pkgconfig')) + '\n'
        '    set -- --static "$@"\n    ;;\n' + nvcodec_case + 'esac\n'
        'exec ' + shlex.quote(pkg_config) + ' "$@"\n')
    return prefix, ['--enable-libdav1d', '--pkg-config=' + shlex.join(['sh', posix(wrapper)])]


def verify_software_configuration(work):
    configuration = '\n'.join((work / name).read_text() for name in ['config.h', 'config_components.h'])
    if not re.search(r'^#define CONFIG_LIBDAV1D_DECODER 1$', configuration, re.MULTILINE):
        raise RuntimeError('AV1 software decoder libdav1d was disabled during FFmpeg configuration')

def write_meta(file, target):
    meta = Path(str(file) + '.meta')
    guid = uuid.uuid5(uuid.NAMESPACE_URL, 'majdata-ffmpeg/' + file.relative_to(OUTPUT).as_posix()).hex
    if meta.is_file():
        existing_guid = re.search(r'^guid:\s*([0-9a-fA-F]{32})\s*$', meta.read_text(encoding='utf-8'), re.MULTILINE)
        if not existing_guid:
            raise RuntimeError('Existing Unity importer has no valid GUID: ' + str(meta))
        guid = existing_guid.group(1)
    if target.startswith('win-'):
        platform_name, cpu, editor_os = ('Win64', 'x86_64', 'Windows') if target == 'win-x64' else ('Win', 'x86', '')
    elif target == 'linux-x64':
        platform_name, cpu, editor_os = 'Linux64', 'x86_64', 'Linux'
    elif target.startswith('android-'):
        platform_name, cpu, editor_os = 'Android', ('ARM64' if target.endswith('arm64') else 'ARMv7'), ''
    elif target.startswith('macos-'):
        platform_name, cpu, editor_os = 'OSXUniversal', ('ARM64' if target.endswith('arm64') else 'x86_64'), 'OSX'
    else:
        platform_name, cpu, editor_os = 'iOS', 'AnyCPU', ''
    prefix = {'Android':'Android', 'iOS':'iPhone'}.get(platform_name, 'Standalone')
    write_text_atomic(meta, f'''fileFormatVersion: 2
guid: {guid}
PluginImporter:
  externalObjects: {{}}
  serializedVersion: 2
  iconMap: {{}}
  executionOrder: {{}}
  defineConstraints: []
  isPreloaded: {1 if file.name in ('FFmpegUnityBridge.dll', 'libFFmpegUnityBridge.so') else 0}
  isOverridable: 0
  isExplicitlyReferenced: 0
  validateReferences: 1
  platformData:
  - first:
      Any:
    second:
      enabled: 0
      settings: {{}}
  - first:
      Editor: Editor
    second:
      enabled: {1 if editor_os else 0}
      settings:
        CPU: {cpu if editor_os else 'AnyCPU'}
        OS: {editor_os or 'AnyOS'}
  - first:
      {prefix}: {platform_name}
    second:
      enabled: 1
      settings:
        CPU: {cpu}
  userData:
  assetBundleName:
  assetBundleVariant:
''')

def stage_linux_dependencies(destination):
    """Bundle redistributable loader dependencies, never vendor-specific GPU drivers."""
    pkg_config = os.environ.get('PKG_CONFIG', 'pkg-config')
    sysroot = Path(os.environ.get('PKG_CONFIG_SYSROOT_DIR', '/'))
    dependencies = [('libva', 'libva.so.2', ['libva2', 'libva-dev', 'libva']),
                    ('libva-drm', 'libva-drm.so.2', ['libva-drm2', 'libva-dev', 'libva']),
                    ('libdrm', 'libdrm.so.2', ['libdrm2', 'libdrm-dev', 'libdrm'])]
    records = []
    for module, name, packages in dependencies:
        libdir = Path(command([pkg_config, '--variable=libdir', module]))
        pcdir = Path(command([pkg_config, '--variable=pcfiledir', module]))
        locations = [pcdir.parent, libdir]
        if libdir.is_absolute() and sysroot != Path('/'):
            locations.insert(0, sysroot / str(libdir).lstrip('/'))
        origin = next((directory / name for directory in locations if (directory / name).is_file()), None)
        if not origin:
            raise RuntimeError(f'Missing redistributable Linux runtime {name}; checked {locations}')
        licenses = []
        roots = [sysroot]
        for parent in pcdir.parents:
            if parent.name == 'usr':
                roots.insert(0, parent.parent)
                break
        extra_license = os.environ.get('FFMPEG_LINUX_RUNTIME_LICENSE_DIR')
        if extra_license:
            licenses.extend(Path(extra_license) / (module + extension) for extension in ['.copyright', '.LICENSE'])
        for root in roots:
            for package in packages:
                licenses += [root / 'usr/share/doc' / package / 'copyright',
                             root / 'usr/share/licenses' / package / 'COPYING',
                             root / 'usr/share/licenses' / package / 'LICENSE']
        copyright_file = next((file for file in licenses if file.is_file()), None)
        if not copyright_file:
            raise RuntimeError(f'Missing {module} redistribution license; set FFMPEG_LINUX_RUNTIME_LICENSE_DIR with {module}.copyright')
        output = destination / name
        shutil.copy2(origin, output, follow_symlinks=True)
        subprocess.run([host_tool('patchelf'), '--set-rpath', '$ORIGIN', str(output)], check=True)
        if command([host_tool('patchelf'), '--print-rpath', output]) != '$ORIGIN':
            raise RuntimeError('Linux dependency RUNPATH verification failed: ' + str(output))
        write_meta(output, 'linux-x64')
        license_name = module + '.copyright'
        shutil.copy2(copyright_file, destination / license_name)
        records.append({'file': name, 'bytes': output.stat().st_size, 'sha256': hashlib.sha256(output.read_bytes()).hexdigest(),
                        'version': command([pkg_config, '--modversion', module]), 'license': license_name,
                        'licenseSha256': hashlib.sha256((destination / license_name).read_bytes()).hexdigest()})
    report = {'target': 'linux-x64', 'files': records, 'postProcessing': 'patchelf --set-rpath $ORIGIN',
              'note': 'GPU-specific VAAPI/Vulkan driver implementations are supplied by the host system.'}
    write_text_atomic(destination / 'dependency-manifest.json', json.dumps(report, indent=2) + '\n')
    return report


def stage_source_patch(destination, patch):
    """Ship the exact source correction needed to reproduce the native binaries."""
    if hashlib.sha256(AMF_RATE_CONTROL_PATCH.read_bytes()).hexdigest() != patch['sha256']:
        raise RuntimeError('The reviewed AMF source patch changed during the build')
    file = destination / AMF_RATE_CONTROL_PATCH.name
    shutil.copy2(AMF_RATE_CONTROL_PATCH, file)
    if OUTPUT in file.parents:
        meta = Path(str(file) + '.meta')
        if not meta.exists():
            guid = uuid.uuid5(uuid.NAMESPACE_URL, 'majdata-ffmpeg/' + file.relative_to(OUTPUT).as_posix()).hex
            write_text_atomic(meta, 'fileFormatVersion: 2\nguid: ' + guid + '\nDefaultImporter:\n'
                             '  externalObjects: {}\n  userData:\n  assetBundleName:\n  assetBundleVariant:\n')
    return dict(patch, stagedFile=file.name)

def stage(target, prefix, config, logs, dav1d_prefix, source_patch):
    # Simulator archives remain outside Assets: device and simulator cannot both be linked by Unity.
    destination = destination_for(target)
    destination.mkdir(parents=True, exist_ok=True)
    produced = []
    for library, major in LOCK['abi'].items():
        if target.startswith('win-'):
            name, origin = f'{library}-{major}.dll', prefix / 'bin'
        elif target.startswith('android-'):
            name, origin = f'lib{library}.so', prefix / 'lib'
        elif target.startswith('macos-'):
            name, origin = f'lib{library}.{major}.dylib', prefix / 'lib'
        elif target.startswith('ios-'):
            name, origin = f'lib{library}.a', prefix / 'lib'
        else:
            name, origin = f'lib{library}.so.{major}', prefix / 'lib'
        source_file = origin / name
        if not source_file.exists():
            raise RuntimeError(f'Missing expected library {source_file}')
        file = destination / name
        if target.startswith('ios-') and library == 'avcodec':
            # Unity links seven FFmpeg archives. Carry dav1d inside avcodec rather than
            # introducing an eighth plugin or relying on transitive static linkage.
            subprocess.run(['xcrun', 'libtool', '-static', '-o', str(file), str(source_file),
                            str(dav1d_prefix / 'lib/libdav1d.a')], check=True)
        else:
            shutil.copy2(source_file, file, follow_symlinks=True)
        if target == 'linux-x64':
            patcher = host_tool('patchelf')
            subprocess.run([patcher, '--set-rpath', '$ORIGIN', str(file)], check=True)
            if command([patcher, '--print-rpath', file]) != '$ORIGIN':
                raise RuntimeError('Linux library RUNPATH verification failed: ' + str(file))
        if 'simulator' not in target:
            write_meta(file, target)
        produced.append({'file': name, 'bytes': file.stat().st_size, 'sha256': hashlib.sha256(file.read_bytes()).hexdigest()})
    for name in ['COPYING.LGPLv2.1', 'LICENSE.md']:
        shutil.copy2(SOURCE / name, destination / name)
    report = {'target': target, 'source': LOCK, 'host': platform.platform(), 'builtUtc': datetime.datetime.now(datetime.timezone.utc).isoformat(),
              'configure': config, 'files': produced, 'softwareDecoders': ['libdav1d'],
              'recordingProfile': verify_recording_configuration(target, logs),
              'buildDependencies': DEPENDENCIES.LOCK,
              'sourcePatches': [stage_source_patch(destination, source_patch)],
              'buildDependencyLicenses': stage_dav1d_license(destination)}
    if target.startswith(('win-', 'linux-', 'android-')):
        report['verifiedHardwareBackends'] = verify_hardware_configuration(target, logs)
        report['buildDependencyLicenses'] += stage_build_dependency_licenses(destination)
        if target.startswith(('win-', 'linux-')):
            report['buildDependencyLicenses'] += stage_nvcodec_license(destination)
        if target.startswith('win-'):
            report['buildDependencyLicenses'] += stage_amf_license(destination)
        if target.startswith('win-') and os.environ.get('FFMPEG_D3D12_HEADERS'):
            report['d3d12HeaderOverlay'] = {'directory': str(DEPENDENCIES.verify_d3d12_overlay()),
                                          'files': DEPENDENCIES.LOCK['d3d12HeaderOverlay']['files']}
    if target == 'linux-x64':
        report['postProcessing'] = 'patchelf --set-rpath $ORIGIN (verified by --print-rpath)'
        stage_linux_dependencies(destination)
    write_text_atomic(destination / 'build-manifest.json', json.dumps(report, indent=2) + '\n')
    write_text_atomic(destination / 'configure.txt', (logs / 'configure.log').read_text(encoding='utf-8', errors='replace'))
    return report


def stage_dav1d_license(destination):
    file = destination / 'dav1d.LICENSE.txt'
    spec = DEPENDENCIES.LOCK['dav1d']
    write_text_atomic(file, 'dav1d ' + spec['version'] + '\n' + spec['url'] + '\nSHA256: ' + spec['sha256'] + '\n\n' +
                      (DEPENDENCIES.dav1d_source() / 'COPYING').read_text(encoding='utf-8'))
    if OUTPUT in file.parents:
        meta = Path(str(file) + '.meta')
        if not meta.exists():
            guid = uuid.uuid5(uuid.NAMESPACE_URL, 'majdata-ffmpeg/' + file.relative_to(OUTPUT).as_posix()).hex
            write_text_atomic(meta, 'fileFormatVersion: 2\nguid: ' + guid + '\nTextScriptImporter:\n'
                             '  externalObjects: {}\n  userData:\n  assetBundleName:\n  assetBundleVariant:\n')
    return [{'file': file.name, 'sha256': hashlib.sha256(file.read_bytes()).hexdigest(), 'license': 'BSD-2-Clause'}]


def stage_build_dependency_licenses(destination):
    """Carry the pinned Vulkan header attribution and selected Apache license with built libraries."""
    headers = DEPENDENCIES.vulkan_headers().parent
    file = destination / 'Vulkan-Headers.LICENSE.txt'
    contents = 'Vulkan-Headers ' + DEPENDENCIES.LOCK['vulkanHeaders']['tag'] + '\n'
    contents += 'Copyright 2015-2025 The Khronos Group Inc.\n'
    contents += DEPENDENCIES.LOCK['vulkanHeaders']['repository'] + '\n'
    contents += 'Commit: ' + DEPENDENCIES.LOCK['vulkanHeaders']['commit'] + '\n\n'
    contents += (headers / 'LICENSE.md').read_text(encoding='utf-8') + '\n\n'
    contents += (headers / 'LICENSES/Apache-2.0.txt').read_text(encoding='utf-8')
    write_text_atomic(file, contents)
    meta = Path(str(file) + '.meta')
    if not meta.exists():
        guid = uuid.uuid5(uuid.NAMESPACE_URL, 'majdata-ffmpeg/' + file.relative_to(OUTPUT).as_posix()).hex
        write_text_atomic(meta, 'fileFormatVersion: 2\nguid: ' + guid + '\nTextScriptImporter:\n'
                         '  externalObjects: {}\n  userData:\n  assetBundleName:\n  assetBundleVariant:\n')
    return [{'file': file.name, 'sha256': hashlib.sha256(file.read_bytes()).hexdigest(), 'license': 'Apache-2.0'}]


def verify_hardware_configuration(target, work):
    """Reject successful configure runs that silently omitted requested video decoders."""
    required = ['VULKAN', 'H264_VULKAN_HWACCEL', 'HEVC_VULKAN_HWACCEL', 'AV1_VULKAN_HWACCEL', 'VP9_VULKAN_HWACCEL']
    if target.startswith('win-'):
        required += ['D3D12VA', 'H264_D3D12VA_HWACCEL', 'HEVC_D3D12VA_HWACCEL', 'AV1_D3D12VA_HWACCEL', 'VP9_D3D12VA_HWACCEL']
    configuration = '\n'.join((work / name).read_text() for name in ['config.h', 'config_components.h'])
    missing = [name for name in required if not re.search(r'^#define CONFIG_' + name + r' 1$', configuration, re.MULTILINE)]
    if missing:
        raise RuntimeError('Requested native hardware backend was disabled: ' + ', '.join(missing))
    return required

def stage_nvcodec_license(destination):
    """Carry all NVIDIA header notices with NVENC-enabled binaries."""
    DEPENDENCIES.verify_nvcodec_headers()
    notices = []
    for header in sorted((DEPENDENCIES.nvcodec_headers() / 'include/ffnvcodec').glob('*.h')):
        text = header.read_text(encoding='utf-8')
        notice = re.match(r'\s*(/\*.*?\*/)', text, re.DOTALL)
        if not notice:
            raise RuntimeError('Missing NVIDIA header copyright notice: ' + str(header))
        notices.append(header.name + '\n' + notice.group(1) + '\n')
    file = destination / 'nv-codec-headers.LICENSE.txt'
    write_text_atomic(file, '\n'.join(notices))
    meta = Path(str(file) + '.meta')
    if not meta.exists():
        guid = uuid.uuid5(uuid.NAMESPACE_URL, 'majdata-ffmpeg/' + file.relative_to(OUTPUT).as_posix()).hex
        write_text_atomic(meta, 'fileFormatVersion: 2\nguid: ' + guid + '\nTextScriptImporter:\n'
                         '  externalObjects: {}\n  userData:\n  assetBundleName:\n  assetBundleVariant:\n')
    return [{'file': file.name, 'sha256': hashlib.sha256(file.read_bytes()).hexdigest(), 'license': 'MIT'}]

def stage_amf_license(destination):
    """Carry the pinned AMD AMF header license with Windows encoder binaries."""
    DEPENDENCIES.verify_amf_headers()
    file = destination / 'AMF.LICENSE.txt'
    source = DEPENDENCIES.amf_source()
    copyrights = set()
    for header in (source / 'amf/public/include').rglob('*.h'):
        copyrights.update(re.findall(r'^\s*//\s*(Copyright[^\r\n]+)', header.read_text(encoding='utf-8'), re.MULTILINE))
    contents = (source / 'LICENSE.txt').read_text(encoding='utf-8').rstrip() + '\n\nHeader copyright notices:\n'
    write_text_atomic(file, contents + '\n'.join(sorted(copyrights)) + '\n')
    meta = Path(str(file) + '.meta')
    if not meta.exists():
        guid = uuid.uuid5(uuid.NAMESPACE_URL, 'majdata-ffmpeg/' + file.relative_to(OUTPUT).as_posix()).hex
        write_text_atomic(meta, 'fileFormatVersion: 2\nguid: ' + guid + '\nTextScriptImporter:\n'
                         '  externalObjects: {}\n  userData:\n  assetBundleName:\n  assetBundleVariant:\n')
    return [{'file': file.name, 'sha256': hashlib.sha256(file.read_bytes()).hexdigest(), 'license': 'MIT'}]

def recording_configuration(target):
    """Enable a small recording profile without new external codec dependencies."""
    encoders = ['mpeg4']
    if target.startswith(('win-', 'linux-')):
        encoders += ['h264_nvenc', 'hevc_nvenc', 'av1_nvenc']
    if target.startswith('win-'):
        encoders += ['h264_amf', 'hevc_amf', 'av1_amf']
    if target == 'linux-x64':
        encoders += ['h264_vaapi', 'hevc_vaapi', 'vp9_vaapi', 'av1_vaapi']
    elif target.startswith(('macos-', 'ios-')):
        encoders += ['h264_videotoolbox']
    muxers = ['mov', 'mp4', 'matroska', 'webm', 'avi']
    return encoders, muxers

def verify_recording_configuration(target, work):
    """Reject builds that silently omit an encoder or container in the recording profile."""
    encoders, muxers = recording_configuration(target)
    configuration = (work / 'config_components.h').read_text()
    required = [name.upper() + '_ENCODER' for name in encoders]
    required += [name.upper() + '_MUXER' for name in muxers]
    missing = [name for name in required if not re.search(r'^#define CONFIG_' + name + r' 1$', configuration, re.MULTILINE)]
    if missing:
        raise RuntimeError('Requested recording component was disabled: ' + ', '.join(missing))
    return {'encoders': encoders, 'muxers': muxers}

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--targets', default='all', help='comma separated targets, or all')
    parser.add_argument('--jobs', type=int, default=min(os.cpu_count() or 4, 16))
    parser.add_argument('--require-all', action='store_true', help='fail if any requested SDK is unavailable')
    parser.add_argument('--probe', action='store_true', help='only inspect toolchains; no downloads or builds')
    parser.add_argument('--with-bridge', action='store_true', default=True, help='build native GPU bridge (default)')
    parser.add_argument('--without-bridge', action='store_false', dest='with_bridge', help='omit optional desktop bridge; not permitted for iOS')
    parser.add_argument('--unity-plugin-api', default=os.environ.get('UNITY_PLUGIN_API'))
    options = parser.parse_args()
    targets = TARGETS if options.targets == 'all' else options.targets.split(',')
    if set(targets) - set(TARGETS):
        parser.error('Unknown targets: ' + ', '.join(set(targets) - set(TARGETS)))
    bash = find_bash()
    if not bash:
        parser.error('Bash required: set FFMPEG_BASH to a working MSYS2 bash.exe on Windows')
    if not options.probe:
        CACHE.mkdir(parents=True, exist_ok=True)
        DEPENDENCIES.prepare(targets, CACHE)
    results = []
    available = []
    for target in targets:
        try:
            config, reason = toolchain(target)
            if not reason:
                dav1d_tools(config[1])
            if not reason and options.with_bridge:
                bridge_plan(target, CACHE / 'install' / target, options, config)
        except (OSError, subprocess.CalledProcessError, RuntimeError) as error:
            config, reason = None, 'Toolchain/SDK probe failed: ' + str(error)
        if reason:
            print(f'SKIP {target}: {reason}', flush=True)
            results.append({'target': target, 'status': 'unavailable', 'reason': reason})
        else:
            if target.startswith('ios-') and not options.with_bridge:
                parser.error('iOS requires the static bridge for __Internal P/Invoke symbols')
            print(f'READY {target}', flush=True)
            available.append((target, config))
    if options.probe:
        return 1 if options.require_all and results else 0
    if available:
        source_patch = verify_source()
    for target, (specific, paths) in available:
        work = CACHE / target
        prefix = CACHE / 'install' / target
        work.mkdir(parents=True, exist_ok=True)
        prefix.mkdir(parents=True, exist_ok=True)
        config = ['--prefix=' + posix(prefix), '--disable-doc', '--disable-programs', '--disable-debug', '--disable-autodetect',
                  '--disable-encoders', '--disable-muxers', '--disable-filters', '--disable-devices', '--enable-pic',
                  '--disable-gpl', '--disable-nonfree', '--extra-version=' + source_patch['versionMarker']]
        encoders, muxers = recording_configuration(target)
        config += ['--enable-encoder=' + ','.join(encoders), '--enable-muxer=' + ','.join(muxers)]
        config += ['--disable-shared', '--enable-static'] if target.startswith('ios-') else ['--enable-shared', '--disable-static']
        config += specific
        if target.startswith('win-') or target.endswith('-x64'):
            nasm = host_tool('nasm')
            portable_nasm = CACHE / 'toolchains/nasm-package/usr/bin/nasm'
            if not nasm and portable_nasm.is_file():
                nasm = str(portable_nasm)
                paths.append(portable_nasm.parent)
            elif nasm:
                paths.append(Path(nasm).parent)
            if not nasm:
                print(f'NOTICE {target}: nasm unavailable; x86 assembly optimizations disabled.', flush=True)
                config += ['--disable-x86asm']
        setup = 'export PATH=' + shlex.quote(':'.join(posix(p) for p in paths) + ':/usr/bin') + ':"$PATH"; '
        make = os.environ.get('FFMPEG_MAKE', 'make')
        try:
            print(f'BUILD {target}; logs: {work}', flush=True)
            dav1d_prefix, dav1d_config = build_dav1d(target, specific, paths, options.jobs)
            config += dav1d_config
            settings_file = work / 'build-settings.json'
            settings = json.dumps({'commit':LOCK['commit'], 'configure':config,
                                   'toolPaths':[str(p) for p in paths], 'runtimePathFix':1,
                                   'sourcePatches': [source_patch],
                                   'dav1d': DEPENDENCIES.LOCK['dav1d']}, sort_keys=True)
            if (work / 'ffbuild/config.mak').exists() and (not settings_file.exists() or settings_file.read_text() != settings):
                shell(bash, setup + shlex.quote(make) + ' clean', work, work / 'clean.log')
            shell(bash, setup + 'bash ' + shlex.quote(posix(SOURCE / 'configure')) + ' ' + shlex.join(config), work, work / 'configure.log')
            verify_software_configuration(work)
            recording_profile = verify_recording_configuration(target, work)
            if target.startswith(('win-', 'linux-', 'android-')):
                verify_hardware_configuration(target, work)
            shell(bash, setup + shlex.quote(make) + f' -j{max(1, options.jobs)}', work, work / 'build.log')
            shell(bash, setup + shlex.quote(make) + ' install', work, work / 'install.log')
            write_text_atomic(settings_file, settings)
            provenance = {'target': target, 'source': LOCK, 'host': platform.platform(),
                          'builtUtc': datetime.datetime.now(datetime.timezone.utc).isoformat(),
                          'configure': config, 'buildDependencies': DEPENDENCIES.LOCK, 'softwareDecoders': ['libdav1d'],
                          'sourcePatches': [source_patch],
                          'recordingProfile': recording_profile}
            if target.startswith(('win-', 'linux-', 'android-')):
                provenance['verifiedHardwareBackends'] = verify_hardware_configuration(target, work)
            # Keep the build evidence beside the usable prefix even when a loaded Unity DLL prevents staging.
            write_text_atomic(prefix / 'build-provenance.json', json.dumps(provenance, indent=2) + '\n')
            shutil.copy2(work / 'configure.log', prefix / 'build-configure.txt')
            report = stage(target, prefix, config, work, dav1d_prefix, source_patch)
            if options.with_bridge:
                build_bridge(target, prefix, options, (specific, paths))
            results.append({'target': target, 'status': 'built', 'files': report['files']})
            print(f'OK {target}', flush=True)
        except (OSError, subprocess.CalledProcessError, RuntimeError) as error:
            print(f'FAILED {target}: {error}; see {work}', file=sys.stderr, flush=True)
            results.append({'target': target, 'status': 'failed', 'reason': str(error)})
    write_text_atomic(CACHE / 'build-summary.json', json.dumps(results, indent=2) + '\n')
    print(json.dumps(results, indent=2), flush=True)
    return 1 if not available or any(r['status'] == 'failed' or (options.require_all and r['status'] == 'unavailable') for r in results) else 0

def bridge_plan(target, prefix, options, selected=None):
    """Resolve the same target compiler used for FFmpeg without configuring or building anything."""
    if not target.startswith(('win-', 'macos-', 'ios-', 'linux-', 'android-')):
        return None
    if selected is None:
        selected, reason = toolchain(target)
        if reason:
            raise RuntimeError(reason)
    specific, paths = selected
    environment = os.environ.copy()
    environment['PATH'] = os.pathsep.join([str(p) for p in paths] + [environment.get('PATH', '')])

    def executable(name):
        result = shutil.which(name, path=environment['PATH'])
        if not result:
            raise RuntimeError(f'Bridge requires executable {name} for {target}; check the selected FFmpeg toolchain PATH')
        return str(Path(result).resolve()).replace('\\', '/')

    cmake = executable('cmake')
    settings = []
    if target.startswith('win-'):
        flags = dict(arg[2:].split('=', 1) for arg in specific if arg.startswith('--') and '=' in arg)
        cross = flags.get('cross-prefix', '')
        cc = executable(flags.get('cc', cross + 'gcc'))
        cxx = executable(flags.get('cxx', cross + 'g++'))
        settings += ['-DCMAKE_SYSTEM_NAME=Windows', '-DCMAKE_C_COMPILER=' + cc, '-DCMAKE_CXX_COMPILER=' + cxx,
                     '-DCMAKE_AR=' + executable(flags.get('ar', cross + 'ar')),
                     '-DCMAKE_RANLIB=' + executable(flags.get('ranlib', cross + 'ranlib')),
                     '-DCMAKE_RC_COMPILER=' + executable(flags.get('windres', cross + 'windres'))]
    elif target.startswith('android-'):
        ndk = find_ndk()
        if not ndk:
            raise RuntimeError('Android bridge requires an Android NDK; set ANDROID_NDK_HOME')
        android_toolchain = ndk / 'build/cmake/android.toolchain.cmake'
        if not android_toolchain.is_file():
            raise RuntimeError('Android NDK is missing its CMake toolchain: ' + str(android_toolchain))
        settings += ['-DCMAKE_TOOLCHAIN_FILE=' + str(android_toolchain),
                     '-DANDROID_ABI=' + ('arm64-v8a' if target == 'android-arm64' else 'armeabi-v7a'),
                     '-DANDROID_PLATFORM=android-' + os.environ.get('ANDROID_API', '23'),
                     '-DANDROID_STL=c++_static', '-DAVUTIL_INCLUDE_DIR=' + str(prefix / 'include'),
                     '-DAVUTIL_LIBRARY=' + str(prefix / 'lib/libavutil.so'),
                     '-DAVCODEC_LIBRARY=' + str(prefix / 'lib/libavcodec.so')]
        if HOST == 'Windows':
            # CMake may shorten clang++.exe to an 8.3 filename without "++".
            # Keep the C++ linker driver mode independent of argv[0].
            settings += ['-DCMAKE_CXX_FLAGS=--driver-mode=g++']
    elif target.startswith('linux-'):
        flags = dict(arg[2:].split('=', 1) for arg in specific if arg.startswith('--') and '=' in arg)
        cross = flags.get('cross-prefix', '')
        cc = flags.get('cc', cross + 'gcc')
        cxx = os.environ.get('CXX', cross + 'g++' if cross else ('clang++' if 'clang' in Path(cc).name else 'c++'))
        settings += ['-DCMAKE_SYSTEM_NAME=Linux', '-DCMAKE_C_COMPILER=' + executable(cc),
                     '-DCMAKE_CXX_COMPILER=' + executable(cxx)]
        if flags.get('sysroot'):
            settings += ['-DCMAKE_SYSROOT=' + flags['sysroot']]

    generator = os.environ.get('FFMPEG_BRIDGE_GENERATOR')
    if not generator:
        if shutil.which('ninja', path=environment['PATH']):
            generator = 'Ninja'
        elif HOST == 'Windows' and shutil.which('mingw32-make', path=environment['PATH']):
            generator = 'MinGW Makefiles'
        elif HOST != 'Windows' and shutil.which('make', path=environment['PATH']):
            generator = 'Unix Makefiles'
        else:
            raise RuntimeError('Bridge needs Ninja, or GNU make (mingw32-make on Windows); no supported CMake generator tool found')
    build_tools = {'Ninja':'ninja', 'MinGW Makefiles':'mingw32-make', 'Unix Makefiles':'make'}
    if generator not in build_tools:
        raise RuntimeError('FFMPEG_BRIDGE_GENERATOR must be Ninja, MinGW Makefiles or Unix Makefiles; the one-click bridge reuses FFmpeg MinGW/LLVM rather than MSVC')
    if HOST == 'Windows' and generator == 'Unix Makefiles':
        raise RuntimeError('Use Ninja or MinGW Makefiles for a native Windows bridge build')
    settings += ['-G', generator, '-DCMAKE_MAKE_PROGRAM=' + executable(build_tools[generator])]
    # CMake cannot safely switch compiler/generator in an existing build tree.
    identity = hashlib.sha256(json.dumps({'settings': settings, 'ffmpegRoot': str(prefix.resolve()),
        'vulkanHeaders': str(DEPENDENCIES.vulkan_headers()) if target.startswith(('win-', 'linux-', 'android-')) else None,
        'd3d12Headers': os.environ.get('FFMPEG_D3D12_HEADERS') if target.startswith('win-') else None},
        sort_keys=True).encode()).hexdigest()[:12]
    build = CACHE / ('bridge-' + target + '-' + identity)
    args = [cmake, '-S', str(HERE / 'Native'), '-B', str(build),
            '-DCMAKE_BUILD_TYPE=Release', '-DFFMPEG_ROOT=' + str(prefix)]
    if target.startswith(('win-', 'linux-', 'android-')):
        args += ['-DFFU_VULKAN_HEADERS=' + str(DEPENDENCIES.vulkan_headers())]
    if target.startswith('win-') and os.environ.get('FFMPEG_D3D12_HEADERS'):
        args += ['-DFFU_D3D12_HEADERS=' + str(DEPENDENCIES.verify_d3d12_overlay())]
    args += settings
    if options.unity_plugin_api:
        args += ['-DUNITY_PLUGIN_API=' + options.unity_plugin_api]
    if target.startswith('macos-'):
        args += ['-DCMAKE_OSX_ARCHITECTURES=' + ('arm64' if target.endswith('arm64') else 'x86_64'),
                 '-DCMAKE_OSX_DEPLOYMENT_TARGET=' + os.environ.get('MACOS_MIN', '11.0')]
    elif target.startswith('ios-'):
        args += ['-DCMAKE_SYSTEM_NAME=iOS', '-DCMAKE_OSX_ARCHITECTURES=' + ('x86_64' if target.endswith('x64') else 'arm64'),
                 '-DCMAKE_OSX_SYSROOT=' + ('iphonesimulator' if 'simulator' in target else 'iphoneos'),
                 '-DCMAKE_OSX_DEPLOYMENT_TARGET=' + os.environ.get('IOS_MIN', '15.0'),
                 # iOS CMake root-path search otherwise prepends the SDK to this host prefix.
                 '-DAVUTIL_INCLUDE_DIR=' + str(prefix / 'include'),
                 '-DAVUTIL_LIBRARY=' + str(prefix / 'lib/libavutil.a')]
    return {'args':args, 'environment':environment, 'build':build, 'cmake':cmake}

def build_bridge(target, prefix, options, selected=None):
    plan = bridge_plan(target, prefix, options, selected)
    if plan is None:
        print(f'Bridge has no hardware backend for {target}; software texture upload remains available.')
        return
    args, build = plan['args'], plan['build']
    subprocess.run(args, env=plan['environment'], check=True)
    subprocess.run([plan['cmake'], '--build', str(build), '--config', 'Release', '--parallel', str(options.jobs)],
                   env=plan['environment'], check=True)
    destination = destination_for(target)
    files = []
    for pattern in ['FFmpegUnityBridge.dll', 'libFFmpegUnityBridge.dylib', 'libFFmpegUnityBridge.a', 'libFFmpegUnityBridge.so']:
        for binary in build.rglob(pattern):
            shutil.copy2(binary, destination / binary.name)
            if 'simulator' not in target:
                write_meta(destination / binary.name, target)
            files.append({'file':binary.name, 'sha256':hashlib.sha256(binary.read_bytes()).hexdigest(), 'bytes':binary.stat().st_size})
    if not files:
        raise RuntimeError('CMake produced no FFmpegUnityBridge library for ' + target)
    sources = {str(source.relative_to(HERE / 'Native')).replace('\\', '/'):hashlib.sha256(source.read_bytes()).hexdigest()
               for source in (HERE / 'Native').rglob('*') if source.is_file() and source.suffix in ('.cpp', '.h', '.mm', '.txt')}
    write_text_atomic(destination / 'bridge-manifest.json', json.dumps({'target':target,
        'bridgeAbi': 2 if target.startswith(('macos-', 'ios-')) else 4, 'host':platform.platform(),
        'builtUtc':datetime.datetime.now(datetime.timezone.utc).isoformat(), 'cmake':args, 'files':files,
        'sourceSha256':sources}, indent=2) + '\n')

if __name__ == '__main__':
    sys.exit(main())
