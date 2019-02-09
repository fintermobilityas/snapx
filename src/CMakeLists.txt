cmake_minimum_required (VERSION 3.10 FATAL_ERROR)

project(snap)

if(" ${CMAKE_SOURCE_DIR}" STREQUAL " ${CMAKE_BINARY_DIR}")
  message(FATAL_ERROR "
FATAL: In-source builds are not allowed.
       You should create a separate directory for build files.
")
endif()

set(CMAKE_CONFIGURATION_TYPES "Debug;Release" CACHE STRING "" FORCE)

set(SNAP_VERSION_MAJOR 1)
set(SNAP_VERSION_MINOR 0)
set(SNAP_VERSION_PATCH 0)
set(SNAP_VERSION ${SNAP_VERSION_MAJOR}.${SNAP_VERSION_MINOR}.${SNAP_VERSION_PATCH})

add_definitions(-DSNAP_VERSION_MAJOR=${SNAP_VERSION_MAJOR})
add_definitions(-DSNAP_VERSION_MINOR=${SNAP_VERSION_MINOR})
add_definitions(-DSNAP_VERSION_PATCH=${SNAP_VERSION_PATCH})

option(ENABLE_LTO OFF)

add_subdirectory(Snap.CoreRun)

if(WIN32)
	if (CMAKE_CXX_COMPILER_ID MATCHES "MSVC")
		set(CMAKE_CXX_FLAGS_DEBUG    "${CMAKE_CXX_FLAGS_DEBUG} /MP -DDEBUG -D_DEBUG")
		set(CMAKE_CXX_FLAGS_RELEASE  "${CMAKE_CXX_FLAGS_RELEASE} /MP -DNDEBUG")

		if(ENABLE_LTO)
		    set(CMAKE_CXX_FLAGS_RELEASE "${CMAKE_CXX_FLAGS_RELEASE} /O1 /GL")
		    set(CMAKE_EXE_LINKER_FLAGS_RELEASE "${CMAKE_EXE_LINKER_FLAGS_RELEASE} /LTCG")
		endif()	

	elseif (CMAKE_COMPILER_IS_GNUCXX)
		set(CMAKE_CXX_FLAGS_RELEASE "-O -DNDEBUG")	
		if(ENABLE_LTO)
			set(CMAKE_EXE_LINKER_FLAGS_RELEASE "${CMAKE_EXE_LINKER_FLAGS_RELEASE} -s -flto -fwhole-program -ffunction-sections -fdata-sections -Wl,--gc-sections -ffast-math")			
		endif()
	endif()
elseif(UNIX)
	if ("${CMAKE_CXX_COMPILER_ID}" STREQUAL "GNU")
		set(CMAKE_CXX_FLAGS_DEBUG "${CMAKE_CXX_FLAGS_DEBUG} -DDEBUG")
		set(CMAKE_CXX_FLAGS_RELEASE "${CMAKE_CXX_FLAGS_RELEASE} -O -DNDEBUG")
	endif()
endif()

# ========================== C/C++ options ==========================
if(CMAKE_CXX_COMPILER_VERSION)
  set(SNAP_COMPILER_STR "${CMAKE_CXX_COMPILER} ${CMAKE_CXX_COMPILER_ARG1} (ver ${CMAKE_CXX_COMPILER_VERSION})")
else()
  set(SNAP_COMPILER_STR "${CMAKE_CXX_COMPILER} ${CMAKE_CXX_COMPILER_ARG1}")
endif()
string(STRIP "${SNAP_COMPILER_STR}" SNAP_COMPILER_STR)

message(STATUS "")
message(STATUS "  Options:")
message(STATUS "    Lto enabled:"           ${ENABLE_LTO})

message(STATUS "  C/C++:")
message(STATUS "    C++ Compiler:"           ${SNAP_COMPILER_STR})
message(STATUS "    C++ flags (Debug):"      ${CMAKE_CXX_FLAGS} ${CMAKE_CXX_FLAGS_DEBUG})
message(STATUS "    C++ flags (Release):"    ${CMAKE_CXX_FLAGS} ${CMAKE_CXX_FLAGS_RELEASE})

if(WIN32 OR UNIX)
  message(STATUS "    Linker flags (Debug):"   ${CMAKE_EXE_LINKER_FLAGS} ${CMAKE_EXE_LINKER_FLAGS_DEBUG})
  message(STATUS "    Linker flags (Release):" ${CMAKE_EXE_LINKER_FLAGS} ${CMAKE_EXE_LINKER_FLAGS_RELEASE})
else()
   message(FATAL_ERROR "Target platform is not supported.")
endif()
