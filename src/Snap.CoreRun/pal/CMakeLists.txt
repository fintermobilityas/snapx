cmake_minimum_required (VERSION 3.10 FATAL_ERROR)

project(corerun_pal CXX)

list(APPEND corerun_pal_INCLUDE_DIRS
   pal 
)

list(APPEND corerun_pal_SOURCES
   pal_string.cpp
   pal.cpp
)

if(WIN32)
	if (CMAKE_CXX_COMPILER_ID MATCHES "MSVC")
	    list(APPEND corerun_pal_INCLUDE_DIRS
		    vendor
	    )
	    list(APPEND corerun_pal_SOURCES
		    vendor/rcedit/rcedit.cpp 
	    )
	endif()
endif()

include_directories(
   ${corerun_pal_INCLUDE_DIRS}
)

add_library(corerun_pal_static 
	${corerun_pal_SOURCES}
)

add_library(corerun_pal SHARED 
	${corerun_pal_SOURCES}
)
	
if(WIN32)
	
	if (CMAKE_COMPILER_IS_GNUCXX)
        target_compile_definitions(corerun_pal PRIVATE 
		    PLATFORM_MINGW
	    )	
        target_compile_definitions(corerun_pal_static PRIVATE 
		    PLATFORM_MINGW
	    )	
	endif()
	
	list(APPEND corerun_LIBS
		shlwapi
		shell32
	)
	
	list(APPEND corerun_DEFINES
		PLATFORM_WINDOWS
		_CRT_NONSTDC_NO_WARNINGS
		_UNICODE
		UNICODE
	)

	target_link_libraries(corerun_pal PUBLIC 
		${corerun_LIBS}
	)

	target_compile_definitions(corerun_pal PRIVATE 
		${corerun_DEFINES}
	)	
	
	target_link_libraries(corerun_pal_static PUBLIC 
		${corerun_LIBS}
	)
	
	target_compile_definitions(corerun_pal_static PRIVATE 
		${corerun_DEFINES}
	)	

	if (CMAKE_CXX_COMPILER_ID MATCHES "MSVC")
		target_compile_options(corerun_pal_static PUBLIC "/MT$<$<CONFIG:Debug>:d>")
	endif()
	
elseif(UNIX)

	list(APPEND corerun_LIBS
		dl
	)
	
	list(APPEND corerun_DEFINES
		PLATFORM_LINUX
	)
	
	target_link_libraries(corerun_pal PUBLIC
		${corerun_LIBS}
	)

	target_compile_definitions(corerun_pal PRIVATE 
		${corerun_DEFINES}
	)	
	
	target_link_libraries(corerun_pal_static PUBLIC
		${corerun_LIBS}
	)

	target_compile_definitions(corerun_pal_static PRIVATE 
		${corerun_DEFINES}
	)	
	
endif()

set_property(TARGET corerun_pal corerun_pal_static PROPERTY CXX_STANDARD 17)
set_property(TARGET corerun_pal corerun_pal_static PROPERTY CXX_STANDARD_REQUIRED ON)