cmake_minimum_required(VERSION 3.24)
project(ContextSuiteOfficeDateProbe LANGUAGES CXX)
if(NOT WIN32 OR NOT CMAKE_SIZEOF_VOID_P EQUAL 8)
  message(FATAL_ERROR "The date experiment requires Windows x64.")
endif()
add_executable(ContextSuite.Office.DateProbe Probe.cpp)
target_compile_features(ContextSuite.Office.DateProbe PRIVATE cxx_std_20)
target_compile_options(ContextSuite.Office.DateProbe PRIVATE /W4 /WX /utf-8)
target_compile_definitions(ContextSuite.Office.DateProbe PRIVATE UNICODE _UNICODE WIN32_LEAN_AND_MEAN NOMINMAX)
target_link_libraries(ContextSuite.Office.DateProbe PRIVATE user32)
set_target_properties(ContextSuite.Office.DateProbe PROPERTIES
  MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>"
  RUNTIME_OUTPUT_DIRECTORY "${CMAKE_BINARY_DIR}/bin")
