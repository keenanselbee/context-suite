bits 64
default rel

section .text
global ContextSuiteNasmAdd
ContextSuiteNasmAdd:
    lea eax, [ecx + edx]
    ret

global ContextSuiteNasmSum4
ContextSuiteNasmSum4:
    movdqu xmm0, [rcx]
    pshufd xmm1, xmm0, 0x4e
    paddd xmm0, xmm1
    pshufd xmm1, xmm0, 0xb1
    paddd xmm0, xmm1
    movd eax, xmm0
    ret
