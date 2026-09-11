cmake_minimum_required(VERSION 3.18)
project(ContextSuiteNasmTool LANGUAGES C)

if(NOT WIN32 OR NOT CMAKE_C_COMPILER_ID STREQUAL "MSVC" OR NOT CMAKE_SIZEOF_VOID_P EQUAL 8)
    message(FATAL_ERROR "This build-tool recipe requires Windows x64 MSVC.")
endif()
if(NOT IS_DIRECTORY "${NASM_SOURCE}")
    message(FATAL_ERROR "Use the pinned source from Build-AudioDependency.ps1.")
endif()

# Select the assembler's translation units from Mkfiles/msvc.mak. The release
# includes generated sources; no Perl regeneration, installation or source writes.
set(stdlib_files snprintf vsnprintf strlcpy strnlen)
set(nasmlib_files ver alloc asprintf crc32b crc64 md5c string nctype file fileio mmap
    realpath path ilog2 numstr rlimit zerobuf bsi rbtree hashtbl raa saa strlist
    perfhash badenum readnum)
set(common_files common errstubs)
set(x86_files insnsa insnsb insnsn regs regvals regflags iflag)
set(zlib_files adler32 crc32 infback inffast inflate inftrees zutil)
set(asm_files nasm error floats directiv pragma assemble labels parser preproc quote
    listing eval exprlib exprdump stdscan getbool strfunc segalloc rdstrnum srcfile
    directbl pptok tokhash uncompress warnings)
set(macros_files macros)
set(output_files outform outlib nulldbg nullout outbin outaout outcoff outelf outobj
    outas86 outdbg outieee outmacho codeview)
set(nasm_sources "")
foreach(directory stdlib nasmlib common x86 zlib asm macros output)
    foreach(name IN LISTS ${directory}_files)
        list(APPEND nasm_sources "${NASM_SOURCE}/${directory}/${name}.c")
    endforeach()
endforeach()
string(REGEX REPLACE "/W[0-4]" "/W2" CMAKE_C_FLAGS "${CMAKE_C_FLAGS}")
add_executable(nasm ${nasm_sources})
target_include_directories(nasm PRIVATE "${NASM_SOURCE}" "${NASM_SOURCE}/include"
    "${NASM_SOURCE}/x86" "${NASM_SOURCE}/asm" "${NASM_SOURCE}/disasm"
    "${NASM_SOURCE}/output" "${NASM_SOURCE}/zlib")
target_compile_options(nasm PRIVATE /std:c11 /bigobj /GS /guard:cf /guard:ehcont
    "/FI${CMAKE_CURRENT_SOURCE_DIR}/MsvcCompatibility.h")
# file.c includes stringapiset.h directly. This SDK needs windows.h first to
# establish the architecture macros and API declarations for that header.
set_source_files_properties("${NASM_SOURCE}/nasmlib/file.c" PROPERTIES COMPILE_OPTIONS /FIwindows.h)
target_link_options(nasm PRIVATE /guard:cf /CETCOMPAT /MANIFEST:EMBED
    "/MANIFESTINPUT:${NASM_SOURCE}/win/manifest.xml")
set_property(TARGET nasm PROPERTY MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")

set(probe_object "${CMAKE_CURRENT_BINARY_DIR}/probe.obj")
add_custom_command(OUTPUT "${probe_object}"
    COMMAND "$<TARGET_FILE:nasm>" -f win64 -o "${probe_object}" "${CMAKE_CURRENT_SOURCE_DIR}/Probe.asm"
    DEPENDS nasm "${CMAKE_CURRENT_SOURCE_DIR}/Probe.asm" VERBATIM)
set_source_files_properties("${probe_object}" PROPERTIES EXTERNAL_OBJECT TRUE GENERATED TRUE)
add_executable(ContextSuite.Nasm.Probe Probe.c "${probe_object}")
target_link_options(ContextSuite.Nasm.Probe PRIVATE /guard:cf /CETCOMPAT)
enable_testing()
add_test(NAME ContextSuite.Nasm.Version COMMAND nasm -v)
set_tests_properties(ContextSuite.Nasm.Version PROPERTIES PASS_REGULAR_EXPRESSION "NASM version 3\\.02")
add_test(NAME ContextSuite.Nasm.CoffLink COMMAND ContextSuite.Nasm.Probe)
