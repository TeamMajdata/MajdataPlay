cmake_minimum_required(VERSION 3.20)
project(MajdataPlayVulkanPacing LANGUAGES CXX)

set(UNITY_PLUGIN_API "${CMAKE_CURRENT_SOURCE_DIR}/Unity" CACHE PATH "Unity native plug-in headers")
# Reuse the repository's pinned Vulkan-Headers copy instead of vendoring a second one.
set(MAJVK_VULKAN_HEADERS "${CMAKE_CURRENT_SOURCE_DIR}/../../FFmpeg/Native/ThirdParty/Vulkan-Headers/include" CACHE PATH "Vulkan headers")
option(MAJVK_BUILD_TESTS "Build the deterministic layer tests" OFF)

function(majvk_configure target)
  target_compile_features(${target} PRIVATE cxx_std_17)
  target_include_directories(${target} PRIVATE "${CMAKE_CURRENT_SOURCE_DIR}/src" "${UNITY_PLUGIN_API}" "${MAJVK_VULKAN_HEADERS}")
  if(MINGW)
    # Statically include the compiler's C++/thread runtimes; the layer ships as a single DLL.
    target_link_options(${target} PRIVATE -static)
  endif()
  if(CMAKE_SYSTEM_NAME STREQUAL "Linux")
    find_package(Threads REQUIRED)
    target_link_libraries(${target} PRIVATE Threads::Threads)
  endif()
endfunction()

add_library(MajdataPlayVulkanPacing SHARED src/VulkanPacing.cpp)
majvk_configure(MajdataPlayVulkanPacing)
set_target_properties(MajdataPlayVulkanPacing PROPERTIES CXX_VISIBILITY_PRESET hidden VISIBILITY_INLINES_HIDDEN YES)
if(WIN32)
  # Unity's Windows plug-in importer expects no Unix "lib" prefix.
  set_target_properties(MajdataPlayVulkanPacing PROPERTIES PREFIX "")
  if(MINGW AND CMAKE_SIZEOF_VOID_P EQUAL 4)
    # Unity looks up the undecorated stdcall plug-in entry points on Win32.
    target_sources(MajdataPlayVulkanPacing PRIVATE src/ExportsWin32MinGW.def)
  endif()
endif()

if(MAJVK_BUILD_TESTS)
  add_executable(MajdataPlayVulkanPacingTests tests/VulkanPacingTests.cpp src/VulkanPacing.cpp)
  majvk_configure(MajdataPlayVulkanPacingTests)
endif()
