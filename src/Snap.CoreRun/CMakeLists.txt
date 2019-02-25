cmake_minimum_required (VERSION 3.10 FATAL_ERROR)

project(corerun CXX)

list(APPEND corerun_INCLUDE_DIRS
   vendor
)

set(corerun_SOURCES
   src/corerun.hpp
   src/stubexecutable.cpp
   src/vendor/semver/semver200_comparator.cpp 
   src/vendor/semver/semver200_parser.cpp
)

include_directories(
   ${corerun_INCLUDE_DIRS}
)

add_executable(corerun 
	${corerun_SOURCES}
	src/main.cpp
)

if(WIN32)
	
	if (CMAKE_CXX_COMPILER_ID MATCHES "MSVC")
		set(CMAKE_EXE_LINKER_FLAGS "${CMAKE_EXE_LINKER_FLAGS} /subsystem:windows")		
		set(CMAKE_CREATE_CONSOLE_EXE "/subsystem:windows") # Override subsystem on Windows.
	elseif (CMAKE_COMPILER_IS_GNUCXX)
		set(CMAKE_EXE_LINKER_FLAGS "${CMAKE_EXE_LINKER_FLAGS} -mwindows -municode -Wl,-subsystem,windows")	
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
		_CRT_NONSTDC_NO_WARNINGS
		_UNICODE
		UNICODE
	)	

elseif(UNIX)

    target_link_libraries(corerun PRIVATE
        libstdc++.a
    )
	
endif()

target_link_libraries(corerun PUBLIC pal_static)

set_property(TARGET corerun PROPERTY CXX_STANDARD 17)
set_property(TARGET corerun PROPERTY CXX_STANDARD_REQUIRED ON)
