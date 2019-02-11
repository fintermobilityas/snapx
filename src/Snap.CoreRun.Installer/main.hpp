// https://stackoverflow.com/a/4865249
// https://balau82.wordpress.com/2012/02/19/linking-a-binary-blob-with-gcc/
// https://stackoverflow.com/questions/47414607/how-to-include-data-object-files-images-etc-in-program-and-access-the-symbol

#include <stdint.h>

#if PLATFORM_WINDOWS && !PLATFORM_MINGW
#define NUPKG_EXTERN extern "C"
#else
#define NUPKG_EXTERN extern
#endif

NUPKG_EXTERN uint8_t _installer_nupkg_start;
NUPKG_EXTERN uint8_t _installer_nupkg_size;
NUPKG_EXTERN uint8_t _installer_nupkg_end;
