#!/usr/bin/env python3
"""Validate staged ABI/dependency loading without LD_LIBRARY_PATH."""
import ctypes
import os
from pathlib import Path
import sys

if os.environ.get('LD_LIBRARY_PATH'):
    raise SystemExit('Run this loader check without LD_LIBRARY_PATH')
library = Path(sys.argv[1]).resolve()
bridge = ctypes.CDLL(str(library), mode=os.RTLD_NOW)
assert bridge.ffu_abi_version() == 4
assert bridge.ffu_capabilities() == 0  # Unity has not provided a device.
assert bridge.ffu_initialization_status() == 1
bridge.ffu_vulkan_create.restype = ctypes.c_void_p
assert not bridge.ffu_vulkan_create()
bridge.ffu_vulkan_video_acquire_device.restype = ctypes.c_void_p
assert not bridge.ffu_vulkan_video_acquire_device()
assert bridge.ffu_vulkan_video_status() == 400
bridge.ffu_vulkan_video_prepare.argtypes = [ctypes.c_void_p, ctypes.c_void_p, ctypes.c_void_p]
bridge.ffu_vulkan_video_prepare.restype = ctypes.c_void_p
assert not bridge.ffu_vulkan_video_prepare(None, None, None)
bridge.ffu_vulkan_map_frame.argtypes = [ctypes.c_void_p, ctypes.POINTER(ctypes.c_void_p)]
frame = ctypes.c_void_p()
assert bridge.ffu_vulkan_map_frame(None, ctypes.byref(frame)) == -203
assert bridge.ffu_vulkan_poll() == 0
maps = Path('/proc/self/maps').read_text()
for dependency in ('libavutil.so.61', 'libva.so.2', 'libva-drm.so.2', 'libdrm.so.2'):
    assert str(library.parent / dependency) in maps, f'{dependency} did not load beside the bridge'
print('PASS: Linux bridge ABI 4, Vulkan Video missing-device/invalid-frame guards, empty retirement, staged FFmpeg/VAAPI dependencies without LD_LIBRARY_PATH')
