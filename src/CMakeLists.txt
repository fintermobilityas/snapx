cmake_minimum_required (VERSION 3.10 FATAL_ERROR)

project(snap)

if(" ${CMAKE_SOURCE_DIR}" STREQUAL " ${CMAKE_BINARY_DIR}")
  message(FATAL_ERROR "
FATAL: In-source builds are not allowed.
       You should create a separate directory for build files.
")
endif()

set(CMAKE_CONFIGURATION_TYPES "Debug;Release" CACHE STRING "" FORCE)

option(BUILD_ENABLE_LTO "Enable lto support" OFF)
option(BUILD_ENABLE_TESTS "Build with tests enabled" ON)

add_subdirectory(Snap.CoreRun.Pal)
add_subdirectory(Snap.CoreRun)

if (BUILD_ENABLE_TESTS)

  add_subdirectory(Vendor/gtest)

  enable_testing()

  include_directories(
    ${gtest_SOURCE_DIR}/include
    ${gtest_SOURCE_DIR}
    Vendor
    Vendor/json/include
  )

  set(tests_SOURCES 
    Vendor/crossguid/Guid.cpp
    Snap.CoreRun.Pal/test/anyos.cpp
  	Snap.CoreRun/test/anyos.cpp
  )

  if(WIN32)
    list(APPEND tests_SOURCES
        Snap.CoreRun.Pal/test/windows.cpp
    )
    list(APPEND tests_DEFINES		
	    GUID_WINDOWS
    )
    list(APPEND tests_LIBS
	    ole32
    )
  elseif(UNIX)
    list(APPEND tests_SOURCES
        Snap.CoreRun.Pal/test/unix.cpp
    )
    list(APPEND tests_DEFINES		
	    GUID_LIBUUID
    )
    list(APPEND tests_LIBS
	    uuid
    )
  endif()

  add_executable(Snap.Tests ${tests_SOURCES})

  target_compile_definitions(Snap.Tests PRIVATE 
    ${tests_DEFINES}
  )	

  target_link_libraries(Snap.Tests PRIVATE
                  ${tests_LIBS}
				  corerun_static
				  gtest
                  gtest_main)

  add_custom_command(TARGET Snap.Tests POST_BUILD
          COMMAND ${CMAKE_COMMAND} -E echo
          "Tests directory = $<TARGET_FILE_DIR:Snap.Tests>")

  add_custom_command(TARGET Snap.Tests PRE_BUILD 
    COMMAND ${CMAKE_COMMAND} -E copy "$<TARGET_FILE:corerun_demoapp>" "$<TARGET_FILE_DIR:Snap.Tests>"
    COMMENT "Copying corerun_demoapp to tests directory"
  )

  add_custom_command(TARGET Snap.Tests PRE_BUILD 
    COMMAND ${CMAKE_COMMAND} -E copy "$<TARGET_FILE:corerun>" "$<TARGET_FILE_DIR:Snap.Tests>"
    COMMENT "Copying corerun to tests directory"
  )

  set_property(TARGET Snap.Tests PROPERTY CXX_STANDARD 17)
  set_property(TARGET Snap.Tests PROPERTY CXX_STANDARD_REQUIRED ON)

endif()

if(WIN32)
	if (CMAKE_CXX_COMPILER_ID MATCHES "MSVC")
		set(CMAKE_CXX_FLAGS_DEBUG    "${CMAKE_CXX_FLAGS_DEBUG} /MP -DDEBUG -D_DEBUG")
		set(CMAKE_CXX_FLAGS_RELEASE  "${CMAKE_CXX_FLAGS_RELEASE} /MP -DNDEBUG")

		if(BUILD_ENABLE_LTO)
		    set(CMAKE_CXX_FLAGS_RELEASE "${CMAKE_CXX_FLAGS_RELEASE} /O1 /GL")
		    set(CMAKE_EXE_LINKER_FLAGS_RELEASE "${CMAKE_EXE_LINKER_FLAGS_RELEASE} /LTCG")
		endif()	

	elseif (CMAKE_COMPILER_IS_GNUCXX)
		set(CMAKE_CXX_FLAGS_DEBUG "-g -DDEBUG")	
		set(CMAKE_CXX_FLAGS_RELEASE "-O -DNDEBUG")	
		if(BUILD_ENABLE_LTO)
			set(CMAKE_EXE_LINKER_FLAGS_RELEASE "${CMAKE_EXE_LINKER_FLAGS_RELEASE} -s -flto -fwhole-program -ffunction-sections -fdata-sections -Wl,--gc-sections -ffast-math")			
		endif()
	endif()
elseif(UNIX)
	if ("${CMAKE_CXX_COMPILER_ID}" STREQUAL "GNU")
		set(CMAKE_CXX_FLAGS_DEBUG "${CMAKE_CXX_FLAGS_DEBUG} -g -DDEBUG")
		set(CMAKE_CXX_FLAGS_RELEASE "${CMAKE_CXX_FLAGS_RELEASE} -O -DNDEBUG")
	endif()
endif()

if(CMAKE_CXX_COMPILER_VERSION)
  set(SNAP_COMPILER_STR "${CMAKE_CXX_COMPILER} ${CMAKE_CXX_COMPILER_ARG1} (ver ${CMAKE_CXX_COMPILER_VERSION})")
else()
  set(SNAP_COMPILER_STR "${CMAKE_CXX_COMPILER} ${CMAKE_CXX_COMPILER_ARG1}")
endif()
string(STRIP "${SNAP_COMPILER_STR}" SNAP_COMPILER_STR)

message(STATUS "")
message(STATUS "  Options:")
message(STATUS "    Lto: "           ${BUILD_ENABLE_LTO})
message(STATUS "    Tests: "		 ${BUILD_ENABLE_TESTS})

message(STATUS "  C/C++:")
message(STATUS "    C++ Compiler: "           ${SNAP_COMPILER_STR})
message(STATUS "    C++ flags (Debug): "      ${CMAKE_CXX_FLAGS} ${CMAKE_CXX_FLAGS_DEBUG})
message(STATUS "    C++ flags (Release): "    ${CMAKE_CXX_FLAGS} ${CMAKE_CXX_FLAGS_RELEASE})

if(WIN32 OR UNIX)
  message(STATUS "    Linker flags (Debug):"   ${CMAKE_EXE_LINKER_FLAGS} ${CMAKE_EXE_LINKER_FLAGS_DEBUG})
  message(STATUS "    Linker flags (Release):" ${CMAKE_EXE_LINKER_FLAGS} ${CMAKE_EXE_LINKER_FLAGS_RELEASE})
else()
   message(FATAL_ERROR "Target platform is not supported.")
endif()
