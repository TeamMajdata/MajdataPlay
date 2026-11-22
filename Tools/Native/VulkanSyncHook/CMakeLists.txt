cmake_minimum_required(VERSION 3.20)
project(MajdataPlayVulkanSyncHook LANGUAGES CXX)

set(UNITY_PLUGIN_API "${CMAKE_CURRENT_SOURCE_DIR}/Unity" CACHE PATH "Unity native plug-in headers")
# Reuse the repository's pinned Vulkan-Headers copy instead of vendoring a second one.
set(MAJVK_VULKAN_HEADERS "${CMAKE_CURRENT_SOURCE_DIR}/../../FFmpeg/Native/ThirdParty/Vulkan-Headers/include" CACHE PATH "Vulkan headers")
option(MAJVK_BUILD_TESTS "Build the deterministic hook tests" OFF)

function(majvk_configure target)
  target_compile_features(${target} PRIVATE cxx_std_17)
  target_include_directories(${target} PRIVATE "${CMAKE_CURRENT_SOURCE_DIR}/src" "${UNITY_PLUGIN_API}" "${MAJVK_VULKAN_HEADERS}")
  if(MSVC)
    target_compile_options(${target} PRIVATE /W4 /WX /EHsc /utf-8)
    set_property(TARGET ${target} PROPERTY MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")
  else()
    target_compile_options(${target} PRIVATE -Wall -Wextra -Werror)
  endif()
  if(MINGW)
    target_link_options(${target} PRIVATE -static -Wl,--no-insert-timestamp)
  endif()
endfunction()

# The hook wraps Vulkan entry points through Unity's IUnityGraphicsVulkanV2 interception API,
# so it currently targets Windows x64 only.
add_library(MajdataPlayVulkanSyncHook SHARED src/VulkanSyncHook.cpp)
majvk_configure(MajdataPlayVulkanSyncHook)
set_target_properties(MajdataPlayVulkanSyncHook PROPERTIES CXX_VISIBILITY_PRESET hidden VISIBILITY_INLINES_HIDDEN YES)
if(WIN32)
  # Unity's Windows plug-in importer expects no Unix "lib" prefix.
  set_target_properties(MajdataPlayVulkanSyncHook PROPERTIES PREFIX "")
endif()

if(MAJVK_BUILD_TESTS)
  add_executable(MajdataPlayVulkanSyncHookTests tests/VulkanSyncHookTests.cpp)
  majvk_configure(MajdataPlayVulkanSyncHookTests)
endif()
