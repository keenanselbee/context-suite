cmake_minimum_required(VERSION 3.24)
project(ContextSuiteOfficeIsolationProbe LANGUAGES CXX)
if(NOT WIN32 OR NOT CMAKE_SIZEOF_VOID_P EQUAL 8)
  message(FATAL_ERROR "The isolation experiment requires Windows x64.")
endif()
add_executable(ContextSuite.Office.IsolationProbe Probe.cpp)
target_compile_features(ContextSuite.Office.IsolationProbe PRIVATE cxx_std_20)
target_compile_options(ContextSuite.Office.IsolationProbe PRIVATE /W4 /WX /utf-8)
target_compile_definitions(ContextSuite.Office.IsolationProbe PRIVATE UNICODE _UNICODE WIN32_LEAN_AND_MEAN NOMINMAX)
target_link_libraries(ContextSuite.Office.IsolationProbe PRIVATE userenv advapi32 ws2_32)
set_target_properties(ContextSuite.Office.IsolationProbe PROPERTIES
  MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>"
  RUNTIME_OUTPUT_DIRECTORY "${CMAKE_BINARY_DIR}/bin")
