cmake_minimum_required(VERSION 3.18)
project(ContextSuiteMakeTool LANGUAGES C)

if(NOT WIN32 OR NOT CMAKE_C_COMPILER_ID STREQUAL "MSVC" OR NOT CMAKE_SIZEOF_VOID_P EQUAL 8)
    message(FATAL_ERROR "This build-tool recipe requires Windows x64 MSVC.")
endif()
if(NOT IS_DIRECTORY "${MAKE_SOURCE}")
    message(FATAL_ERROR "Use the pinned source from Build-AudioDependency.ps1.")
endif()

# Follow build_w32.bat's MSVC source/configuration selection without its in-tree
# writes or cleanup. Guile discovery and installation are intentionally absent.
set(core_files ar arscan commands default dir expand file function getopt getopt1
    guile hash implicit job load loadapi main misc output read remake remote-stub
    rule shuffle signame strcache variable version vpath)
set(platform_files pathstuff w32os compat/posixfcn compat/dirent subproc/misc
    subproc/sub_proc subproc/w32err)
set(make_sources "")
foreach(name IN LISTS core_files)
    list(APPEND make_sources "${MAKE_SOURCE}/src/${name}.c")
endforeach()
foreach(name IN LISTS platform_files)
    list(APPEND make_sources "${MAKE_SOURCE}/src/w32/${name}.c")
endforeach()
foreach(name fnmatch glob getloadavg)
    list(APPEND make_sources "${MAKE_SOURCE}/lib/${name}.c")
endforeach()
configure_file("${MAKE_SOURCE}/src/config.h.W32" "${CMAKE_CURRENT_BINARY_DIR}/src/config.h" COPYONLY)
configure_file("${MAKE_SOURCE}/lib/glob.in.h" "${CMAKE_CURRENT_BINARY_DIR}/lib/glob.h" COPYONLY)
configure_file("${MAKE_SOURCE}/lib/fnmatch.in.h" "${CMAKE_CURRENT_BINARY_DIR}/lib/fnmatch.h" COPYONLY)
string(REGEX REPLACE "/W[0-4]" "/W4" CMAKE_C_FLAGS "${CMAKE_C_FLAGS}")
add_executable(gnumake ${make_sources})
target_include_directories(gnumake PRIVATE "${CMAKE_CURRENT_BINARY_DIR}/src" "${MAKE_SOURCE}/src"
    "${CMAKE_CURRENT_BINARY_DIR}/lib" "${MAKE_SOURCE}/lib" "${MAKE_SOURCE}/src/w32/include")
target_compile_definitions(gnumake PRIVATE _CONSOLE HAVE_CONFIG_H)
target_compile_options(gnumake PRIVATE /GS /guard:cf /guard:ehcont)
target_link_options(gnumake PRIVATE /guard:cf /CETCOMPAT)
target_link_libraries(gnumake PRIVATE kernel32 user32 gdi32 winspool comdlg32 advapi32
    shell32 ole32 oleaut32 uuid odbc32 odbccp32)
set_property(TARGET gnumake PROPERTY MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")

find_package(Python3 3.14 REQUIRED COMPONENTS Interpreter)
enable_testing()
add_test(NAME ContextSuite.Make.Version COMMAND gnumake --version)
set_tests_properties(ContextSuite.Make.Version PROPERTIES PASS_REGULAR_EXPRESSION "GNU Make 4\\.4\\.1")
add_test(NAME ContextSuite.Make.Workflow COMMAND "${Python3_EXECUTABLE}" -B "${CMAKE_CURRENT_SOURCE_DIR}/Smoke.py"
    "$<TARGET_FILE:gnumake>" "${CMAKE_CURRENT_BINARY_DIR}/workflow")
