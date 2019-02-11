cmake_minimum_required (VERSION 3.10 FATAL_ERROR)

project(installer CXX)

list(APPEND installer_INCLUDE_DIRS
   ../Snap.CoreRun/vendor/easyloggingpp/src
   ../Snap.CoreRun.Pal
)

set(installer_SOURCES
   installer.cpp
   ../Snap.CoreRun/vendor/easyloggingpp/src/easylogging++.cc
)

include_directories(
   ${installer_INCLUDE_DIRS}
)

add_executable(installer 
	${installer_SOURCES}
	main.cpp
)

if(WIN32)
	
	if (CMAKE_CXX_COMPILER_ID MATCHES "MSVC")
		set(CMAKE_EXE_LINKER_FLAGS "${CMAKE_EXE_LINKER_FLAGS} /subsystem:windows")		
		set(CMAKE_CREATE_CONSOLE_EXE "/subsystem:windows") # Override subsystem on Windows.
	elseif (CMAKE_COMPILER_IS_GNUCXX)
		set(CMAKE_EXE_LINKER_FLAGS "${CMAKE_EXE_LINKER_FLAGS} -mwindows -municode -Wl,-subsystem,windows")	
        target_compile_definitions(installer PRIVATE 
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
	
	target_compile_definitions(installer PRIVATE 
		PLATFORM_WINDOWS
		_CRT_NONSTDC_NO_WARNINGS
		_UNICODE
		UNICODE
	)	

elseif(UNIX)

	target_compile_definitions(installer PRIVATE 
		PLATFORM_LINUX
	)	
	
endif()

target_link_libraries(installer PUBLIC pal_static)

set_property(TARGET installer PROPERTY CXX_STANDARD 17)
set_property(TARGET installer PROPERTY CXX_STANDARD_REQUIRED ON)
