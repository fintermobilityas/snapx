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

add_executable(corerun_demoapp
    src/main_demoapp.cpp
)

add_library(corerun_static 
	${corerun_SOURCES}
)

target_include_directories(corerun_demoapp PUBLIC
	../Vendor/json/include
)

target_link_libraries(corerun_demoapp PUBLIC
	pal_static
)

target_include_directories(corerun_static PUBLIC 
    vendor
    src
)

target_link_libraries(corerun_static PUBLIC
	pal_static
)

target_link_libraries(corerun PUBLIC pal_static)

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

elseif(UNIX)

    target_link_libraries(corerun PUBLIC
        libstdc++.a
    )

endif()

set_property(TARGET corerun corerun_static corerun_demoapp PROPERTY CXX_STANDARD 17)
set_property(TARGET corerun corerun_static corerun_demoapp PROPERTY CXX_STANDARD_REQUIRED ON)
