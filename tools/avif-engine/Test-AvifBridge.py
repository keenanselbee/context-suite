"""Exercise the native ABI with bounded pixels, alpha, metadata, and refusal cases."""
import argparse
import ctypes as C
from pathlib import Path


U8 = C.c_uint8
U64 = C.c_uint64
I32 = C.c_int32
VOID = C.c_void_p
CANCEL = C.CFUNCTYPE(C.c_int, VOID)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("dll", type=Path)
    args = parser.parse_args()
    native = C.CDLL(str(args.dll.resolve(strict=True)))
    native.cs_avif_abi.restype = C.c_uint32
    native.cs_avif_encode.argtypes = [VOID, U64, I32, I32, I32, VOID, U64,
                                      VOID, U64, VOID, U64, CANCEL, VOID, C.POINTER(VOID)]
    native.cs_avif_encode.restype = I32
    native.cs_avif_length.argtypes = [VOID, C.POINTER(U64)]
    native.cs_avif_length.restype = I32
    native.cs_avif_copy.argtypes = [VOID, VOID, U64]
    native.cs_avif_copy.restype = I32
    native.cs_avif_release.argtypes = [VOID]
    checks = 0

    def expect(condition, label):
        nonlocal checks
        if not condition:
            raise AssertionError(label)
        checks += 1

    expect(native.cs_avif_abi() == 1, "ABI version")
    width = height = 16
    pixels = (U8 * (width * height * 4))()
    for y in range(height):
        for x in range(width):
            offset = (y * width + x) * 4
            pixels[offset:offset + 4] = [x * 17, y * 17, (x + y) * 8, 255]
    source = C.cast(pixels, VOID)
    always_continue = CANCEL(lambda context: 0)
    always_cancel = CANCEL(lambda context: 1)

    def encode(callback=always_continue, icc=None, exif=None, xmp=None,
               image=source, size=len(pixels), w=width, h=height, stride=width * 4):
        result = VOID(123)
        metadata = []
        for item in (icc, exif, xmp):
            if item is None:
                metadata.extend((None, 0))
            else:
                buffer = (U8 * len(item)).from_buffer_copy(item)
                metadata.extend((C.cast(buffer, VOID), len(item)))
                buffers.append(buffer)
        return native.cs_avif_encode(image, size, w, h, stride, *metadata, callback, None, C.byref(result)), result

    buffers = []
    for transparent in (False, True):
        if transparent:
            pixels[3] = 0
            pixels[7] = 128
        status, result = encode(xmp=b"<x:xmpmeta/>")
        expect(status == 0 and result.value, "encode with alpha=" + str(transparent))
        length = U64()
        expect(native.cs_avif_length(result, C.byref(length)) == 0 and 0 < length.value < 128 * 1024 * 1024,
               "encoded bound")
        output = (U8 * length.value)()
        expect(native.cs_avif_copy(result, output, length.value - 1) == 1, "short copy refusal")
        expect(native.cs_avif_copy(result, output, length.value) == 0, "copy")
        expect(bytes(output[4:12]) == b"ftypavif", "AVIF signature")
        native.cs_avif_release(result)
    status, result = encode(callback=always_cancel)
    expect(status == 5 and not result.value, "pre-encode cancellation")
    calls = 0

    def cancel_after_encode(context):
        nonlocal calls
        calls += 1
        return int(calls >= 3)

    status, result = encode(callback=CANCEL(cancel_after_encode))
    expect(status == 5 and not result.value and calls >= 3, "post-encode cancellation")
    for label, options, wanted in (
        ("short RGBA", {"size": len(pixels) - 1}, 1),
        ("wrong stride", {"stride": width * 4 + 4}, 1),
        ("zero dimension", {"w": 0}, 1),
        ("pixel cap", {"w": 16384, "h": 16384}, 4),
        ("metadata cap", {"xmp": b"a" * (4 * 1024 * 1024 + 1)}, 4),
        ("invalid EXIF", {"exif": b"garbage"}, 2),
    ):
        status, result = encode(**options)
        expect(status == wanted and not result.value, label)
    expect(native.cs_avif_length(None, C.byref(U64())) == 1, "null result")
    print(f"Passed {checks} native AVIF ABI checks: {args.dll}")


if __name__ == "__main__":
    main()
