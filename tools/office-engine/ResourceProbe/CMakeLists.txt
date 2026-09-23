cmake_minimum_required(VERSION 3.24)
project(ContextSuiteOfficeResourceProbe LANGUAGES CXX)
if(NOT WIN32 OR NOT CMAKE_SIZEOF_VOID_P EQUAL 8)
  message(FATAL_ERROR "The resource probe requires Windows x64.")
endif()
add_executable(ContextSuite.Office.ResourceProbe Probe.cpp)
target_compile_features(ContextSuite.Office.ResourceProbe PRIVATE cxx_std_20)
target_compile_options(ContextSuite.Office.ResourceProbe PRIVATE /W4 /WX /utf-8 /Brepro)
target_link_options(ContextSuite.Office.ResourceProbe PRIVATE /Brepro)
target_link_libraries(ContextSuite.Office.ResourceProbe PRIVATE advapi32 psapi)
set_target_properties(ContextSuite.Office.ResourceProbe PROPERTIES
  MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>"
  RUNTIME_OUTPUT_DIRECTORY "${CMAKE_BINARY_DIR}/bin")
