#pragma once

#include "pal_string.hpp"

#ifdef PLATFORM_WINDOWS
#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#endif
#endif

#define PAL_UNUSED(x) (void)(x)

#ifdef PLATFORM_WINDOWS
#define PAL_MAX_PATH MAX_PATH 
#define PAL_DIRECTORY_SEPARATOR_STR "\\"
#define PAL_DIRECTORY_SEPARATOR_WIDE_STR L"\\"
#define PAL_DIRECTORY_SEPARATOR_C '\\'
#define PAL_CORECLR_TPA_SEPARATOR_STR ";"
#define PAL_CORECLR_TPA_SEPARATOR_C ';'
#define PAL_API __declspec( dllexport )
#define PAL_CALLING_CONVENTION __cdecl
#elif PLATFORM_LINUX
#include <limits.h>
#define PAL_MAX_PATH PATH_MAX
#define TRUE 1
#define FALSE 0
#define PAL_DIRECTORY_SEPARATOR_STR "/"
#define PAL_DIRECTORY_SEPARATOR_C '/'
#define PAL_CORECLR_TPA_SEPARATOR_STR ":"
#define PAL_CORECLR_TPA_SEPARATOR_C ':'
#if defined(__GNUC__)
#define PAL_API __attribute__((visibility("default")))
#define PAL_CALLING_CONVENTION 
#else
#define PAL_API 
#define PAL_CALLING_CONVENTION
#endif
#else
#error Unsupported platform
#endif

#ifdef __cplusplus
extern "C" {
#endif

// - Primitives
typedef int BOOL;
typedef FILE pal_file_handle_t;

// - Callbacks
typedef BOOL(*pal_fs_list_filter_callback_t)(const char* filename);

// - Generic
PAL_API BOOL PAL_CALLING_CONVENTION pal_isdebuggerpresent(void);
PAL_API BOOL PAL_CALLING_CONVENTION pal_load_library(const char* name_in, BOOL pinning_required, void** instance_out);
PAL_API BOOL PAL_CALLING_CONVENTION pal_free_library(void* instance_in);
PAL_API BOOL PAL_CALLING_CONVENTION pal_getprocaddress(void* instance_in, const char* name_in, void** ptr_out);
PAL_API BOOL PAL_CALLING_CONVENTION pal_is_elevated();

// - Environment
PAL_API BOOL PAL_CALLING_CONVENTION pal_env_get_variable(const char* environment_variable_in, char** environment_variable_value_out);
PAL_API BOOL PAL_CALLING_CONVENTION pal_env_get_variable_bool(const char* environment_variable_in);
PAL_API BOOL PAL_CALLING_CONVENTION pal_env_expand_str(const char* environment_in, char** environment_out);

// - Filesystem
PAL_API BOOL PAL_CALLING_CONVENTION pal_fs_chmod(const char* path_in, int mode);
PAL_API BOOL PAL_CALLING_CONVENTION pal_fs_get_directory_name_absolute_path(const char* path_in, char** path_out);
PAL_API BOOL PAL_CALLING_CONVENTION pal_fs_get_directory_name(const char* path_in, char** path_out);
PAL_API BOOL PAL_CALLING_CONVENTION pal_fs_path_combine(const char* path_in_lhs, const char* path_in_rhs, char** path_out);
PAL_API BOOL PAL_CALLING_CONVENTION pal_fs_list_directories(const char* path_in, pal_fs_list_filter_callback_t filter_callback_in,
        const char* filter_extension_in, char*** directories_out, size_t* directories_out_len);
PAL_API BOOL PAL_CALLING_CONVENTION pal_fs_list_files(const char* path_in, pal_fs_list_filter_callback_t filter_callback_in,
        const char* filter_extension_in, char*** files_out, size_t* files_out_len);
PAL_API BOOL PAL_CALLING_CONVENTION pal_fs_file_exists(const char* file_path_in);
PAL_API BOOL PAL_CALLING_CONVENTION pal_fs_get_cwd(char** working_directory_out);
PAL_API BOOL PAL_CALLING_CONVENTION pal_fs_get_own_executable_name(char** own_executable_name_out);
PAL_API BOOL PAL_CALLING_CONVENTION pal_fs_get_absolute_path(const char* path_in, char** path_absolute_out);
PAL_API BOOL PAL_CALLING_CONVENTION pal_fs_directory_exists(const char* path_in);
PAL_API BOOL PAL_CALLING_CONVENTION pal_fs_get_file_size(const char* filename_in, int* file_size_out);
PAL_API BOOL PAL_CALLING_CONVENTION pal_fs_read_file(const char* filename_in, const char* mode_in, char** bytes_out, int* bytes_read_out);
PAL_API BOOL PAL_CALLING_CONVENTION pal_fs_mkdir(const char* directory_in, int mode_in);
PAL_API BOOL PAL_CALLING_CONVENTION pal_fs_fopen(const char* filename_in, const char* mode_in, pal_file_handle_t** file_handle_out);
PAL_API BOOL PAL_CALLING_CONVENTION pal_fs_fwrite(pal_file_handle_t* pal_file_handle_in, void* data_in, size_t data_len_in);
PAL_API BOOL PAL_CALLING_CONVENTION pal_fs_fclose(pal_file_handle_t*& pal_file_handle_in);
PAL_API BOOL PAL_CALLING_CONVENTION pal_fs_write(const char* filename_in, const char* mode_in, void* data_in, size_t data_len_in);

// - String
PAL_API BOOL PAL_CALLING_CONVENTION pal_str_endswith(const char* src, const char* str);
PAL_API BOOL PAL_CALLING_CONVENTION pal_str_startswith(const char* src, const char* str);
PAL_API BOOL PAL_CALLING_CONVENTION pal_str_iequals(const char* lhs, const char* rhs);

PAL_API BOOL PAL_CALLING_CONVENTION pal_rc_is_snap_aware(const char* filename_in);
PAL_API BOOL PAL_CALLING_CONVENTION pal_rc_set_snap_aware(const char* filename_in);

#ifdef __cplusplus
}
#endif
