cmake_minimum_required(VERSION 3.20)
project(MajdataPlayTextureUpload LANGUAGES CXX)

set(UNITY_PLUGIN_API "${CMAKE_CURRENT_SOURCE_DIR}/Unity" CACHE PATH "Unity native plug-in headers")
# Reuse the repository's pinned Vulkan-Headers copy instead of vendoring a second one. The plug-in defines
# VK_NO_PROTOTYPES and resolves every entry point through Unity's device, so no Vulkan library is linked.
set(MAJVKU_VULKAN_HEADERS "${CMAKE_CURRENT_SOURCE_DIR}/../../FFmpeg/Native/ThirdParty/Vulkan-Headers/include" CACHE PATH "Vulkan headers")
option(MAJVKU_BUILD_TESTS "Build the deterministic staging ring tests" OFF)

function(majvku_configure target)
  target_compile_features(${target} PRIVATE cxx_std_17)
  target_include_directories(${target} PRIVATE "${CMAKE_CURRENT_SOURCE_DIR}/src" "${UNITY_PLUGIN_API}" "${MAJVKU_VULKAN_HEADERS}")
  if(MSVC)
    target_compile_options(${target} PRIVATE /W4 /permissive- /EHsc /utf-8)
  endif()
  if(MINGW)
    # Statically include the compiler's C++/thread runtimes; the plug-in ships as a single DLL.
    target_link_options(${target} PRIVATE -static -Wl,--no-insert-timestamp)
  endif()
  if(CMAKE_SYSTEM_NAME STREQUAL "Linux")
    find_package(Threads REQUIRED)
    target_link_libraries(${target} PRIVATE Threads::Threads)
  endif()
endfunction()

add_library(MajdataPlayTextureUpload SHARED src/TextureUpload.cpp)
majvku_configure(MajdataPlayTextureUpload)
set_target_properties(MajdataPlayTextureUpload PROPERTIES CXX_VISIBILITY_PRESET hidden VISIBILITY_INLINES_HIDDEN YES)
# cl.exe localizes the /showIncludes lines CMake turns into Ninja depfiles, so a non-English toolchain loses
# header dependencies. These explicit dependencies rebuild the plug-in and its tests on every header edit.
set_source_files_properties(src/TextureUpload.cpp PROPERTIES OBJECT_DEPENDS "${CMAKE_CURRENT_SOURCE_DIR}/src/StagingRing.h;${CMAKE_CURRENT_SOURCE_DIR}/src/TextureUpload.h")
set_source_files_properties(tests/StagingRingTests.cpp PROPERTIES OBJECT_DEPENDS "${CMAKE_CURRENT_SOURCE_DIR}/src/StagingRing.h")
if(WIN32)
  # Unity's Windows plug-in importer expects no Unix "lib" prefix.
  set_target_properties(MajdataPlayTextureUpload PROPERTIES PREFIX "")
  if(MINGW AND CMAKE_SIZEOF_VOID_P EQUAL 4)
    # Unity looks up the undecorated stdcall plug-in entry points on Win32.
    target_sources(MajdataPlayTextureUpload PRIVATE src/ExportsWin32MinGW.def)
  endif()
endif()

if(MAJVKU_BUILD_TESTS)
  add_executable(MajdataPlayTextureUploadTests tests/StagingRingTests.cpp)
  majvku_configure(MajdataPlayTextureUploadTests)
endif()
