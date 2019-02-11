cmake_minimum_required (VERSION 3.10 FATAL_ERROR)

project(corerun CXX C)

add_subdirectory(pal)

list(APPEND corerun_INCLUDE_DIRS
    vendor
    vendor/easyloggingpp/src
    vendor/miniz
    vendor/elfio
    pal
)

set(corerun_SOURCES
    coreclr.cpp
    corerun.cpp
    stubexecutable.cpp
    installer.cpp
    vendor/easyloggingpp/src/easylogging++.cc
    vendor/semver/semver200_comparator.cpp
    vendor/semver/semver200_parser.cpp
    vendor/miniz/miniz.c
)

add_executable(corerun
    ${corerun_SOURCES}
    main.cpp
)

target_include_directories(corerun PRIVATE
    ${corerun_INCLUDE_DIRS}
)

target_compile_definitions(corerun PRIVATE 
    ELPP_NO_DEFAULT_LOG_FILE
)

if(WIN32)

    if (CMAKE_CXX_COMPILER_ID MATCHES "MSVC")
        set(CMAKE_EXE_LINKER_FLAGS "${CMAKE_EXE_LINKER_FLAGS} /subsystem:windows")
        set(CMAKE_CREATE_CONSOLE_EXE "/subsystem:windows") # Override subsystem on Windows.
    elseif (CMAKE_COMPILER_IS_GNUCXX)
        set(CMAKE_EXE_LINKER_FLAGS "${CMAKE_EXE_LINKER_FLAGS} -mwindows -municode -Wl,-subsystem,windows")
        target_compile_definitions(corerun PRIVATE 
            PLATFORM_MINGW
        )
    endif()

    set(CompilerFlags
        CMAKE_CXX_FLAGS
        CMAKE_CXX_FLAGS_DEBUG
        CMAKE_CXX_FLAGS_RELEASE
    )

    foreach(CompilerFlag ${CompilerFlags})
      string(REPLACE "/MD" "/MT" ${CompilerFlag} "${${CompilerFlag}}")
    endforeach()

    target_compile_definitions(corerun PRIVATE
        PLATFORM_WINDOWS
        _CRT_NONSTDC_NO_WARNINGS
        _UNICODE
        UNICODE
    )

elseif(UNIX)

    target_compile_definitions(corerun PRIVATE
        PLATFORM_LINUX
    )

    target_link_libraries(corerun PRIVATE
        libstdc++.a
    )

endif()

set_source_files_properties(vendor/miniz/miniz.c PROPERTIES LANGUAGE CXX)

target_link_libraries(corerun PUBLIC corerun_pal_static)

set_property(TARGET corerun PROPERTY CXX_STANDARD 17)
set_property(TARGET corerun PROPERTY CXX_STANDARD_REQUIRED ON)
