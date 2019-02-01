cmake_minimum_required (VERSION 3.10 FATAL_ERROR)

project(corerun CXX)

list(APPEND corerun_INCLUDE_DIRS
   vendor
   vendor/easyloggingpp/src
   pal 
)

set(corerun_SOURCES
   coreclr.cpp
   corerun.cpp
   stubexecutable.cpp
   vendor/easyloggingpp/src/easylogging++.cc
   vendor/semver/semver200_comparator.cpp 
   vendor/semver/semver200_parser.cpp
   pal/pal_string.cpp
   pal/pal.cpp
)

include_directories(
   ${corerun_INCLUDE_DIRS}
)

add_executable(corerun 
	${corerun_SOURCES}
	main.cpp
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
	endif()

	target_link_libraries(corerun PRIVATE 
		shlwapi
		shell32
	)

	set(CompilerFlags
		CMAKE_CXX_FLAGS
		CMAKE_CXX_FLAGS_DEBUG
		CMAKE_CXX_FLAGS_RELEASE
		CMAKE_C_FLAGS
		CMAKE_C_FLAGS_DEBUG
		CMAKE_C_FLAGS_RELEASE
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

	target_link_libraries(corerun PRIVATE
		dl
	)

	target_compile_definitions(corerun PRIVATE 
		PLATFORM_LINUX
	)	

	if(${CMAKE_SYSTEM_PROCESSOR} MATCHES "aarch64") 
		target_compile_definitions(corerun PRIVATE 
			PLATFORM_LINUX_AARCH64
		)	
	endif()
	
endif()

set_property(TARGET corerun PROPERTY CXX_STANDARD 17)
set_property(TARGET corerun PROPERTY CXX_STANDARD_REQUIRED ON)
