#pragma once

// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

#ifdef PLATFORM_WINDOWS
#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#endif
#endif

#ifndef FORCEINLINE
#if _MSC_VER < 1200
#define FORCEINLINE inline
#else
#define FORCEINLINE __forceinline
#endif
#endif

#ifdef PLATFORM_WINDOWS
#define PALIMPORT   __declspec(dllimport)
#define PALEXPORT   __declspec(dllexport)
#define PALAPI      __cdecl
#define PAL_DIRECTORY_SEPARATOR_STR L"\\"
#define PAL_DIRECTORY_SEPARATOR_C L'\\'
#define PAL_MAX_PATH MAX_PATH
#else
#error Unsupported platform
#endif

#ifdef __cplusplus
extern "C" {
#endif

    // - Primitives
    typedef int BOOL;

    // - Callbacks
    typedef BOOL(*pal_list_files_filter_callback_t)(const wchar_t* filename);

    // - Generic
    PALEXPORT BOOL PALAPI pal_isdebuggerpresent(void);

    // - Environment

    PALEXPORT BOOL PALAPI pal_env_get_variable(const wchar_t* environment_variable_in, wchar_t** environment_variable_value_out);
    PALEXPORT BOOL PALAPI pal_env_get_variable_bool(const wchar_t* environment_variable_in, BOOL* env_value_bool_out);

    // - Filesystem

    PALEXPORT BOOL PALAPI pal_fs_get_directory_name_full_path(const wchar_t* path_in, wchar_t** path_out);
    PALEXPORT BOOL PALAPI pal_fs_get_directory_name(const wchar_t* path_in, wchar_t** path_out);
    PALEXPORT BOOL PALAPI pal_fs_path_combine(const wchar_t* path_in_lhs, const wchar_t* path_in_rhs, wchar_t** path_out);
    PALEXPORT BOOL PALAPI pal_fs_list_directories(const wchar_t* path_in, wchar_t*** paths_out, size_t* paths_out_len);
    PALEXPORT BOOL PALAPI pal_fs_list_files(const wchar_t* path_in, const pal_list_files_filter_callback_t filter_callback_in, const wchar_t* extension_filter_in, wchar_t*** files_out, size_t* files_out_len);
    PALEXPORT BOOL PALAPI pal_fs_file_exists(const wchar_t* file_path_in, BOOL* file_exists_bool_out);
    PALEXPORT BOOL PALAPI pal_fs_get_current_directory(wchar_t** current_directory_out);

    // - String

    PALEXPORT void PALAPI pal_str_from_utf16_to_utf8(const wchar_t* widechar_string_in, const int widechar_string_in_len, char** multibyte_string_out);
    PALEXPORT void PALAPI pal_str_from_utf8_to_utf16(const char* multibyte_string_in, wchar_t** widechar_string_out);
    PALEXPORT BOOL PALAPI pal_str_to_lower_case(const wchar_t* widechar_string_in, wchar_t** widechar_string_out);
    PALEXPORT BOOL PALAPI pal_str_endswith(const wchar_t* src, const wchar_t* suffix);
    PALEXPORT BOOL PALAPI pal_str_endswithi(const wchar_t* src, const wchar_t* suffix);

#ifdef __cplusplus
}
#endif
