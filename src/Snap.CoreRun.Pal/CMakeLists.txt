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
)

list(APPEND pal_SOURCES
   src/pal_string.cpp
   src/pal.cpp
)

if(WIN32)
	list(APPEND pal_INCLUDE_DIRS
		src/vendor
	)
	list(APPEND pal_SOURCES
		src/vendor/rcedit/rcedit.cpp 
	)
endif()

include_directories(
   ${pal_INCLUDE_DIRS}
)

add_library(pal_static 
	${pal_SOURCES}
)

add_library(pal SHARED 
	${pal_SOURCES}
)
	
target_include_directories(pal PUBLIC
    src/include
)

target_include_directories(pal_static PUBLIC
    src/include
)

if(WIN32)
	
	if (CMAKE_COMPILER_IS_GNUCXX)
        target_compile_definitions(pal PUBLIC 
		    PAL_PLATFORM_MINGW
	    )	
        target_compile_definitions(pal_static PUBLIC 
		    PAL_PLATFORM_MINGW
	    )	
		target_link_libraries(pal_static PUBLIC
			-static-libgcc 
			-static-libstdc++
		)
	endif()

    target_compile_definitions(pal PUBLIC 
		PAL_PLATFORM_WINDOWS
	)	

    target_compile_definitions(pal_static PUBLIC 
		PAL_PLATFORM_WINDOWS
	)	
	
	list(APPEND pal_LIBS
		shlwapi
		shell32
        kernel32
	)
	
	list(APPEND pal_DEFINES		
		_UNICODE
		UNICODE
	)

	target_link_libraries(pal PUBLIC 
		${pal_LIBS}
	)

	target_compile_definitions(pal PRIVATE 
		${pal_DEFINES}
	)	
	
	target_link_libraries(pal_static PUBLIC 
		${pal_LIBS}
	)
	
	target_compile_definitions(pal_static PRIVATE 
		${pal_DEFINES}
	)	

	if (CMAKE_CXX_COMPILER_ID MATCHES "MSVC")
		target_compile_options(pal_static PUBLIC "/MT$<$<CONFIG:Debug>:d>")
        pal_mitigate_dll_hijacking_for_targets(pal)
	endif()
	
elseif(UNIX)

	list(APPEND pal_LIBS
		dl
	)
	
	list(APPEND pal_DEFINES
		
	)
	
    target_compile_definitions(pal PUBLIC 
	    PAL_PLATFORM_LINUX
    )	

	target_link_libraries(pal PUBLIC
		${pal_LIBS}
	)

	target_compile_definitions(pal PRIVATE 
		${pal_DEFINES}
	)	
	
	target_link_libraries(pal_static PUBLIC
		${pal_LIBS}
        libstdc++.a
	)

    target_compile_definitions(pal_static PUBLIC 
		PAL_PLATFORM_LINUX
	)	

	target_compile_definitions(pal_static PRIVATE 
		${pal_DEFINES}
	)	
	
endif()

set_property(TARGET pal pal_static PROPERTY CXX_STANDARD 17)
set_property(TARGET pal pal_static PROPERTY CXX_STANDARD_REQUIRED ON)
