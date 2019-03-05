cmake_minimum_required (VERSION 3.10 FATAL_ERROR)

project(corerun_tests CXX)

set(corerun_tests_SOURCES 
    ${GTEST_ALL_CPP_FILENAME}
    ../Vendor/crossguid/Guid.cpp
    ../Snap.CoreRun.Pal/test/anyos.cpp
    ../Snap.CoreRun/test/anyos.cpp
    src/main.cpp
)

list(APPEND corerun_tests_INCLUDE_DIRS_VENDOR
    ${gtest_SOURCE_DIR}/include
    ${gtest_SOURCE_DIR}
    ../Vendor
    ../Vendor/json/include
)

set_source_files_properties(Vendor/crossguid/Guid.cpp PROPERTIES COMPILE_FLAGS -w)

if(WIN32)
    list(APPEND corerun_tests_SOURCES
        ../Snap.CoreRun.Pal/test/windows.cpp
    )
    if (CMAKE_COMPILER_IS_GNUCXX)
        set(CMAKE_RC_COMPILER_INIT windres)
        ENABLE_LANGUAGE(RC)        
        list(APPEND corerun_tests_SOURCES
           src/resources_mingw.rc
        )
    endif()
    list(APPEND corerun_tests_DEFINES		
	    GUID_WINDOWS
    )
    list(APPEND corerun_tests_LIBS
	    ole32
        kernel32
        ntdll
    )
elseif(UNIX)
    list(APPEND corerun_tests_SOURCES
        ../Snap.CoreRun.Pal/test/unix.cpp
    )
    list(APPEND corerun_tests_DEFINES		
	    GUID_LIBUUID
    )
    list(APPEND corerun_tests_LIBS
	    uuid
    )
endif()

add_executable(corerun_tests ${corerun_tests_SOURCES})
  
include_directories(corerun_tests SYSTEM
    ${corerun_tests_INCLUDE_DIRS_VENDOR}
)

target_include_directories(corerun_tests PRIVATE
    src/include
    ${corerun_tests_INCLUDE_DIRS_VENDOR}
)

target_compile_definitions(corerun_tests PRIVATE 
    ${corerun_tests_DEFINES}
)	

target_link_libraries(corerun_tests PRIVATE
    ${corerun_tests_LIBS}
	corerun_static
	gtest
    gtest_main)

add_custom_target(always_copy_test_dependencies ALL
    COMMAND ${CMAKE_COMMAND} -E echo "Copying tests assetst to directory: $<TARGET_FILE_DIR:corerun_tests>"
    COMMAND ${CMAKE_COMMAND} -E copy "${CMAKE_SOURCE_DIR}/Snap.CoreRun/src/resources/CoreRun.ico" "$<TARGET_FILE_DIR:corerun_tests>/test.ico"
    COMMAND ${CMAKE_COMMAND} -E copy "$<TARGET_FILE:corerun_demoapp>" "$<TARGET_FILE_DIR:corerun_tests>"
    COMMAND ${CMAKE_COMMAND} -E copy "$<TARGET_FILE:corerun>" "$<TARGET_FILE_DIR:corerun_tests>"
)

add_custom_command(TARGET corerun_tests PRE_BUILD
        DEPENDS always_copy_test_dependencies
        COMMAND ${CMAKE_COMMAND} -E echo )

if(WIN32)
    if (CMAKE_CXX_COMPILER_ID MATCHES "MSVC")
        add_custom_command(
            TARGET corerun_tests 
            POST_BUILD
            COMMAND "mt.exe" -manifest \"${CMAKE_CURRENT_SOURCE_DIR}\\corerun_tests.exe.manifest\" 
                -inputresource:\"$<TARGET_FILE:corerun_tests>\" -outputresource:\"$<TARGET_FILE:corerun_tests>\"
            COMMENT "Adding corerun_tests manifest" 
        )	
    endif()
endif()

set_property(TARGET corerun_tests PROPERTY CXX_STANDARD 17)
set_property(TARGET corerun_tests PROPERTY CXX_STANDARD_REQUIRED ON)
