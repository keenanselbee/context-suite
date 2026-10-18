"""Trim the pinned vgmstream scratch source to Wwise PCM/Vorbis entry points."""

import pathlib
import sys


def replace_once(path, old, new):
    text = path.read_text(encoding="utf-8")
    if text.count(old) != 1:
        raise ValueError(f"Pinned source layout changed: {path}: {old[:45]!r}")
    path.write_text(text.replace(old, new), encoding="utf-8", newline="\n")


source = pathlib.Path(sys.argv[1]).resolve(strict=True)
init = source / "src/vgmstream_init.c"
replace_once(init, "init_vgmstream_t init_vgmstream_functions[] = {",
             "#ifdef VGM_WEM_ONLY\ninit_vgmstream_t init_vgmstream_functions[] = {\n"
             "    init_vgmstream_wwise,\n};\n#else\n"
             "init_vgmstream_t init_vgmstream_functions[] = {")
replace_once(init, "};\n\n#define LOCAL_ARRAY_LENGTH(array)",
             "};\n#endif\n\n#define LOCAL_ARRAY_LENGTH(array)")

codec_info = source / "src/base/codec_info.c"
replace_once(codec_info, "    switch(v->coding_type) {",
             "#ifdef VGM_WEM_ONLY\n"
             "    if (v->coding_type == coding_VORBIS_custom) return &vorbis_custom_decoder;\n"
             "    return NULL;\n#else\n"
             "    switch(v->coding_type) {")
replace_once(codec_info, "            return NULL;\n    }\n}",
             "            return NULL;\n    }\n#endif\n}")

info = source / "src/base/info.c"
replace_once(info,
             "static STREAMFILE* get_vgmstream_average_bitrate_channel_streamfile(VGMSTREAM* vgmstream, int channel) {\n",
             "static STREAMFILE* get_vgmstream_average_bitrate_channel_streamfile(VGMSTREAM* vgmstream, int channel) {\n"
             "#ifdef VGM_WEM_ONLY\n"
             "    return vgmstream->ch[channel].streamfile;\n"
             "#else\n")
replace_once(info,
             "    return vgmstream->ch[channel].streamfile;\n}\n\nstatic int get_vgmstream_file_bitrate_from_size",
             "    return vgmstream->ch[channel].streamfile;\n"
             "#endif\n}\n\nstatic int get_vgmstream_file_bitrate_from_size")

cmake = source / "src/CMakeLists.txt"
replace_once(cmake, "# libvgmstream.a\n",
             "# The optional WEM-only build omits unrelated codecs with separate provenance.\n"
             "if(VGM_WEM_ONLY)\n"
             "    list(REMOVE_ITEM libvgmstream_sources\n"
             "        ${CMAKE_CURRENT_SOURCE_DIR}/coding/libs/relic_mixfft.c\n"
             "        ${CMAKE_CURRENT_SOURCE_DIR}/coding/libs/relic_lib.c\n"
             "        ${CMAKE_CURRENT_SOURCE_DIR}/coding/libs/compresswave_lib.c\n"
             "        ${CMAKE_CURRENT_SOURCE_DIR}/coding/relic_decoder.c\n"
             "        ${CMAKE_CURRENT_SOURCE_DIR}/coding/compresswave_decoder.c\n"
             "        ${CMAKE_CURRENT_SOURCE_DIR}/coding/mio_decoder.c\n"
             "        ${CMAKE_CURRENT_SOURCE_DIR}/meta/bnk_relic.c\n"
             "        ${CMAKE_CURRENT_SOURCE_DIR}/meta/compresswave.c\n"
             "        ${CMAKE_CURRENT_SOURCE_DIR}/meta/mio.c)\n"
             "endif()\n\n# libvgmstream.a\n")
replace_once(cmake, "add_library(libvgmstream STATIC ${libvgmstream_sources} ${libvgmstream_headers})\n",
             "add_library(libvgmstream STATIC ${libvgmstream_sources} ${libvgmstream_headers})\n"
             "if(VGM_WEM_ONLY)\n"
             "    target_compile_definitions(libvgmstream PRIVATE VGM_WEM_ONLY)\n"
             "endif()\n")
install_cmake = source / "cmake/vgmstream.cmake"
replace_once(install_cmake,
             "set(VORBIS_DLL ${VGM_SOURCE_DIR}/ext_libs/libvorbis.dll)",
             "set(VORBIS_DLL ${VGM_SOURCE_DIR}/ext_libs/dll-x64/libvorbis.dll)")
excluded = (
    "src/coding/relic_decoder.c", "src/coding/compresswave_decoder.c",
    "src/coding/mio_decoder.c", "src/coding/libs/relic_mixfft.c",
    "src/coding/libs/relic_lib.c", "src/coding/libs/relic_lib.h",
    "src/coding/libs/compresswave_lib.c", "src/coding/libs/compresswave_lib.h",
    "src/coding/libs/mio_xerisa.h", "src/coding/libs/mio_erisasound.h",
    "src/coding/libs/mio_erisasound.c", "src/coding/libs/mio_erisamatrix.h",
    "src/coding/libs/mio_erisamatrix.c", "src/coding/libs/mio_erisafile.h",
    "src/coding/libs/mio_erisafile.c", "src/coding/libs/mio_erisacontext.h",
    "src/coding/libs/mio_erisacontext.c", "src/meta/bnk_relic.c",
    "src/meta/compresswave.c", "src/meta/mio.c",
)
for relative in excluded:
    path = source / relative
    if not path.is_file():
        raise ValueError(f"Pinned excluded source missing: {relative}")
    path.unlink()
print("Applied WEM-only scratch patch to pinned vgmstream source.")
