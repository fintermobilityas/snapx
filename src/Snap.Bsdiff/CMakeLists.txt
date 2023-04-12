cmake_minimum_required (VERSION 3.10 FATAL_ERROR)

project(snap_bsdiff CXX)

set(snap_bsdiff_SOURCES
        src/lib.cpp
        )

set(snap_bsdiff_INCLUDE_DIRS PRIVATE
        ../Vendor/bsdiff/include
        src/include)


set(snap_bsdiff_static_LIBS )

if(WIN32)
list(APPEND snap_bsdiff_DEFINES SNAP_PLATFORM_WINDOWS)
elseif(UNIX)
list(APPEND snap_bsdiff_DEFINES SNAP_PLATFORM_LINUX)
list(APPEND snap_bsdiff_static_LIBS libstdc++.a)
else()
message(FATAL_ERROR "Error: Unsupported platform")
endif()

add_library(snap_bsdiff SHARED ${snap_bsdiff_SOURCES})

target_link_libraries(snap_bsdiff PUBLIC bsdiff ${snap_bsdiff_static_LIBS})
target_include_directories(snap_bsdiff PUBLIC ${snap_bsdiff_INCLUDE_DIRS})
target_compile_definitions(snap_bsdiff PRIVATE ${snap_bsdiff_DEFINES})

set_property(TARGET snap_bsdiff PROPERTY CXX_STANDARD 17)
set_property(TARGET snap_bsdiff PROPERTY CXX_STANDARD_REQUIRED ON)
set_property(TARGET snap_bsdiff PROPERTY POSITION_INDEPENDENT_CODE ON)

