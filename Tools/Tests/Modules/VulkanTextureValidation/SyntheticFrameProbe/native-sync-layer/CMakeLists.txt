cmake_minimum_required(VERSION 3.20)
project(MajdataPlaySyncLayer LANGUAGES CXX)
if(NOT WIN32 OR NOT CMAKE_SIZEOF_VOID_P EQUAL 8)
    message(FATAL_ERROR "This diagnostic Vulkan layer supports Windows x64 only.")
endif()
get_filename_component(REPOSITORY "${CMAKE_CURRENT_SOURCE_DIR}/../../../../../.." ABSOLUTE)
set(MAJ_VULKAN_INCLUDE "${REPOSITORY}/Tools/FFmpeg/Native/ThirdParty/Vulkan-Headers/include")
if(NOT EXISTS "${MAJ_VULKAN_INCLUDE}/vulkan/vulkan_core.h")
    message(FATAL_ERROR "The repository Vulkan headers are missing: ${MAJ_VULKAN_INCLUDE}")
endif()

# The loader finds this DLL through VkLayer_majdataplay_sync.json; the manifest name and
# the output name must stay in sync.
add_library(MajdataPlaySyncLayer SHARED SyncLayer.cpp)
target_compile_features(MajdataPlaySyncLayer PRIVATE cxx_std_17)
target_include_directories(MajdataPlaySyncLayer PRIVATE "${MAJ_VULKAN_INCLUDE}")
set_target_properties(MajdataPlaySyncLayer PROPERTIES PREFIX "" OUTPUT_NAME "VkLayer_majdataplay_sync")

# Host-only state tests. They compile the layer translation unit directly against fake
# entry points, so no Vulkan loader, instance, device or GPU work is involved.
add_executable(SyncLayerStateTests CPUtests.cpp)
target_compile_features(SyncLayerStateTests PRIVATE cxx_std_17)
target_include_directories(SyncLayerStateTests PRIVATE "${MAJ_VULKAN_INCLUDE}")

if(MSVC)
    foreach(target MajdataPlaySyncLayer SyncLayerStateTests)
        target_compile_options(${target} PRIVATE /W4 /WX /EHsc /utf-8)
        set_property(TARGET ${target} PROPERTY MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")
    endforeach()
else()
    message(FATAL_ERROR "This diagnostic layer is built with MSVC only.")
endif()
