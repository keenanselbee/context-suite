cmake_minimum_required(VERSION 3.24)
project(ContextSuitePdfDark LANGUAGES CXX)

add_executable(ContextSuite.PdfDark PdfDark.cpp)
target_compile_features(ContextSuite.PdfDark PRIVATE cxx_std_20)
target_compile_definitions(ContextSuite.PdfDark PRIVATE NOMINMAX WIN32_LEAN_AND_MEAN)
target_compile_options(ContextSuite.PdfDark PRIVATE /W4 /WX /utf-8 /Brepro)
target_link_options(ContextSuite.PdfDark PRIVATE /Brepro)
target_include_directories(ContextSuite.PdfDark SYSTEM PRIVATE "${QPDF_PAYLOAD}/include")
target_link_libraries(ContextSuite.PdfDark PRIVATE "${QPDF_PAYLOAD}/lib/qpdf.lib" dwrite)
set_target_properties(ContextSuite.PdfDark PROPERTIES RUNTIME_OUTPUT_DIRECTORY "${CMAKE_BINARY_DIR}/bin")
foreach(runtime qpdf30.dll concrt140.dll msvcp140.dll msvcp140_1.dll msvcp140_2.dll
    msvcp140_atomic_wait.dll msvcp140_codecvt_ids.dll vcruntime140.dll vcruntime140_1.dll)
  add_custom_command(TARGET ContextSuite.PdfDark POST_BUILD COMMAND ${CMAKE_COMMAND} -E copy_if_different
    "${QPDF_PAYLOAD}/bin/${runtime}" "$<TARGET_FILE_DIR:ContextSuite.PdfDark>")
endforeach()
