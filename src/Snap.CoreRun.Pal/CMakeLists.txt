cmake_minimum_required (VERSION 3.10 FATAL_ERROR)

project(pal CXX)

# https://github.com/Squirrel/Squirrel.Windows/pull/1444/files
function(pal_mitigate_dll_hijacking_for_targets targets)
    if(WIN32)
        if (CMAKE_CXX_COMPILER_ID MATCHES "MSVC")
            set(PAL_WIN32_DELAYLOAD_DLLS "user32.dll;advapi32.dll;shell32.dll;ole32.dll;oleaut32.dll;urlmon.dll")
            set_property(TARGET ${targets} PROPERTY LINK_FLAGS "/DELAYLOAD:${PAL_WIN32_DELAYLOAD_DLLS}")
            message(STATUS "Dll mitigation enabled for targets: ${targets}")
        elseif (CMAKE_COMPILER_IS_GNUCXX)
            message(WARNING "Dll mitigation disabled. Does anyone know how this could be achieved when targeting mingw? Then please submit a pull request on Github :)")
        endif()
    else()
        message(FATAL_ERROR "Dll hijack mitigation is only supported on Windows")
    endif()
endfunction()

list(APPEND pal_INCLUDE_DIRS
        src/include
        ../Vendor/plog/include
        )

list(APPEND pal_SOURCES
        src/pal_string.cpp
        src/pal_module.cpp
        src/pal_semaphore.cpp
        src/pal.cpp
        )

list(APPEND pal_DEFINES
        UNICODE
        _UNICODE
        PAL_LOGGING_ENABLED
        )
list(APPEND pal_static_LIBS)

if(WIN32)
    list(APPEND pal_INCLUDE_DIRS
            src/vendor
            )
    list(APPEND pal_SOURCES
            src/vendor/rcedit/rcedit.cpp
            )
endif()

add_library(pal_static
        ${pal_SOURCES}
        )

add_library(pal SHARED
        ${pal_SOURCES}
        )

if(WIN32)

    if (CMAKE_COMPILER_IS_GNUCXX)
        list(APPEND pal_DEFINES
                PAL_PLATFORM_MINGW
                )
        list(APPEND pal_static_LIBS
                -static-libgcc
                -static-libstdc++
                )
    endif()

    list(APPEND pal_DEFINES
            PAL_PLATFORM_WINDOWS
            _UNICODE
            UNICODE
            )

    list(APPEND pal_LIBS
            shlwapi
            shell32
            kernel32
            )

    if (CMAKE_CXX_COMPILER_ID MATCHES "MSVC")
        target_compile_options(pal_static PUBLIC "/MT$<$<CONFIG:Debug>:d>")
    endif()

    pal_mitigate_dll_hijacking_for_targets(pal)

elseif(UNIX)

    list(APPEND pal_LIBS
            dl
            pthread
            )

    list(APPEND pal_static_LIBS
            libstdc++.a
            )

    list(APPEND pal_DEFINES
            PAL_PLATFORM_LINUX
            )

endif()

foreach(cmake_target IN ITEMS pal pal_static)
    target_include_directories(${cmake_target} PUBLIC ${pal_INCLUDE_DIRS})
    target_compile_definitions(${cmake_target} PUBLIC ${pal_DEFINES})
    target_link_libraries(${cmake_target} PUBLIC ${pal_LIBS})
    set_property(TARGET ${cmake_target} PROPERTY CXX_STANDARD 17)
    set_property(TARGET ${cmake_target} PROPERTY CXX_STANDARD_REQUIRED ON)
endforeach(cmake_target)

target_link_libraries(pal_static PUBLIC ${pal_static_LIBS})
