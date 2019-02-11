cmake_minimum_required (VERSION 3.10 FATAL_ERROR)

project(installer CXX)

list(APPEND installer_INCLUDE_DIRS
   ../Snap.CoreRun/vendor/semver
   ../Snap.CoreRun.Pal
   vendor/miniz
   vendor/guid
   vendor/coreclr
   vendor/easylogging
)

set(installer_SOURCES
   ../Snap.CoreRun/vendor/semver/semver200_comparator.cpp 
   ../Snap.CoreRun/vendor/semver/semver200_parser.cpp
   vendor/easylogging/easylogging++.cc
   vendor/miniz/miniz.c
   vendor/guid/guid.cpp
   extractor.cpp
   coreclr.cpp
)

include_directories(
   ${installer_INCLUDE_DIRS}
)

add_executable(installer 
	${installer_SOURCES}
	main.cpp
)

target_compile_definitions(installer PRIVATE 
    MINIZ_NO_STDIO
    ELPP_NO_DEFAULT_LOG_FILE
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
        GUID_WINDOWS
    )	

elseif(UNIX)

	target_compile_definitions(installer PRIVATE 
		PLATFORM_LINUX
        GUID_LIBUUID
	)	

    target_link_libraries(installer PRIVATE
        libstdc++.a
        uuid.a
    )
	
endif()

target_link_libraries(installer PUBLIC pal_static)

set_property(TARGET installer PROPERTY CXX_STANDARD 17)
set_property(TARGET installer PROPERTY CXX_STANDARD_REQUIRED ON)
