#pragma once

#ifdef PAL_PLATFORM_WINDOWS
#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#define STRICT
#include <windows.h>
#endif
#endif

#ifndef PAL_UNUSED
#define PAL_UNUSED(x) (void)(x)
#endif

#ifndef TRUE
#define TRUE 1
#endif
#ifndef FALSE
#define FALSE 0
#endif

#ifdef PAL_PLATFORM_WINDOWS
#define PAL_MAX_PATH MAX_PATH 
#define PAL_MAX_PATH_UNICODE (1 << 15) // https://docs.microsoft.com/en-gb/windows/desktop/FileIO/naming-a-file#maximum-path-length-limitation
#define PAL_DIRECTORY_SEPARATOR_STR "\\"
#define PAL_DIRECTORY_SEPARATOR_WIDE_STR L"\\"
#define PAL_DIRECTORY_SEPARATOR_C '\\'
#define PAL_CORECLR_TPA_SEPARATOR_STR ";"
#define PAL_CORECLR_TPA_SEPARATOR_C ';'
#define PAL_API __declspec( dllexport )
#define PAL_CALLING_CONVENTION __cdecl
#elif PAL_PLATFORM_LINUX
#include <limits.h>
#include <cstdio>
#include <wait.h>
#include <cstdint>
#include <sys/stat.h> // mode_t
#define PAL_MAX_PATH PATH_MAX
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

#include "pal_string.hpp"
#include "pal_module.hpp"

#include <plog/Log.h>

#ifdef __cplusplus
extern "C" {
#endif

// - Primitives
typedef int BOOL;
typedef FILE pal_file_handle_t;

#if defined(PAL_PLATFORM_WINDOWS) 
typedef DWORD pal_pid_t;
typedef int pal_mode_t;
typedef DWORD pal_exit_code_t;
#elif defined(PAL_PLATFORM_LINUX)
typedef pid_t pal_pid_t;
typedef mode_t pal_mode_t;
typedef int pal_exit_code_t;
#endif

// - Callbacks

typedef BOOL(*pal_fs_list_filter_callback_t)(const char* filename);

// - Generic

PAL_API BOOL PAL_CALLING_CONVENTION pal_isdebuggerpresent();
PAL_API BOOL PAL_CALLING_CONVENTION pal_mitigate_dll_hijacking();
PAL_API BOOL PAL_CALLING_CONVENTION pal_wait_for_debugger();
PAL_API BOOL PAL_CALLING_CONVENTION pal_load_library(const char* name_in, BOOL pinning_required, void** instance_out);
PAL_API BOOL PAL_CALLING_CONVENTION pal_free_library(void* instance_in);
PAL_API BOOL PAL_CALLING_CONVENTION pal_getprocaddress(void* instance_in, const char* name_in, void** ptr_out);
PAL_API BOOL PAL_CALLING_CONVENTION pal_is_elevated();
PAL_API BOOL PAL_CALLING_CONVENTION pal_set_icon(const char* filename_in, const char* icon_filename_in);
PAL_API BOOL PAL_CALLING_CONVENTION pal_has_icon(const char * filename_in);
PAL_API BOOL PAL_CALLING_CONVENTION pal_process_get_real_path(char **real_path_out);
PAL_API BOOL PAL_CALLING_CONVENTION pal_process_get_cwd(char **cwd_out);
PAL_API BOOL PAL_CALLING_CONVENTION pal_process_is_running(pal_pid_t pid);
PAL_API BOOL PAL_CALLING_CONVENTION pal_process_kill(pal_pid_t pid);
PAL_API BOOL PAL_CALLING_CONVENTION pal_process_get_pid(pal_pid_t* pid_out);
PAL_API BOOL PAL_CALLING_CONVENTION pal_process_get_name(char **exe_name_out);
PAL_API BOOL PAL_CALLING_CONVENTION pal_process_exec(const char *filename_in, const char *working_dir_in,
                                                     int argc_in, char **argv_in, pal_exit_code_t *exit_code_out);
PAL_API BOOL PAL_CALLING_CONVENTION pal_process_daemonize(const char *filename_in, const char *working_dir_in, int argc_in,
                                                          char **argv_in,
                                                          int cmd_show_in /* Only applicable on Windows */,
                                                          pal_pid_t *pid_out);
PAL_API BOOL PAL_CALLING_CONVENTION pal_sleep_ms(uint32_t milliseconds);
PAL_API BOOL PAL_CALLING_CONVENTION pal_is_windows();
PAL_API BOOL PAL_CALLING_CONVENTION pal_is_windows_8_or_greater();
PAL_API BOOL PAL_CALLING_CONVENTION pal_is_windows_7_or_greater();
PAL_API BOOL PAL_CALLING_CONVENTION pal_is_linux();
PAL_API BOOL PAL_CALLING_CONVENTION pal_is_unknown_os();

// - Environment

PAL_API BOOL PAL_CALLING_CONVENTION pal_env_set(const char* name_in, const char* value_in);
PAL_API BOOL PAL_CALLING_CONVENTION pal_env_get(const char* environment_variable_in, char** environment_variable_value_out);
PAL_API BOOL PAL_CALLING_CONVENTION pal_env_get_bool(const char* environment_variable_in);
PAL_API BOOL PAL_CALLING_CONVENTION pal_env_expand_str(const char* environment_in, char** environment_out);

// - Filesystem

PAL_API BOOL PAL_CALLING_CONVENTION pal_fs_chmod(const char* path_in, pal_mode_t mode);
PAL_API BOOL PAL_CALLING_CONVENTION pal_fs_list_directories(const char* path_in, pal_fs_list_filter_callback_t filter_callback_in,
        const char* filter_extension_in, char*** directories_out, size_t* directories_out_len);
PAL_API BOOL PAL_CALLING_CONVENTION pal_fs_list_files(const char* path_in, pal_fs_list_filter_callback_t filter_callback_in,
        const char* filter_extension_in, char*** files_out, size_t* files_out_len);
PAL_API BOOL PAL_CALLING_CONVENTION pal_fs_file_exists(const char* file_path_in);
PAL_API BOOL PAL_CALLING_CONVENTION pal_fs_get_cwd(char** working_directory_out);
PAL_API BOOL PAL_CALLING_CONVENTION pal_fs_directory_exists(const char* path_in);
PAL_API BOOL PAL_CALLING_CONVENTION pal_fs_get_file_size(const char* filename_in, size_t* file_size_out);
PAL_API BOOL PAL_CALLING_CONVENTION pal_fs_read_binary_file(const char *filename_in, char **bytes_out, size_t *bytes_read_out);
PAL_API BOOL PAL_CALLING_CONVENTION pal_fs_mkdir(const char* directory_in, pal_mode_t mode_in);
PAL_API BOOL PAL_CALLING_CONVENTION pal_fs_mkdirp(const char *directory_in, pal_mode_t mode_in);
PAL_API BOOL PAL_CALLING_CONVENTION pal_fs_rmdir(const char* directory_in, BOOL recursive);
PAL_API BOOL PAL_CALLING_CONVENTION pal_fs_rmfile(const char* filename_in);
PAL_API BOOL PAL_CALLING_CONVENTION pal_fs_fopen(const char* filename_in, const char* mode_in, pal_file_handle_t** file_handle_out);
PAL_API BOOL PAL_CALLING_CONVENTION pal_fs_fwrite(pal_file_handle_t* pal_file_handle_in, const char* data_in, size_t data_len_in);
PAL_API BOOL PAL_CALLING_CONVENTION pal_fs_fclose(pal_file_handle_t*& pal_file_handle_in);
PAL_API BOOL PAL_CALLING_CONVENTION pal_fs_write(const char* filename_in, const char* mode_in, const char* data_in, size_t data_len_in);

// - Path
PAL_API BOOL PAL_CALLING_CONVENTION pal_path_normalize(const char* path_in, char** path_normalized_out);
PAL_API BOOL PAL_CALLING_CONVENTION pal_path_get_directory_name(const char* path_in, char** path_out);
PAL_API BOOL PAL_CALLING_CONVENTION pal_path_get_directory_name_from_file_path(const char * path_in, char ** path_out);
PAL_API BOOL PAL_CALLING_CONVENTION pal_path_combine(const char* path1, const char* path2, char** path_out);

// - String

PAL_API BOOL PAL_CALLING_CONVENTION pal_str_endswith(const char* src, const char* str);
PAL_API BOOL PAL_CALLING_CONVENTION pal_str_startswith(const char* src, const char* str);
PAL_API BOOL PAL_CALLING_CONVENTION pal_str_iequals(const char* lhs, const char* rhs);
PAL_API BOOL PAL_CALLING_CONVENTION pal_str_is_null_or_whitespace(const char* str);

#ifdef __cplusplus
}
#endif
