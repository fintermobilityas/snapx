cmake_minimum_required (VERSION 3.10 FATAL_ERROR)

project(snap)

if(" ${CMAKE_SOURCE_DIR}" STREQUAL " ${CMAKE_BINARY_DIR}")
    message(FATAL_ERROR "
FATAL: In-source builds are not allowed.
       You should create a separate directory for build files.
")
endif()

set(CMAKE_CONFIGURATION_TYPES "Debug;Release" CACHE STRING "" FORCE)

set(GNU_CXX_WARNING_FLAGS "-Wall -Weffc++ -Wextra -Wsign-conversion")
set(MSVS_CXX_WARNING_FLAGS "/W4")

option(BUILD_ENABLE_LTO "Build with LINK TIME OPTIMIZATION enabled" OFF)
option(BUILD_ENABLE_TESTS "Build with tests enabled" OFF)
option(BUILD_ENABLE_LOGGING "Build with logging enabled" ON)

add_subdirectory(Snap.CoreRun.Pal)
add_subdirectory(Snap.CoreRun)

if (BUILD_ENABLE_TESTS)

    message(STATUS "Unit tests enabled.")

    add_subdirectory(Vendor/gtest)
    add_subdirectory(Vendor/nanoid_cpp)
    enable_testing()

    set(GTEST_ALL_CPP_FILENAME ${PROJECT_SOURCE_DIR}/Vendor/gtest/googletest/src/gtest-all.cc)
    set_source_files_properties(${GTEST_ALL_CPP_FILENAME} PROPERTIES LANGUAGE CXX)

    add_subdirectory(Snap.CoreRun.Tests)

else()

    message(STATUS "Unit tests disabled.")

endif()

if(WIN32)
    set(GNU_CXX_LTO_FLAGS_RELEASE "-s -flto -fwhole-program -ffunction-sections -fdata-sections -Wl,--gc-sections -ffast-math")

    if (CMAKE_CXX_COMPILER_ID MATCHES "MSVC")
        set(CMAKE_CXX_FLAGS_DEBUG    "${CMAKE_CXX_FLAGS_DEBUG} /MP -DDEBUG -D_DEBUG ${MSVS_CXX_WARNING_FLAGS}")
        set(CMAKE_CXX_FLAGS_RELEASE  "${CMAKE_CXX_FLAGS_RELEASE} /MP -DNDEBUG ${MSVS_CXX_WARNING_FLAGS}")

        if(BUILD_ENABLE_LTO)
            set(CMAKE_CXX_FLAGS_RELEASE "${CMAKE_CXX_FLAGS_RELEASE} /O1 /GL")
            set(CMAKE_EXE_LINKER_FLAGS_RELEASE "${CMAKE_EXE_LINKER_FLAGS_RELEASE} /LTCG")
        endif()

    elseif (CMAKE_COMPILER_IS_GNUCXX)
        set(CMAKE_CXX_FLAGS_DEBUG "-g -DDEBUG ${GNU_CXX_WARNING_FLAGS}")
        set(CMAKE_CXX_FLAGS_RELEASE "-O -DNDEBUG ${GNU_CXX_WARNING_FLAGS}")
        if(BUILD_ENABLE_LTO)
            set(CMAKE_EXE_LINKER_FLAGS_RELEASE "${CMAKE_EXE_LINKER_FLAGS_RELEASE} ${GNU_CXX_LTO_FLAGS_RELEASE}")
        endif()
    endif()
elseif(UNIX)
    if ("${CMAKE_CXX_COMPILER_ID}" STREQUAL "GNU")
        set(CMAKE_CXX_FLAGS_DEBUG "${CMAKE_CXX_FLAGS_DEBUG} -g -DDEBUG ${GNU_CXX_WARNING_FLAGS}")
        set(CMAKE_CXX_FLAGS_RELEASE "${CMAKE_CXX_FLAGS_RELEASE} -O -DNDEBUG ${GNU_CXX_WARNING_FLAGS}")
        if(BUILD_ENABLE_LTO)
            set(CMAKE_EXE_LINKER_FLAGS_RELEASE "${CMAKE_EXE_LINKER_FLAGS_RELEASE} ${GNU_CXX_LTO_FLAGS_RELEASE}")
        endif()
    endif()
endif()

if(CMAKE_CXX_COMPILER_VERSION)
    set(SNAP_COMPILER_STR "${CMAKE_CXX_COMPILER} ${CMAKE_CXX_COMPILER_ARG1} (ver ${CMAKE_CXX_COMPILER_VERSION})")
else()
    set(SNAP_COMPILER_STR "${CMAKE_CXX_COMPILER} ${CMAKE_CXX_COMPILER_ARG1}")
endif()
string(STRIP "${SNAP_COMPILER_STR}" SNAP_COMPILER_STR)

message(STATUS "")
message(STATUS "  Options:")
message(STATUS "    Lto: "           ${BUILD_ENABLE_LTO})
message(STATUS "    Tests: "		 ${BUILD_ENABLE_TESTS})
message(STATUS "    Toolchain file: " ${CMAKE_TOOLCHAIN_FILE})

message(STATUS "  C/C++:")
message(STATUS "    C++ Compiler: "           ${SNAP_COMPILER_STR})
message(STATUS "    C++ flags (Debug): "      ${CMAKE_CXX_FLAGS} ${CMAKE_CXX_FLAGS_DEBUG})
message(STATUS "    C++ flags (Release): "    ${CMAKE_CXX_FLAGS} ${CMAKE_CXX_FLAGS_RELEASE})

if(WIN32 OR UNIX)
    message(STATUS "    Linker flags (Debug):"   ${CMAKE_EXE_LINKER_FLAGS} ${CMAKE_EXE_LINKER_FLAGS_DEBUG})
    message(STATUS "    Linker flags (Release):" ${CMAKE_EXE_LINKER_FLAGS} ${CMAKE_EXE_LINKER_FLAGS_RELEASE})
else()
    message(FATAL_ERROR "Target platform is not supported.")
endif()
