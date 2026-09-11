#include <stdio.h>

extern int ContextSuiteNasmAdd(int left, int right);
extern int ContextSuiteNasmSum4(const int *values);

int main(void)
{
    const int values[] = { 12, -25, 93, 72 };
    if (ContextSuiteNasmAdd(-19, 61) != 42 || ContextSuiteNasmSum4(values) != 152)
        return 1;
    puts("NASM Win64 COFF linked with MSVC; scalar and SSE2 results passed.");
    return 0;
}
