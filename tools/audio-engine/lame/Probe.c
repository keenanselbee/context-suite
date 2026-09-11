#include <math.h>
#include <stdio.h>
#include <string.h>
#include <lame.h>

enum { sample_count = 44100, buffer_size = 65536 };
static short samples[sample_count * 2];
static unsigned char encoded[buffer_size];
static unsigned char tag[16384];

int main(int argc, char **argv)
{
    if (argc != 2)
        return 1;
    lame_t encoder = lame_init();
    if (!encoder)
        return 2;
    printf("LAME %s\n", get_lame_version());
#ifdef CONTEXT_SUITE_LAME_STABLE
    if (strcmp(get_lame_version(), "4.0") != 0)
    {
        lame_close(encoder);
        return 9;
    }
#endif
    if (lame_set_in_samplerate(encoder, 44100) != 0 ||
        lame_set_num_channels(encoder, 2) != 0 ||
        lame_set_num_samples(encoder, sample_count) != 0 ||
        lame_set_VBR(encoder, vbr_default) != 0 ||
        lame_set_VBR_quality(encoder, 2.0f) != 0 ||
        lame_set_bWriteVbrTag(encoder, 1) != 0 || lame_init_params(encoder) != 0)
    {
        lame_close(encoder);
        return 3;
    }
    for (int index = 0; index < sample_count; ++index)
    {
        samples[index * 2] = (short)(12000.0 * sin(6.283185307179586 * 440.0 * index / 44100.0));
        samples[index * 2 + 1] = (short)(9000.0 * sin(6.283185307179586 * 880.0 * index / 44100.0));
    }
    int length = lame_encode_buffer_interleaved(encoder, samples, sample_count, encoded, buffer_size);
    if (length < 0 || length >= buffer_size)
    {
        lame_close(encoder);
        return 4;
    }
    int flushed = lame_encode_flush(encoder, encoded + length, buffer_size - length);
    if (flushed < 0 || flushed > buffer_size - length)
    {
        lame_close(encoder);
        return 5;
    }
    length += flushed;
    size_t tag_length = lame_get_lametag_frame(encoder, tag, sizeof(tag));
    lame_close(encoder);
    if (tag_length == 0 || tag_length > sizeof(tag) || tag_length > (size_t)length)
        return 6;
    memcpy(encoded, tag, tag_length);
    FILE *output = NULL;
    if (fopen_s(&output, argv[1], "wb") != 0 || !output)
        return 7;
    size_t written = fwrite(encoded, 1, (size_t)length, output);
    int closed = fclose(output);
    if (written != (size_t)length || closed != 0)
        return 8;
    printf("Encoded %d stereo frames at VBR quality 2 into %d bytes.\n", sample_count, length);
    return 0;
}
