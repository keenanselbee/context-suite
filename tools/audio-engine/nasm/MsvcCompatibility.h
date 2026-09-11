// NASM's MSVC configuration maps inline to __inline, while compiler.h chooses
// C99 extern-inline semantics under /std:c11. MSVC emits duplicate external
// definitions for that combination. Give header copies internal linkage; the
// upstream ILOG2_C branch still emits the single out-of-line implementation.
#include "compiler.h"
#undef extern_inline
#define extern_inline static inline
#undef inline_prototypes
