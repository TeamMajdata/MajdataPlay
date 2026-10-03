#!/usr/bin/env python3
"""Pinned FFmpeg source builds; no global installations, no silent ABI upgrades."""
from __future__ import annotations
import argparse
import ctypes
import datetime
import hashlib
import json
import os
from pathlib import Path
import platform
import shlex
import shutil
import subprocess
import sys
import uuid

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[1]
CACHE = Path(os.environ.get('FFMPEG_BUILD_ROOT', str(HERE / '.build'))).resolve()
SOURCE = CACHE / 'source'
OUTPUT = ROOT / 'Assets/Plugins/FFmpeg/Native'
LOCK = json.loads((HERE / 'ffmpeg.lock.json').read_text())
TARGETS = ['win-x86', 'win-x64', 'linux-x64', 'android-armv7', 'android-arm64',
           'macos-x64', 'macos-arm64', 'ios-arm64', 'ios-simulator-arm64', 'ios-simulator-x64']
HOST = platform.system()

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
                  'C:/msys64/usr/bin/bash.exe', 'C:/Program Files/Git/usr/bin/bash.exe'] if HOST == 'Windows' else ['bash']
    return next((shutil.which(c) for c in candidates if c and shutil.which(c)), None)

def find_ndk():
    candidates = [Path(p) for p in [os.environ.get('ANDROID_NDK_HOME'), os.environ.get('ANDROID_NDK_ROOT')] if p]
    for root in [CACHE, HERE / '.build']:
        candidates += sorted((root / 'toolchains').glob('android-ndk-*'), reverse=True)
    sdk = Path(os.environ.get('ANDROID_SDK_ROOT', str(Path.home() / 'AppData/Local/Android/Sdk')))
    candidates += sorted((sdk / 'ndk').glob('*'), reverse=True)
    if HOST == 'Windows':
        for base in ['C:/Program Files/Unity Editors', 'C:/Program Files/Unity/Hub/Editor']:
            candidates += sorted(Path(base).glob('*/Editor/Data/PlaybackEngines/AndroidPlayer/NDK'), reverse=True)
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
        candidates += sorted((CACHE / 'toolchains').glob('llvm-mingw-*'), reverse=True)
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
                 '--enable-d3d11va', '--enable-dxva2', '--enable-schannel']
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
    return (args, paths), None

def verify_source():
    if not (SOURCE / '.git').exists():
        SOURCE.parent.mkdir(parents=True, exist_ok=True)
        subprocess.run(['git', '-c', 'core.autocrlf=false', 'clone', '--depth', '1', '--branch', LOCK['tag'], LOCK['repository'], str(SOURCE)], check=True)
    actual = command(['git', '-C', SOURCE, 'rev-parse', 'HEAD'])
    if actual != LOCK['commit']:
        raise RuntimeError(f'FFmpeg source commit mismatch: {actual}; expected {LOCK["commit"]}')
    if command(['git', '-c', 'core.autocrlf=false', '-c', 'core.filemode=false', '-C', SOURCE, 'status', '--porcelain', '--untracked-files=no']):
        raise RuntimeError('FFmpeg source contains tracked modifications; refusing an unrecorded build')
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

def shell(bash, text, cwd, log):
    with log.open('w', encoding='utf-8') as output:
        subprocess.run([bash, '-c', text], cwd=cwd, stdout=output, stderr=subprocess.STDOUT, check=True)

def write_meta(file, target):
    meta = Path(str(file) + '.meta')
    guid = uuid.uuid5(uuid.NAMESPACE_URL, 'majdata-ffmpeg/' + file.relative_to(OUTPUT).as_posix()).hex
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
    meta.write_text(f'''fileFormatVersion: 2
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
''', encoding='utf-8')

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
    (destination / 'dependency-manifest.json').write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8')
    return report


def stage(target, prefix, config, logs):
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
              'configure': config, 'files': produced}
    if target == 'linux-x64':
        report['postProcessing'] = 'patchelf --set-rpath $ORIGIN (verified by --print-rpath)'
        stage_linux_dependencies(destination)
    (destination / 'build-manifest.json').write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8')
    shutil.copy2(logs / 'configure.log', destination / 'configure.txt')
    return report

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
    CACHE.mkdir(parents=True, exist_ok=True)
    results = []
    available = []
    for target in targets:
        try:
            config, reason = toolchain(target)
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
        verify_source()
    for target, (specific, paths) in available:
        work = CACHE / target
        prefix = CACHE / 'install' / target
        work.mkdir(parents=True, exist_ok=True)
        prefix.mkdir(parents=True, exist_ok=True)
        config = ['--prefix=' + posix(prefix), '--disable-doc', '--disable-programs', '--disable-debug', '--disable-autodetect',
                  '--disable-encoders', '--disable-muxers', '--disable-filters', '--disable-devices', '--enable-pic',
                  '--disable-gpl', '--disable-nonfree']
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
            settings_file = work / 'build-settings.json'
            settings = json.dumps({'commit':LOCK['commit'], 'configure':config,
                                   'toolPaths':[str(p) for p in paths], 'runtimePathFix':1}, sort_keys=True)
            if (work / 'ffbuild/config.mak').exists() and (not settings_file.exists() or settings_file.read_text() != settings):
                shell(bash, setup + shlex.quote(make) + ' clean', work, work / 'clean.log')
            shell(bash, setup + 'bash ' + shlex.quote(posix(SOURCE / 'configure')) + ' ' + shlex.join(config), work, work / 'configure.log')
            shell(bash, setup + shlex.quote(make) + f' -j{max(1, options.jobs)}', work, work / 'build.log')
            shell(bash, setup + shlex.quote(make) + ' install', work, work / 'install.log')
            report = stage(target, prefix, config, work)
            settings_file.write_text(settings)
            if options.with_bridge:
                build_bridge(target, prefix, options, (specific, paths))
            results.append({'target': target, 'status': 'built', 'files': report['files']})
            print(f'OK {target}', flush=True)
        except (OSError, subprocess.CalledProcessError, RuntimeError) as error:
            print(f'FAILED {target}: {error}; see {work}', file=sys.stderr, flush=True)
            results.append({'target': target, 'status': 'failed', 'reason': str(error)})
    (CACHE / 'build-summary.json').write_text(json.dumps(results, indent=2) + '\n')
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
    identity = hashlib.sha256(json.dumps(settings).encode()).hexdigest()[:12]
    build = CACHE / ('bridge-' + target + '-' + identity)
    args = [cmake, '-S', str(HERE / 'Native'), '-B', str(build),
            '-DCMAKE_BUILD_TYPE=Release', '-DFFMPEG_ROOT=' + str(prefix)]
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
    (destination / 'bridge-manifest.json').write_text(json.dumps({'target':target, 'host':platform.platform(),
        'builtUtc':datetime.datetime.now(datetime.timezone.utc).isoformat(), 'cmake':args, 'files':files,
        'sourceSha256':sources}, indent=2) + '\n')

if __name__ == '__main__':
    sys.exit(main())
