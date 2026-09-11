cmake_minimum_required(VERSION 3.18)
project(ContextSuiteLameDependency LANGUAGES C)

if(NOT WIN32 OR NOT CMAKE_C_COMPILER_ID STREQUAL "MSVC" OR NOT CMAKE_SIZEOF_VOID_P EQUAL 8)
    message(FATAL_ERROR "This dependency recipe requires Windows x64 MSVC.")
endif()
if(NOT IS_DIRECTORY "${LAME_SOURCE}")
    message(FATAL_ERROR "Use the pinned source from Build-AudioDependency.ps1.")
endif()

# The source list follows the pinned core library project, excluding frontends,
# decoders, drivers and network clients. Source/configuration files stay unchanged.
set(core_files bitstream encoder fft gain_analysis id3tag lame mpglib_interface
    newmdct presets psymodel quantize quantize_pvt reservoir set_get tables
    takehiro util vbrquantize VbrTag version)
set(vector_files xmm_quantize_sub xmm_choose_table avx2_choose_table
    xmm_quantize_lines avx2_quantize_lines xmm_calc_sfb_noise avx512_quantize_lines)
set(lame_sources "")
foreach(name IN LISTS core_files)
    list(APPEND lame_sources "${LAME_SOURCE}/libmp3lame/${name}.c")
endforeach()
foreach(name IN LISTS vector_files)
    list(APPEND lame_sources "${LAME_SOURCE}/libmp3lame/vector/${name}.c")
endforeach()
configure_file("${LAME_SOURCE}/configMS.h" "${CMAKE_CURRENT_BINARY_DIR}/config.h" COPYONLY)
add_library(mp3lame STATIC ${lame_sources})
target_include_directories(mp3lame PUBLIC "${LAME_SOURCE}/include"
    PRIVATE "${CMAKE_CURRENT_BINARY_DIR}" "${LAME_SOURCE}" "${LAME_SOURCE}/libmp3lame")
target_compile_definitions(mp3lame PRIVATE HAVE_CONFIG_H)
target_compile_options(mp3lame PRIVATE /GS /guard:cf /guard:ehcont)
set_property(TARGET mp3lame PROPERTY MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>DLL")

add_executable(ContextSuite.Lame.Probe Probe.c)
target_link_libraries(ContextSuite.Lame.Probe PRIVATE mp3lame)
target_link_options(ContextSuite.Lame.Probe PRIVATE /guard:cf /CETCOMPAT)
enable_testing()
add_test(NAME ContextSuite.Lame.Encode COMMAND ContextSuite.Lame.Probe "${CMAKE_CURRENT_BINARY_DIR}/lame-vbr2.mp3")
