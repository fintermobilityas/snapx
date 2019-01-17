#include "pal.hpp"
#include "pathcch.h"

#if PLATFORM_WINDOWS
#include <Shlwapi.h>
#endif

#include <vector>
#include <cwchar>

/*++
Function:
PAL_IsDebuggerPresent
Abstract:
This function should be used to determine if a debugger is attached to the process.
--*/
PALEXPORT BOOL PALAPI pal_isdebuggerpresent()
{
#if PLATFORM_WINDOWS
    return IsDebuggerPresent();
#elif defined(__linux__)
    BOOL debugger_present = FALSE;
    char buf[2048];

    int status_fd = open("/proc/self/status", O_RDONLY);
    if (status_fd == -1)
    {
        return FALSE;
    }
    ssize_t num_read = read(status_fd, buf, sizeof(buf) - 1);

    if (num_read > 0)
    {
        static const char TracerPid[] = "TracerPid:";
        char *tracer_pid;

        buf[num_read] = '\0';
        tracer_pid = strstr(buf, TracerPid);
        if (tracer_pid)
        {
            debugger_present = !!atoi(tracer_pid + sizeof(TracerPid) - 1);
        }
    }

    close(status_fd);

    return debugger_present;
#elif defined(__APPLE__)
    struct kinfo_proc info = {};
    size_t size = sizeof(info);
    int mib[4] = { CTL_KERN, KERN_PROC, KERN_PROC_PID, getpid() };
    int ret = sysctl(mib, sizeof(mib) / sizeof(*mib), &info, &size, NULL, 0);

    if (ret == 0)
        return ((info.kp_proc.p_flag & P_TRACED) != 0);

    return FALSE;
#elif defined(__NetBSD__)
    int traced;
    kvm_t *kd;
    int cnt;

    struct kinfo_proc *info;

    kd = kvm_open(NULL, NULL, NULL, KVM_NO_FILES, "kvm_open");
    if (kd == NULL)
        return FALSE;

    info = kvm_getprocs(kd, KERN_PROC_PID, getpid(), &cnt);
    if (info == NULL || cnt < 1)
    {
        kvm_close(kd);
        return FALSE;
    }

    traced = info->kp_proc.p_slflag & PSL_TRACED;
    kvm_close(kd);

    if (traced != 0)
        return TRUE;
    else
        return FALSE;
#else
    return FALSE;
#endif
}

PALEXPORT BOOL PALAPI pal_ends_with_case_sensitive(const wchar_t * src, const wchar_t * value)
{
    const auto diff = wcslen(src) - wcslen(value);
    return diff > 0 && 0 == wcscmp(&src[diff], value);
}

PALEXPORT BOOL PALAPI pal_ends_with_case_insensitive(const wchar_t * src, const wchar_t * value)
{
    const auto diff = wcslen(src) - wcslen(value);
    return diff > 0 && 0 == _wcsicmp(&src[diff], value);
}

PALEXPORT BOOL PALAPI pal_get_directory_name_full_path(const wchar_t* path_in, wchar_t** path_out, size_t* path_out_len)
{
#if PLATFORM_WINDOWS
    if (path_in == nullptr)
    {
        return FALSE;
    }

    wchar_t path_in_without_filespec[MAX_PATH];
    wcscpy_s(path_in_without_filespec, MAX_PATH, path_in);

    if (PathIsDirectory(path_in))
    {
        if (S_OK != PathCchCanonicalize(path_in_without_filespec, MAX_PATH, path_in))
        {
            return FALSE;
        }
    }
    else if (S_OK != ::PathCchRemoveFileSpec(path_in_without_filespec, MAX_PATH))
    {
        return FALSE;
    }

    if (0 == wcscmp(path_in_without_filespec, L""))
    {
        return FALSE;
    }

    *path_out_len = wcslen(path_in_without_filespec) + 1;
    *path_out = new wchar_t[*path_out_len];
    wcscpy_s(*path_out, *path_out_len, path_in_without_filespec);

    return TRUE;
#else
#error "TODO: IMPLEMENT ME"
#endif
}

PALEXPORT BOOL PALAPI pal_get_directory_name(const wchar_t * path_in, wchar_t ** path_out, size_t * path_out_len)
{
    if (!pal_get_directory_name_full_path(path_in, path_out, path_out_len)) {
        return FALSE;
    }

    const auto directory_name = wcsrchr(*path_out, PAL_DIRECTORY_SEPARATOR_C);

    *path_out_len = wcslen(directory_name) + 1;
    *path_out = new wchar_t[*path_out_len];
    wcsncpy_s(*path_out, *path_out_len - 1, &directory_name[1], *path_out_len);

    return TRUE;
}

PALEXPORT BOOL PALAPI pal_path_combine(const wchar_t * path_in_lhs, const wchar_t * path_in_rhs, wchar_t ** path_out, size_t * path_out_len)
{
#if PLATFORM_WINDOWS
    if (path_in_lhs == nullptr
        || path_in_rhs == nullptr)
    {
        return FALSE;
    }

    wchar_t path_combined[MAX_PATH];

    if (S_OK != PathCchCombine(path_combined, MAX_PATH, path_in_lhs, path_in_rhs))
    {
        return FALSE;
    }

    const auto path_combined_len = wcslen(path_combined) + 1;
    *path_out = new wchar_t[path_combined_len];
    wcscpy_s(*path_out, path_combined_len, path_combined);

    return TRUE;
#else
#error "TODO: IMPLEMENT ME"
#endif
}

PALEXPORT BOOL PALAPI pal_list_directories(const wchar_t * path_in, wchar_t *** paths_out, size_t* paths_out_len)
{
#if PLATFORM_WINDOWS
    wchar_t* path_root = nullptr;
    size_t path_root_len = 0;

    if (!pal_get_directory_name_full_path(path_in, &path_root, &path_root_len))
    {
        return FALSE;
    }

    const auto path_root_search_pattern = new wchar_t[path_root_len];
    wcscpy_s(path_root_search_pattern, path_root_len, path_root);

    if (!pal_ends_with_case_insensitive(path_root_search_pattern, PAL_DIRECTORY_SEPARATOR_STR))
    {
        PathCchAppend(path_root_search_pattern, MAX_PATH, PAL_DIRECTORY_SEPARATOR_STR);
    }

    PathCchAppend(path_root_search_pattern, MAX_PATH, L"*");

    WIN32_FIND_DATA file_info;
    const auto h_file = FindFirstFile(path_root_search_pattern, &file_info);
    if (h_file == INVALID_HANDLE_VALUE) {
        return FALSE;
    }

    std::vector<wchar_t*> directories;

    do
    {
        if (!(file_info.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY)) {
            continue;
        }

        auto path_root_this_directory = new wchar_t[MAX_PATH];

        if (0 == _wcsicmp(file_info.cFileName, L".")
            || 0 == _wcsicmp(file_info.cFileName, L".."))
        {
            continue;
        }

        if (S_OK != PathCchCombine(path_root_this_directory, MAX_PATH, path_root, file_info.cFileName))
        {
            continue;
        }

        directories.emplace_back(path_root_this_directory);

    } while (FindNextFile(h_file, &file_info));

    *paths_out_len = directories.size();

    const auto paths = new wchar_t*[*paths_out_len];

    for (auto i = 0u; i < *paths_out_len; i++)
    {
        const auto directory = directories[i];
        paths[i] = directory;
    }

    *paths_out = paths;

    ::FindClose(h_file);

    return TRUE;
#else
#error "TODO: IMPLEMENT ME"
#endif
}

PALEXPORT BOOL PALAPI pal_get_environment_variable(const wchar_t * environment_variable_in, wchar_t ** environment_variable_value_out)
{
#if PLATFORM_WINDOWS

    const auto buffer_size = 65535;
    wchar_t buffer[buffer_size];

    const auto actual_buffer_size = GetEnvironmentVariable(environment_variable_in, buffer, buffer_size);
    if (actual_buffer_size == 0) {
        return FALSE;
    }

    auto environment_variable_value_out_length = static_cast<size_t>(actual_buffer_size + 1);
    *environment_variable_value_out = new wchar_t[environment_variable_value_out_length];
    wcscpy_s(*environment_variable_value_out, environment_variable_value_out_length, buffer);

    if (!pal_get_directory_name_full_path(*environment_variable_value_out,
        environment_variable_value_out, &environment_variable_value_out_length))
    {
        delete *environment_variable_value_out;
        *environment_variable_value_out = nullptr;
        return FALSE;
    }

    return TRUE;
#else
#error "TODO: IMPLEMENT ME"
#endif
}

PALEXPORT BOOL PALAPI pal_get_environment_variable_bool(const wchar_t * environment_variable_in, bool* value)
{
    wchar_t* environment_variable_value_out = nullptr;
    if (!pal_get_environment_variable(environment_variable_in, &environment_variable_value_out))
    {
        return FALSE;
    }

    *value = std::wcscmp(environment_variable_value_out, L"1") == 0
        || _wcsicmp(environment_variable_value_out, L"true") == 0 ?
        "true" : "false";

    return TRUE;
}

PALEXPORT void PALAPI pal_convert_from_utf16_to_utf8(const wchar_t* widechar_string_in, const int widechar_string_in_len, char** multibyte_string_out)
{
    if (widechar_string_in == nullptr)
    {
        return;
    }

    const auto required_size = ::WideCharToMultiByte(CP_UTF8, 0, widechar_string_in, widechar_string_in_len,
        nullptr, 0, nullptr, nullptr);

    *multibyte_string_out = new char[required_size + 1];

    ::WideCharToMultiByte(CP_UTF8, 0, widechar_string_in, widechar_string_in_len,
        *multibyte_string_out, required_size, nullptr, nullptr);
    multibyte_string_out[required_size] = nullptr;
}

PALEXPORT void PALAPI pal_convert_from_utf8_to_utf16(const char* multibyte_string_in, wchar_t** widechar_string_out)
{
    if (multibyte_string_in == nullptr)
    {
        return;
    }

    auto mbstate = std::mbstate_t();

    size_t widechar_string_out_len = 0; // Including '\0' - the terminating zero
    mbsrtowcs_s(&widechar_string_out_len, nullptr, 0, &multibyte_string_in, 0, &mbstate);

    std::wstring widechar_string;
    widechar_string.resize(widechar_string_out_len);

    mbsrtowcs_s(&widechar_string_out_len,
        widechar_string.data(),
        widechar_string.size(), // The character count to write (excl. '\0')
        &multibyte_string_in,
        widechar_string.size(), // The character count to convert
        &mbstate);
}
