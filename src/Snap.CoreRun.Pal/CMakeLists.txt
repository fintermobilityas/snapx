cmake_minimum_required (VERSION 3.10 FATAL_ERROR)

project(pal CXX)

list(APPEND pal_INCLUDE_DIRS
   pal 
)

list(APPEND pal_SOURCES
   pal_string.cpp
   pal.cpp
)

include_directories(
   ${pal_INCLUDE_DIRS}
)

add_library(pal_static 
	${pal_SOURCES}
)

add_library(pal SHARED 
	${pal_SOURCES}
)
	
if(WIN32)
	
	if (CMAKE_COMPILER_IS_GNUCXX)
        target_compile_definitions(pal PRIVATE 
		    PLATFORM_MINGW
	    )	
        target_compile_definitions(pal_static PRIVATE 
		    PLATFORM_MINGW
	    )	
	endif()
	
	list(APPEND pal_LIBS
		shlwapi
		shell32
	)
	
	list(APPEND pal_DEFINES
		PLATFORM_WINDOWS
		_CRT_NONSTDC_NO_WARNINGS
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
	endif()
	
elseif(UNIX)

	list(APPEND pal_LIBS
		dl
	)
	
	list(APPEND pal_DEFINES
		PLATFORM_LINUX
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
	
endif()

set_property(TARGET pal pal_static PROPERTY CXX_STANDARD 17)
set_property(TARGET pal pal_static PROPERTY CXX_STANDARD_REQUIRED ON)
