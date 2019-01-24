# For cross-compiling on arm64 Linux using gcc-aarch64-linux-gnu package:
# - install AArch64 tool chain:
#   $ sudo apt-get install g++-aarch64-linux-gnu
set(CMAKE_SYSTEM_NAME Linux)
set(CMAKE_SYSTEM_PROCESSOR aarch64)
set(TARGET_ABI "linux-gnu")

# specify the cross compiler
SET(CMAKE_C_COMPILER   aarch64-${TARGET_ABI}-gcc)
SET(CMAKE_CXX_COMPILER aarch64-${TARGET_ABI}-g++)

# To build the tests, we need to set where the target environment containing
# the required library is. On Debian-like systems, this is
# /usr/aarch64-linux-gnu.
SET(CMAKE_FIND_ROOT_PATH "/usr/aarch64-${TARGET_ABI}")

# search for programs in the build host directories
SET(CMAKE_FIND_ROOT_PATH_MODE_PROGRAM NEVER)

# for libraries and headers in the target directories
SET(CMAKE_FIND_ROOT_PATH_MODE_LIBRARY ONLY)
SET(CMAKE_FIND_ROOT_PATH_MODE_INCLUDE ONLY)

# Set additional variables.
# If we don't set some of these, CMake will end up using the host version.
# We want the full path, however, so we can pass EXISTS and other checks in
# the our CMake code.
find_program(GCC_FULL_PATH aarch64-${TARGET_ABI}-gcc)
if (NOT GCC_FULL_PATH)
  message(FATAL_ERROR "Cross-compiler aarch64-${TARGET_ABI}-gcc not found")
endif ()
get_filename_component(GCC_DIR ${GCC_FULL_PATH} PATH)
SET(CMAKE_LINKER       ${GCC_DIR}/aarch64-${TARGET_ABI}-ld      CACHE FILEPATH "linker")
SET(CMAKE_ASM_COMPILER ${GCC_DIR}/aarch64-${TARGET_ABI}-as      CACHE FILEPATH "assembler")
SET(CMAKE_OBJCOPY      ${GCC_DIR}/aarch64-${TARGET_ABI}-objcopy CACHE FILEPATH "objcopy")
SET(CMAKE_STRIP        ${GCC_DIR}/aarch64-${TARGET_ABI}-strip   CACHE FILEPATH "strip")
SET(CMAKE_CPP          ${GCC_DIR}/aarch64-${TARGET_ABI}-cpp     CACHE FILEPATH "cpp")