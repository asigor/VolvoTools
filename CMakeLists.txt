cmake_minimum_required(VERSION 3.16)
project(VolvoTools VERSION 0.0.1 LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

add_subdirectory (j2534)
add_subdirectory (Registry)
add_subdirectory (Common)
add_subdirectory (Logger)
add_subdirectory (Flasher)
add_subdirectory (argparse)
add_subdirectory (libintelhex)
add_subdirectory (VolvoFlasher)
add_subdirectory (VolvoLogger)

include(InstallRequiredSystemLibraries)

set(CPACK_PACKAGE_VENDOR "VolvoTools")
set(CPACK_PACKAGE_CONTACT "prometey1982")
set(CPACK_PACKAGE_DESCRIPTION_SUMMARY "Volvo ECU/CEM flashing and logging utilities")
set(CPACK_PACKAGE_NAME "VolvoTools")
set(CPACK_PACKAGE_VERSION ${PROJECT_VERSION})
set(CPACK_RESOURCE_FILE_LICENSE "${CMAKE_CURRENT_SOURCE_DIR}/README.md")
set(CPACK_RESOURCE_FILE_README "${CMAKE_CURRENT_SOURCE_DIR}/README.md")

if(CMAKE_SIZEOF_VOID_P EQUAL 8)
    set(VOLVOTOOLS_ARCH_SUFFIX "x64")
else()
    set(VOLVOTOOLS_ARCH_SUFFIX "x86")
endif()

set(CPACK_PACKAGE_FILE_NAME "VolvoTools-${PROJECT_VERSION}-${VOLVOTOOLS_ARCH_SUFFIX}")

if(WIN32)
    set(CPACK_GENERATOR "NSIS")
    set(CPACK_NSIS_DISPLAY_NAME "VolvoTools")
    set(CPACK_NSIS_PACKAGE_NAME "VolvoTools")
    set(CPACK_NSIS_CONTACT "prometey1982")
    set(CPACK_NSIS_ENABLE_UNINSTALL_BEFORE_INSTALL ON)
    set(CPACK_PACKAGE_INSTALL_DIRECTORY "VolvoTools")
    set(CPACK_NSIS_MODIFY_PATH OFF)
endif()

set(CPACK_PACKAGE_EXECUTABLES
    "VolvoFlasher" "Volvo Flasher"
    "VolvoLogger" "Volvo Logger"
)

include(CPack)
