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
X265_PARAMETER_CHECK_PATCH = HERE / 'patches/x265-parameter-check.patch'

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

def reviewed_source_patches():
    """Record both wrapper checks without changing the public FFmpeg ABI."""
    digest = hashlib.sha256(X265_PARAMETER_CHECK_PATCH.read_bytes()).hexdigest()
    return [amf_rate_control_patch(),
            {'file': X265_PARAMETER_CHECK_PATCH.relative_to(HERE).as_posix(), 'sha256': digest,
             'versionMarker': 'MajdataPlay-X265-Params-v1-' + digest[:12]}]

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
    patches = reviewed_source_patches()
    if not source_git('diff', '--cached', '--quiet', '--no-ext-diff', 'HEAD', '--', quiet=True):
        raise RuntimeError('FFmpeg source index contains tracked modifications; refusing an unrecorded build')
    # A temporary index describes the exact reviewed tree. Comparing every tracked
    # working-tree file to it also catches edits outside the patch and partial patches.
    with tempfile.TemporaryDirectory(prefix='ffmpeg-patch-index-', dir=CACHE) as directory:
        environment = os.environ.copy()
        environment['GIT_INDEX_FILE'] = str(Path(directory) / 'index')
        if not source_git('read-tree', 'HEAD', environment=environment):
            raise RuntimeError('Cannot prepare the pinned FFmpeg patch verification index')
        files = [HERE / patch['file'] for patch in patches]
        matched = 0 if source_git('diff', '--quiet', '--no-ext-diff', '--', environment=environment, quiet=True) else None
        for index, file in enumerate(files):
            if not source_git('apply', '--cached', '--whitespace=error-all', file, environment=environment):
                raise RuntimeError('A reviewed wrapper patch does not apply to the pinned FFmpeg commit: ' + str(file))
            if source_git('diff', '--quiet', '--no-ext-diff', '--', environment=environment, quiet=True):
                matched = index + 1
        # A previous AMF-only build is an authenticated prefix of the reviewed
        # patches. Partial patches or unrelated edits never match an allowed tree.
        if matched is None:
            raise RuntimeError('FFmpeg source contains edits outside a complete reviewed patch prefix')
        if matched < len(files):
            if not source_git('apply', '--check', '--whitespace=error-all', *files[matched:]):
                raise RuntimeError('Cannot apply the remaining reviewed wrapper patches')
            if not source_git('apply', '--whitespace=error-all', *files[matched:]):
                raise RuntimeError('Applying the reviewed wrapper patches failed')
            if not source_git('diff', '--quiet', '--no-ext-diff', '--', environment=environment, quiet=True):
                raise RuntimeError('FFmpeg source differs from the reviewed patched tree')
    for patch in patches:
        print('Verified source patch ' + patch['file'] + ' SHA256 ' + patch['sha256'] + '.', flush=True)
    return patches

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
    for name in ['GPL', 'GPLV3', 'VERSION3', 'LIBX264', 'LIBX265', 'LIBVPX', 'LIBAOM']:
        if not re.search(r'^#define CONFIG_' + name + r' 1$', configuration, re.MULTILINE):
            raise RuntimeError('Software encoder build omitted required configuration ' + name)
    forbidden = ['LIBVPX_VP8_DECODER', 'LIBVPX_VP9_DECODER', 'LIBAOM_AV1_DECODER',
                 'AHX_PARSER', 'AHX_TO_MP2_BSF']
    forbidden += [name.upper() + '_DECODER' for name in gpl_only_decoders()]
    enabled = [name for name in forbidden if re.search(r'^#define CONFIG_' + name + r' 1$', configuration, re.MULTILINE)]
    if enabled:
        raise RuntimeError('Software encoder build changed the pinned decoder profile: ' + ', '.join(enabled))


def gpl_only_decoders():
    """Identify default decoder additions gated by FFmpeg's internal lgpl_gpl feature."""
    return ['adpcm_circus', 'adpcm_ima_escape', 'adpcm_ima_hvqm2', 'adpcm_ima_hvqm4',
            'adpcm_ima_magix', 'adpcm_ima_pda', 'adpcm_n64', 'adpcm_psxc', 'ahx']


def encoder_toolchain(target, specific, paths):
    """Resolve the same ABI, compiler arguments, SDK and archiver used by FFmpeg."""
    environment = dav1d_tools(paths)
    cmake = os.environ.get('FFMPEG_ENCODER_CMAKE', 'cmake')
    for name in [cmake, 'ninja']:
        if not shutil.which(name, path=environment['PATH']):
            raise RuntimeError('Software encoding requires build tool ' + name)
    flags = dict(arg[2:].split('=', 1) for arg in specific if arg.startswith('--') and '=' in arg)
    cross = flags.get('cross-prefix', '')
    cc = shlex.split(flags.get('cc', cross + 'gcc'))
    if target.startswith(('macos-', 'ios-')):
        default_cxx = str(Path(cc[0]).with_name('clang++'))
    else:
        default_cxx = cross + 'g++' if cross else ('clang++' if 'clang' in Path(cc[0]).name else 'c++')
    cxx = shlex.split(flags.get('cxx', os.environ.get('CXX', default_cxx)))
    def executable(value):
        native = re.sub(r'^/([a-zA-Z])/', lambda match: match.group(1) + ':/', value) if HOST == 'Windows' else value
        found = shutil.which(native, path=environment['PATH'])
        if not found:
            raise RuntimeError('Missing target build executable ' + value)
        # LLVM target-driver and ranlib aliases use argv[0] to choose the target
        # or operation. Keep their symlink names while making the path absolute.
        return str(Path(found).absolute())
    cc[0], cxx[0] = executable(cc[0]), executable(cxx[0])
    ar = executable(flags.get('ar', cross + 'ar'))
    ranlib = executable(flags.get('ranlib', cross + 'ranlib'))
    cflags, ldflags = [], []
    for arg in specific:
        if arg.startswith('--extra-cflags='):
            cflags += shlex.split(arg.split('=', 1)[1])
        elif arg.startswith('--extra-ldflags='):
            ldflags += shlex.split(arg.split('=', 1)[1])
    if flags.get('sysroot'):
        cflags += ['--sysroot=' + flags['sysroot']]
        ldflags += ['--sysroot=' + flags['sysroot']]
    if HOST == 'Windows':
        def native(value):
            return re.sub(r'(^|=|-I|-L)/([a-zA-Z])/', lambda match: match.group(1) + match.group(2) + ':/', value)
        cflags, ldflags = [native(v) for v in cflags], [native(v) for v in ldflags]
    family = 'x86' if target == 'win-x86' else 'arm' if target == 'android-armv7' else 'x86_64' if target.endswith('-x64') else 'arm64'
    nasm = shutil.which('nasm', path=environment['PATH'])
    cmake = executable(cmake)
    cmake_version = command([cmake, '--version']).splitlines()[0]
    if not re.search(r'^cmake version 3\.', cmake_version):
        raise RuntimeError('Pinned x265 4.1 requires CMake 3.x; select a portable CMake 3 executable with FFMPEG_ENCODER_CMAKE')
    return {'environment': environment, 'flags': flags, 'cc': cc, 'cxx': cxx, 'ar': ar, 'ranlib': ranlib,
            'cflags': cflags, 'ldflags': ldflags, 'family': family, 'nasm': nasm,
            'cmake': cmake, 'cmakeVersion': cmake_version, 'ninja': executable('ninja')}


def encoder_cmake_settings(target, tools):
    """Generate a native-host CMake invocation that never runs target executables."""
    flags = tools['flags']
    args = ['-G', 'Ninja', '-DCMAKE_MAKE_PROGRAM=' + tools['ninja'], '-DCMAKE_BUILD_TYPE=Release',
            '-DCMAKE_POLICY_VERSION_MINIMUM=3.5', '-DCMAKE_POSITION_INDEPENDENT_CODE=ON',
            '-DCMAKE_INSTALL_LIBDIR=lib', '-DCMAKE_TRY_COMPILE_TARGET_TYPE=STATIC_LIBRARY',
            '-DCMAKE_SYSTEM_PROCESSOR=' + ('armv7l' if tools['family'] == 'arm' else tools['family'])]
    if target.startswith('android-'):
        args += ['-DCMAKE_TOOLCHAIN_FILE=' + str(find_ndk() / 'build/cmake/android.toolchain.cmake'),
                 '-DANDROID_ABI=' + ('arm64-v8a' if target.endswith('arm64') else 'armeabi-v7a'),
                 '-DANDROID_PLATFORM=android-' + os.environ.get('ANDROID_API', '23'), '-DANDROID_STL=c++_static']
    else:
        system = 'Windows' if target.startswith('win-') else 'iOS' if target.startswith('ios-') else 'Darwin' if target.startswith('macos-') else 'Linux'
        args += ['-DCMAKE_SYSTEM_NAME=' + system, '-DCMAKE_C_COMPILER=' + tools['cc'][0],
                 '-DCMAKE_CXX_COMPILER=' + tools['cxx'][0], '-DCMAKE_AR=' + tools['ar'],
                 '-DCMAKE_RANLIB=' + tools['ranlib']]
        if flags.get('sysroot') and not target.startswith(('macos-', 'ios-')):
            args += ['-DCMAKE_SYSROOT=' + flags['sysroot']]
        if target.startswith('win-'):
            args += ['-DCMAKE_RC_COMPILER=' + flags.get('windres', flags.get('cross-prefix', '') + 'windres')]
    if target.startswith(('macos-', 'ios-')):
        args += ['-DCMAKE_OSX_SYSROOT=' + flags['sysroot'],
                 '-DCMAKE_OSX_ARCHITECTURES=' + ('x86_64' if target.endswith('x64') else 'arm64'),
                 '-DCMAKE_OSX_DEPLOYMENT_TARGET=' + (os.environ.get('IOS_MIN', '15.0') if target.startswith('ios-') else os.environ.get('MACOS_MIN', '11.0'))]
    cxx_flags = tools['cxx'][1:] + tools['cflags']
    if HOST == 'Windows' and 'clang' in Path(tools['cxx'][0]).name:
        cxx_flags += ['--driver-mode=g++']
    args += ['-DCMAKE_C_FLAGS=' + shlex.join(tools['cc'][1:] + tools['cflags']),
             '-DCMAKE_CXX_FLAGS=' + shlex.join(cxx_flags),
             '-DCMAKE_EXE_LINKER_FLAGS=' + shlex.join(tools['ldflags']),
             '-DCMAKE_SHARED_LINKER_FLAGS=' + shlex.join(tools['ldflags'])]
    return args


def x265_runtime_libraries(target, tools):
    """Link C++ statically on Windows/Android/Linux and use the Apple system runtime."""
    if target.startswith(('macos-', 'ios-')):
        return ['-lc++', '-lm']
    def archive(name):
        file = subprocess.check_output(tools['cxx'] + ['-print-file-name=' + name],
                                       env=tools['environment'], text=True).strip()
        if not Path(file).is_file():
            raise RuntimeError('Missing target static compiler runtime archive ' + name)
        resolved = Path(file).resolve()
        # FFmpeg's link probe partitions positional paths before its test object.
        # Keep archives in the library partition while selecting their exact names.
        return ['-L' + posix(resolved.parent), '-l:' + resolved.name]
    if target.startswith('android-'):
        return archive('libc++_static.a') + archive('libc++abi.a') + archive('libunwind.a') + ['-ldl', '-lm']
    result = subprocess.check_output(tools['cxx'] + ['-print-file-name=libc++.a'],
                                     env=tools['environment'], text=True).strip()
    libraries = archive('libc++.a') + archive('libc++abi.a') + archive('libunwind.a') if Path(result).is_file() else archive('libstdc++.a')
    # PE linkers may prefer DLL import archives even inside -Bstatic regions.
    # Explicit archive paths also preserve closure after FFmpeg flattens library flags.
    if target.startswith('win-'):
        libraries += archive('libwinpthread.a')
    return libraries + ['-lm'] + ([] if target.startswith('win-') else ['-ldl', '-pthread'])


def build_software_encoders(target, specific, paths, jobs, bash, dav1d_config):
    """Build authenticated static PIC encoders and return usable prefixes and provenance."""
    tools = encoder_toolchain(target, specific, paths)
    identity_settings = {'recipe': 1, 'target': target, 'specific': specific,
        'cc': tools['cc'], 'cxx': tools['cxx'], 'ar': tools['ar'], 'nasm': tools['nasm'],
        'cmake': tools['cmake'], 'cmakeVersion': tools['cmakeVersion'],
        'sources': DEPENDENCIES.LOCK['softwareEncoders']}
    if not tools['family'].startswith('x86'):
        identity_settings['x264AssemblyFlags'] = tools['cc'][1:] + tools['cflags']
    if target == 'ios-simulator-x64':
        identity_settings['aomAppleSimulatorPlatform'] = 'Darwin-iOS-SDK-macho64-v1'
    identity = hashlib.sha256(json.dumps(identity_settings, sort_keys=True).encode()).hexdigest()[:12]
    root = CACHE / ('software-' + target + '-' + identity)
    root.mkdir(parents=True, exist_ok=True)
    prefixes, archives, reports = {}, [], {}
    runtime = x265_runtime_libraries(target, tools)
    cflags = tools['cc'][1:] + tools['cflags']
    ldflags = tools['cc'][1:] + tools['ldflags']
    def shell_flags(values):
        return shlex.join([posix(value) if Path(value).is_file() else value for value in values])
    exports = {'CC': shell_flags(tools['cc']), 'CXX': shell_flags(tools['cxx']),
               'AR': posix(tools['ar']), 'RANLIB': posix(tools['ranlib']),
               'CFLAGS': shell_flags(cflags), 'CXXFLAGS': shell_flags(tools['cxx'][1:] + tools['cflags']),
               'LDFLAGS': shell_flags(ldflags)}
    if tools['nasm'] and tools['family'].startswith('x86'):
        exports['AS'] = posix(tools['nasm'])
    elif not tools['family'].startswith('x86'):
        # x264 does not inherit CFLAGS when invoking the integrated ARM assembler.
        # Carry the exact target/SDK/minimum OS so iOS assembly is never tagged macOS.
        exports['AS'] = shell_flags(tools['cc'])
        exports['ASFLAGS'] = shell_flags(cflags)
    setup = 'export PATH=' + shlex.quote(':'.join(posix(p) for p in paths) + ':/usr/bin') + ':"$PATH"; '
    setup += ''.join('export ' + name + '=' + shlex.quote(value) + '; ' for name, value in exports.items())
    make = os.environ.get('FFMPEG_MAKE', 'make')
    for name in DEPENDENCIES.LOCK['softwareEncoders']:
        DEPENDENCIES.verify_encoder_source(name)
        source = DEPENDENCIES.encoder_source(name)
        work = root / name
        build, prefix = work / 'build', work / 'install'
        build.mkdir(parents=True, exist_ok=True)
        prefix.mkdir(parents=True, exist_ok=True)
        print('BUILD ' + target + ' software encoder ' + name + '; logs: ' + str(work), flush=True)
        if name in ('x264', 'libvpx'):
            if name == 'x264':
                host = 'i686-w64-mingw32' if target == 'win-x86' else 'x86_64-w64-mingw32' if target == 'win-x64' else \
                    'arm-linux-androideabi' if target == 'android-armv7' else 'aarch64-linux-android' if target == 'android-arm64' else \
                    ('x86_64' if target.endswith('x64') else 'aarch64') + ('-apple-darwin' if target.startswith(('macos-', 'ios-')) else '-linux-gnu')
                options = ['--prefix=' + posix(prefix), '--host=' + host, '--enable-static', '--enable-pic',
                           '--disable-cli', '--disable-opencl', '--disable-lavf', '--disable-swscale',
                           '--disable-ffms', '--disable-gpac', '--disable-lsmash', '--bit-depth=8', '--chroma-format=420']
                if tools['family'].startswith('x86') and not tools['nasm']:
                    options += ['--disable-asm']
                archive_name = 'libx264.a'
            else:
                platform_name = 'x86-win32-gcc' if target == 'win-x86' else 'x86_64-win64-gcc' if target == 'win-x64' else \
                    'x86_64-linux-gcc' if target == 'linux-x64' else 'x86_64-darwin25-gcc' if target == 'macos-x64' else \
                    'x86_64-iphonesimulator-gcc' if target == 'ios-simulator-x64' else 'generic-gnu'
                options = ['--prefix=' + posix(prefix), '--target=' + platform_name, '--enable-pic',
                           '--enable-static', '--disable-shared', '--disable-examples', '--disable-tools',
                           '--disable-docs', '--disable-unit-tests', '--disable-install-bins',
                           '--disable-vp8', '--enable-vp9', '--enable-vp9-encoder', '--disable-vp9-decoder']
                if platform_name == 'generic-gnu' or not tools['nasm']:
                    options += ['--disable-runtime-cpu-detect']
                if tools['family'].startswith('x86') and tools['nasm']:
                    options += ['--as=nasm']
                elif tools['family'].startswith('x86'):
                    options[1] = '--target=generic-gnu'
                archive_name = 'libvpx.a'
            configure = setup + 'bash ' + shlex.quote(posix(source / 'configure')) + ' ' + shlex.join(options)
            shell(bash, configure, build, work / 'configure.log')
            shell(bash, setup + shlex.quote(make) + f' -j{max(1, jobs)}', build, work / 'build.log')
            shell(bash, setup + shlex.quote(make) + ' install', build, work / 'install.log')
            commands = [options]
        else:
            options = encoder_cmake_settings(target, tools) + ['-DCMAKE_INSTALL_PREFIX=' + str(prefix)]
            if name == 'x265':
                source = source / 'source'
                assembly = tools['family'].startswith('x86') and bool(tools['nasm'])
                options += ['-DENABLE_SHARED=OFF', '-DENABLE_CLI=OFF', '-DENABLE_PIC=ON', '-DENABLE_LIBNUMA=OFF',
                            '-DENABLE_TESTS=OFF', '-DENABLE_HDR10_PLUS=OFF', '-DENABLE_LIBVMAF=OFF',
                            '-DENABLE_SVT_HEVC=OFF', '-DENABLE_ALPHA=OFF', '-DHIGH_BIT_DEPTH=OFF',
                            '-DNATIVE_BUILD=OFF', '-DENABLE_ASSEMBLY=' + ('ON' if assembly else 'OFF'),
                            '-DENABLE_NEON=OFF', '-DENABLE_NEON_DOTPROD=OFF', '-DENABLE_NEON_I8MM=OFF',
                            '-DENABLE_SVE=OFF', '-DENABLE_SVE2=OFF']
                if target == 'android-armv7':
                    # Upstream otherwise chooses -mcpu=native and the Linux hard-float
                    # ABI even in an Android cross build. Its portable ARM cross branch
                    # retains the Android AAPCS calling convention without host probing.
                    options += ['-DCROSS_COMPILE_ARM=ON']
                if assembly:
                    options += ['-DNASM_EXECUTABLE=' + tools['nasm']]
                archive_name = 'libx265.a'
            else:
                cpu = tools['family'] if tools['family'].startswith('x86') and tools['nasm'] else 'generic'
                options += ['-DBUILD_SHARED_LIBS=OFF', '-DCONFIG_PIC=1', '-DCONFIG_AV1_ENCODER=1',
                            '-DCONFIG_AV1_DECODER=0', '-DCONFIG_AV1_HIGHBITDEPTH=1',
                            '-DENABLE_EXAMPLES=OFF', '-DENABLE_TESTS=OFF', '-DENABLE_TOOLS=OFF',
                            '-DENABLE_DOCS=OFF', '-DAOM_TARGET_CPU=' + cpu]
                if target == 'ios-simulator-x64':
                    # Match libaom's official x86_64-ios-simulator toolchain: its
                    # NASM selector recognizes Darwin, while iOS incorrectly selects
                    # ELF. The explicit iOS SDK and clang target retain simulator ABI.
                    options += ['-DCMAKE_SYSTEM_NAME=Darwin', '-DCMAKE_ASM_NASM_OBJECT_FORMAT=macho64']
                if target.startswith('win-'):
                    # Select upstream's _beginthreadex/SRW-lock backend. Static
                    # try-compile probes cannot prove pthread linkage and otherwise
                    # mistake MinGW's pthread headers for functions in the Windows CRT.
                    options += ['-DCMAKE_HAVE_LIBC_PTHREAD=0', '-DCMAKE_HAVE_PTHREADS_CREATE=0',
                                '-DCMAKE_HAVE_PTHREAD_CREATE=0', '-DTHREADS_HAVE_PTHREAD_ARG=0',
                                '-DCMAKE_USE_PTHREADS_INIT=0', '-DCMAKE_THREAD_LIBS_INIT=',
                                '-DCMAKE_USE_WIN32_THREADS_INIT=1']
                elif target.startswith('android-'):
                    # Bionic provides the worker pthread API inside libc. It omits
                    # pthread_cancel, so FindThreads' broad libc test fails, while a
                    # static library probe falsely reports a separate libpthreads.
                    options += ['-DCMAKE_HAVE_LIBC_PTHREAD=1', '-DCMAKE_HAVE_PTHREADS_CREATE=0',
                                '-DCMAKE_HAVE_PTHREAD_CREATE=0', '-DTHREADS_HAVE_PTHREAD_ARG=0',
                                '-DCMAKE_USE_PTHREADS_INIT=1', '-DCMAKE_THREAD_LIBS_INIT=']
                archive_name = 'libaom.a'
            configure = [tools['cmake'], '-S', str(source), '-B', str(build)] + options
            commands = [configure, [tools['cmake'], '--build', str(build), '--parallel', str(max(1, jobs))],
                        [tools['cmake'], '--install', str(build)]]
            for args, log_name in zip(commands, ['configure.log', 'build.log', 'install.log']):
                with (work / log_name).open('w', encoding='utf-8') as output:
                    subprocess.run(args, env=tools['environment'], stdout=output, stderr=subprocess.STDOUT, check=True)
        archive = prefix / 'lib' / archive_name
        if not archive.is_file():
            raise RuntimeError('Missing static encoder archive ' + str(archive))
        if name == 'libaom' and target.startswith('win-'):
            configuration = (build / 'config/aom_config.h').read_text()
            for macro, expected in [('HAVE_PTHREAD_H', 0), ('CONFIG_MULTITHREAD', 1)]:
                if not re.search(r'^#define ' + macro + ' ' + str(expected) + r'$', configuration, re.MULTILINE):
                    raise RuntimeError('libaom did not select the native Windows thread backend: ' + macro)
        if name == 'x265':
            pc = prefix / 'lib/pkgconfig/x265.pc'
            contents = pc.read_text(encoding='utf-8')
            contents = re.sub(r'^Libs\.private:.*$', 'Libs.private: ' + shlex.join(runtime), contents, flags=re.MULTILINE)
            write_text_atomic(pc, contents)
        elif name == 'libvpx' and target.startswith('win-'):
            pc = prefix / 'lib/pkgconfig/vpx.pc'
            contents = pc.read_text(encoding='utf-8')
            pthread_flag = runtime.index('-l:libwinpthread.a')
            pthread_flags = shlex.join(runtime[pthread_flag - 1:pthread_flag + 1])
            contents = re.sub(r'(?<!\S)-lpthread(?!\S)', pthread_flags, contents)
            write_text_atomic(pc, contents)
        DEPENDENCIES.verify_encoder_source(name)
        prefixes[name] = prefix
        archives.append(archive)
        reports[name] = {'source': DEPENDENCIES.LOCK['softwareEncoders'][name], 'configure': commands[0],
                         'archive': archive_name, 'bytes': archive.stat().st_size,
                         'sha256': hashlib.sha256(archive.read_bytes()).hexdigest(), 'static': True, 'pic': True}
        if name in ('x265', 'libaom'):
            reports[name]['cmakeVersion'] = tools['cmakeVersion']
        if name == 'x265':
            reports[name]['cxxRuntimeLink'] = runtime
        write_text_atomic(work / 'build-provenance.json', json.dumps(reports[name], indent=2) + '\n')
    # Only these authenticated prefixes override sysroot resolution. All other
    # queries still use dav1d's wrapper and the caller's original hardware sysroot.
    underlying = next(arg.split('=', 1)[1] for arg in dav1d_config if arg.startswith('--pkg-config='))
    wrapper = root / 'pkg-config.sh'
    write_text_atomic(wrapper, '#!/bin/sh\ncase " $* " in\n'
        '  *" x264 "*|*" x265 "*|*" vpx "*|*" aom "*)\n'
        '    unset PKG_CONFIG_SYSROOT_DIR PKG_CONFIG_LIBDIR\n'
        '    export PKG_CONFIG_PATH=' + shlex.quote(':'.join(posix(p / 'lib/pkgconfig') for p in prefixes.values())) + '\n'
        '    set -- --static "$@"\n    ;;\nesac\nexec ' + underlying + ' "$@"\n')
    configure = ['--enable-libx264', '--enable-libx265', '--enable-libvpx', '--enable-libaom',
                 '--disable-decoder=libvpx_vp8,libvpx_vp9,libaom_av1',
                 '--pkg-config=' + shlex.join(['sh', posix(wrapper)])]
    if target.startswith(('win-', 'linux-', 'android-')):
        configure += ['--extra-ldflags=-Wl,--exclude-libs,ALL']
    if target.startswith(('win-', 'linux-')):
        configure += ['--extra-ldflags=-static-libstdc++']
    return {'prefixes': prefixes, 'archives': archives, 'configure': configure, 'provenance': reports}

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
    source_patch = HERE / patch['file']
    if hashlib.sha256(source_patch.read_bytes()).hexdigest() != patch['sha256']:
        raise RuntimeError('A reviewed source patch changed during the build')
    file = destination / source_patch.name
    shutil.copy2(source_patch, file)
    if OUTPUT in file.parents:
        meta = Path(str(file) + '.meta')
        if not meta.exists():
            guid = uuid.uuid5(uuid.NAMESPACE_URL, 'majdata-ffmpeg/' + file.relative_to(OUTPUT).as_posix()).hex
            write_text_atomic(meta, 'fileFormatVersion: 2\nguid: ' + guid + '\nDefaultImporter:\n'
                             '  externalObjects: {}\n  userData:\n  assetBundleName:\n  assetBundleVariant:\n')
    return dict(patch, stagedFile=file.name)

def stage(target, prefix, config, logs, dav1d_prefix, source_patches, software_build):
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
            # Unity links seven FFmpeg archives. Include every private codec inside
            # avcodec rather than relying on transitive static-library linkage.
            subprocess.run(['xcrun', 'libtool', '-static', '-o', str(file), str(source_file),
                            str(dav1d_prefix / 'lib/libdav1d.a'),
                            *[str(archive) for archive in software_build['archives']]], check=True)
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
    for name in ['COPYING.LGPLv2.1', 'COPYING.GPLv2', 'COPYING.GPLv3', 'LICENSE.md']:
        shutil.copy2(SOURCE / name, destination / name)
        if OUTPUT in destination.parents and not Path(str(destination / name) + '.meta').is_file():
            write_license_meta(destination / name, importer='DefaultImporter')
    report = {'target': target, 'source': LOCK, 'host': platform.platform(), 'builtUtc': datetime.datetime.now(datetime.timezone.utc).isoformat(),
              'configure': config, 'files': produced, 'softwareDecoders': ['libdav1d'],
              'recordingProfile': verify_recording_configuration(target, logs),
              'buildDependencies': DEPENDENCIES.LOCK,
              'license': 'GPL-3.0-or-later',
              'softwareEncoderBuilds': software_build['provenance'],
              'sourcePatches': [stage_source_patch(destination, patch) for patch in source_patches],
              'buildDependencyLicenses': stage_dav1d_license(destination) + stage_software_encoder_licenses(destination) +
                                         stage_compiler_runtime_licenses(destination, target, software_build)}
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


def write_license_meta(file, importer='TextScriptImporter'):
    """Create a paired license asset once while preserving any existing Unity GUID."""
    if OUTPUT not in file.parents:
        return
    meta = Path(str(file) + '.meta')
    if not meta.exists():
        guid = uuid.uuid5(uuid.NAMESPACE_URL, 'majdata-ffmpeg/' + file.relative_to(OUTPUT).as_posix()).hex
        write_text_atomic(meta, 'fileFormatVersion: 2\nguid: ' + guid + '\n' + importer + ':\n'
                         '  externalObjects: {}\n  userData:\n  assetBundleName:\n  assetBundleVariant:\n')


def stage_software_encoder_licenses(destination):
    """Carry source identities, copyright notices, patent grants and codec licenses."""
    reports = []
    for name, spec in DEPENDENCIES.LOCK['softwareEncoders'].items():
        DEPENDENCIES.verify_encoder_source(name)
        root = DEPENDENCIES.encoder_source(name)
        contents = name + ' ' + spec['version'] + '\n' + spec['repository'] + '\nCommit: ' + spec['commit'] + '\n'
        for license_file in spec['licenseFiles']:
            contents += '\n--- ' + license_file + ' ---\n' + (root / license_file).read_text(encoding='utf-8').rstrip() + '\n'
        # x264/x265's assembly abstraction is separately ISC-licensed. Preserve
        # its complete initial permission/copyright block beside the GPL notices.
        assembly_notice = {'x264': 'common/x86/x86inc.asm', 'x265': 'source/common/x86/x86inc.asm'}.get(name)
        if assembly_notice:
            lines = (root / assembly_notice).read_text(encoding='utf-8').splitlines()
            end = next(index for index, line in enumerate(lines) if not line.startswith(';'))
            notice = '\n'.join(lines[:end])
            if 'Permission to use, copy, modify' not in notice or 'IN CONNECTION WITH THE USE OR PERFORMANCE' not in notice:
                raise RuntimeError('Incomplete ISC assembly notice in pinned ' + name + ' source')
            contents += '\n--- ' + assembly_notice + ' license notice ---\n' + notice + '\n'
        file = destination / (name + '.LICENSE.txt')
        write_text_atomic(file, contents)
        write_license_meta(file)
        reports.append({'file': file.name, 'sha256': hashlib.sha256(file.read_bytes()).hexdigest(),
                        'license': spec['license'], 'sourceCommit': spec['commit']})
    return reports


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


def stage_compiler_runtime_licenses(destination, target, software_build):
    """Attribute the selected embedded C++/thread runtimes without shipping extra libraries."""
    if target.startswith(('macos-', 'ios-')):
        return []  # Apple's libc++ remains a system library, not a redistributed archive.
    runtime = software_build['provenance']['x265']['cxxRuntimeLink']
    directories = []
    archives = []
    for flag in runtime:
        if flag.startswith('-L'):
            value = flag[2:]
            if HOST == 'Windows':
                value = re.sub(r'^/([a-zA-Z])/', lambda match: match.group(1) + ':/', value)
            directories.append(Path(value))
        elif flag.startswith('-l:'):
            file = next((directory / flag[3:] for directory in directories if (directory / flag[3:]).is_file()), None)
            if file is None:
                raise RuntimeError('Cannot attribute missing compiler runtime archive ' + flag)
            archives.append({'file': file.name, 'bytes': file.stat().st_size,
                             'sha256': hashlib.sha256(file.read_bytes()).hexdigest()})
        elif not flag.startswith('-') and flag.endswith('.a'):
            # Early Android stages used positional archives; LLD still linked
            # them correctly. Preserve that authenticated build evidence when
            # refreshing only attribution instead of rebuilding validated binaries.
            value = re.sub(r'^/([a-zA-Z])/', lambda match: match.group(1) + ':/', flag) if HOST == 'Windows' else flag
            file = Path(value)
            if not file.is_file():
                raise RuntimeError('Cannot attribute missing compiler runtime archive ' + flag)
            archives.append({'file': file.name, 'bytes': file.stat().st_size,
                             'sha256': hashlib.sha256(file.read_bytes()).hexdigest()})
    if not archives:
        raise RuntimeError('Software encoder provenance does not identify its static compiler runtime archives')
    notices = []
    override = Path(os.environ['FFMPEG_COMPILER_RUNTIME_LICENSE_DIR']) if os.environ.get('FFMPEG_COMPILER_RUNTIME_LICENSE_DIR') else None
    if target.startswith('android-'):
        ndk = find_ndk()
        prebuilt = {'Windows': 'windows-x86_64', 'Linux': 'linux-x86_64', 'Darwin': 'darwin-x86_64'}[HOST]
        notices = [ndk / 'toolchains/llvm/prebuilt' / prebuilt / 'NOTICE',
                   ndk / 'toolchains/llvm/prebuilt' / prebuilt / 'sysroot/NOTICE']
        license_name = 'Apache-2.0 WITH LLVM-exception and Android NDK runtime notices'
    elif any(item['file'] == 'libstdc++.a' for item in archives):
        package = 'gcc-mingw-w64-base' if target.startswith('win-') else 'libstdc++6'
        candidates = ([override / 'GNU-runtime.copyright'] if override else []) + [Path('/usr/share/doc') / package / 'copyright']
        notice = next((file for file in candidates if file.is_file()), None)
        if notice is None:
            raise RuntimeError('Missing selected GNU runtime copyright; set FFMPEG_COMPILER_RUNTIME_LICENSE_DIR with GNU-runtime.copyright')
        if 'GCC Runtime Library Exception' not in notice.read_text(encoding='utf-8'):
            raise RuntimeError('Selected GNU runtime copyright omits its GCC Runtime Library Exception')
        notices.append(notice)
        license_name = 'GPL-3.0-or-later WITH GCC-exception-3.1'
        if target.startswith('win-'):
            candidates = ([override / 'mingw-runtime.copyright'] if override else []) + [Path('/usr/share/doc/mingw-w64-common/copyright')]
            notice = next((file for file in candidates if file.is_file()), None)
            if notice is None:
                raise RuntimeError('Missing selected MinGW runtime copyright; set FFMPEG_COMPILER_RUNTIME_LICENSE_DIR with mingw-runtime.copyright')
            notices.append(notice)
            license_name += ' and mingw-w64 runtime notices'
    else:
        compiler = next(arg.split('=', 1)[1] for arg in software_build['provenance']['x265']['configure']
                        if arg.startswith('-DCMAKE_CXX_COMPILER='))
        root = Path(compiler).resolve().parent.parent
        notices.append(root / 'LICENSE.TXT')
        # llvm-mingw carries the Winpthreads permission text in the public header.
        triple = 'i686-w64-mingw32' if target == 'win-x86' else 'x86_64-w64-mingw32'
        candidates = [root / 'include/pthread.h', root / triple / 'include/pthread.h']
        notices.append(next((file for file in candidates if file.is_file()), candidates[0]))
        license_name = 'Apache-2.0 WITH LLVM-exception and mingw-w64 runtime notices'
    contents = 'Selected static compiler runtimes for ' + target + '\n'
    contents += '\n'.join(item['file'] + ' SHA256 ' + item['sha256'] for item in archives) + '\n'
    sources = []
    for notice in notices:
        if not notice.is_file():
            raise RuntimeError('Missing selected compiler runtime notice: ' + str(notice))
        text = notice.read_text(encoding='utf-8')
        if notice.name == 'pthread.h':
            # Preserve both MinGW's MIT and its derived Lockless BSD permission
            # blocks before the first declaration, without packaging C header code.
            end = 0
            while text[end:].lstrip().startswith('/*'):
                start = end + len(text[end:]) - len(text[end:].lstrip())
                close = text.find('*/', start)
                if close < 0:
                    raise RuntimeError('Unterminated MinGW Winpthreads permission notice')
                end = close + 2
            if not end or 'Permission' not in text[:end]:
                raise RuntimeError('Incomplete MinGW Winpthreads permission notice')
            text = text[:end]
        contents += '\n--- ' + str(notice) + ' ---\n' + text.rstrip() + '\n'
        sources.append({'file': str(notice), 'sha256': hashlib.sha256(notice.read_bytes()).hexdigest()})
    file = destination / 'compiler-runtime.LICENSE.txt'
    write_text_atomic(file, contents)
    write_license_meta(file)
    return [{'file': file.name, 'sha256': hashlib.sha256(file.read_bytes()).hexdigest(), 'license': license_name,
             'runtimeArchives': archives, 'sourceNotices': sources}]


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
    """Enable all mandatory software formats alongside existing hardware candidates."""
    encoders = ['mpeg4', 'libx264', 'libx265', 'libvpx_vp9', 'libaom_av1']
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
    native_names = {'libvpx_vp9': 'libvpx-vp9', 'libaom_av1': 'libaom-av1'}
    return {'encoders': [native_names.get(name, name) for name in encoders], 'muxers': muxers}

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
                encoder_toolchain(target, config[0], config[1])
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
        source_patches = verify_source()
    for target, (specific, paths) in available:
        work = CACHE / target
        prefix = CACHE / 'install' / target
        work.mkdir(parents=True, exist_ok=True)
        prefix.mkdir(parents=True, exist_ok=True)
        config = ['--prefix=' + posix(prefix), '--disable-doc', '--disable-programs', '--disable-debug', '--disable-autodetect',
                  '--disable-encoders', '--disable-muxers', '--disable-filters', '--disable-devices', '--enable-pic',
                  '--enable-gpl', '--enable-version3', '--disable-nonfree',
                  '--extra-version=' + '_'.join(patch['versionMarker'] for patch in source_patches)]
        # GPL permits the four requested software libraries but also enables nine
        # unrelated default decoders and AHX helpers. Preserve the previous matrix.
        config += ['--disable-decoder=' + ','.join(gpl_only_decoders()),
                   '--disable-parser=ahx', '--disable-bsf=ahx_to_mp2']
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
            software_build = build_software_encoders(target, specific, paths, options.jobs, bash, dav1d_config)
            config += software_build['configure']
            settings_file = work / 'build-settings.json'
            settings = json.dumps({'commit':LOCK['commit'], 'configure':config,
                                   'toolPaths':[str(p) for p in paths], 'runtimePathFix':1,
                                   'sourcePatches': source_patches,
                                   'softwareEncoders': software_build['provenance'],
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
                          'sourcePatches': source_patches,
                          'license': 'GPL-3.0-or-later', 'softwareEncoderBuilds': software_build['provenance'],
                          'recordingProfile': recording_profile}
            if target.startswith(('win-', 'linux-', 'android-')):
                provenance['verifiedHardwareBackends'] = verify_hardware_configuration(target, work)
            # Keep the build evidence beside the usable prefix even when a loaded Unity DLL prevents staging.
            write_text_atomic(prefix / 'build-provenance.json', json.dumps(provenance, indent=2) + '\n')
            shutil.copy2(work / 'configure.log', prefix / 'build-configure.txt')
            report = stage(target, prefix, config, work, dav1d_prefix, source_patches, software_build)
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
