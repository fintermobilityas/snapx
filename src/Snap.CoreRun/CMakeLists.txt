cmake_minimum_required (VERSION 3.10 FATAL_ERROR)

project(corerun CXX)

set(corerun_SOURCES
        src/corerun.hpp
        src/stubexecutable.cpp
        src/vendor/semver/semver200_comparator.cpp
        src/vendor/semver/semver200_parser.cpp
        )

include_directories(SYSTEM
        vendor
        ../Vendor
        )

add_executable(corerun
        ${corerun_SOURCES}
        src/main.cpp
        )

add_executable(corerun_demoapp
        src/main_demoapp.cpp
        )

add_library(corerun_static
        ${corerun_SOURCES}
        )

target_include_directories(corerun_demoapp SYSTEM PUBLIC
        ../Vendor/json/include
        )

target_link_libraries(corerun_demoapp PUBLIC
        pal_static
        )

target_include_directories(corerun_static SYSTEM PUBLIC
        vendor
        )

target_include_directories(corerun_static PUBLIC
        src
        )

target_link_libraries(corerun_static PUBLIC
        pal_static
        )

target_link_libraries(corerun PUBLIC pal_static)

if(WIN32)

    if (CMAKE_CXX_COMPILER_ID MATCHES "MSVC")
        set(CMAKE_EXE_LINKER_FLAGS "${CMAKE_EXE_LINKER_FLAGS} /ENTRY:wWinMainCRTStartup /subsystem:windows")
        set(CMAKE_CREATE_CONSOLE_EXE "/subsystem:windows") # Override subsystem on Windows.
    elseif (CMAKE_COMPILER_IS_GNUCXX)
        set(CMAKE_EXE_LINKER_FLAGS "${CMAKE_EXE_LINKER_FLAGS} -mwindows -municode -Wl,-subsystem,windows")
    endif()

    pal_mitigate_dll_hijacking_for_targets(corerun corerun_demoapp)
endif()

set_property(TARGET corerun corerun_static corerun_demoapp PROPERTY CXX_STANDARD 17)
set_property(TARGET corerun corerun_static corerun_demoapp PROPERTY CXX_STANDARD_REQUIRED ON)
