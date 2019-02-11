#include "corerun.hpp"
#include "stubexecutable.hpp"
#include "coreclr.hpp"
#include "installer.hpp"
#include "vendor/cxxopts/cxxopts.hpp"

#if PLATFORM_WINDOWS
#include "vendor/rcedit/rcedit.hpp"
#endif

#if PLATFORM_LINUX
// https://stackoverflow.com/a/4865249
// https://balau82.wordpress.com/2012/02/19/linking-a-binary-blob-with-gcc/
// https://stackoverflow.com/questions/47414607/how-to-include-data-object-files-images-etc-in-program-and-access-the-symbol
extern uint8_t _installer_nupkg_start;
extern uint8_t _installer_nupkg_size;
extern uint8_t _installer_nupkg_end;
#endif