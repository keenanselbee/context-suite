#include <stdio.h>
#include <string.h>
#include <opus.h>

int main(void)
{
    const char *actual = opus_get_version_string();
    puts(actual);
    if (strcmp(actual, EXPECTED_OPUS_VERSION) != 0)
    {
        fputs("The linked Opus library did not report its pinned source version.\n", stderr);
        return 1;
    }
    return 0;
}
