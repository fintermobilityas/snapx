#include "pal.hpp"

#if PLATFORM_WINDOWS
#include <Shlwapi.h> // PathIsDirectory, PathFileExists etc.
#include <PathCch.h> // PathCchCombine etc.
#endif

#include <vector>
#include <regex>
#include <codecvt>
#include <string>

/*++
Function:
PAL_IsDebuggerPresent
Abstract:
This function should be used to determine if a debugger is attached to the process.
--*/

// - Generic
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

// - Environment
PALEXPORT BOOL PALAPI pal_env_get_variable(const wchar_t * environment_variable_in, wchar_t ** environment_variable_value_out)
{
#if PLATFORM_WINDOWS
    wchar_t* buffer = nullptr;
    size_t buffer_len = 0;
    const auto error = _wdupenv_s(&buffer, &buffer_len, environment_variable_in);
    if (error || buffer_len <= 0)
    {
        return FALSE;
    }

    *environment_variable_value_out = new wchar_t[buffer_len];
    wcscpy_s(*environment_variable_value_out, buffer_len, buffer);

    return TRUE;
#else
    const auto environment_variable_in_s = pal_str_narrow(environment_variable_in);
    const auto environment_variable_value_out_s = std::getenv(environment_variable_in_s);

    delete environment_variable_in_s;

    if (environment_variable_value_out_s == nullptr)
    {
        return FALSE;
    }

    const auto environment_variable_value_out_w = pal_str_widen(environment_variable_value_out_s);
    const auto environment_variable_value_out_w_len = wcslen(environment_variable_value_out_w) + 1;
    *environment_variable_value_out = new wchar_t[environment_variable_value_out_w_len];
    wcscpy_s(environment_variable_value_out_w, environment_variable_value_out_w_len, environment_variable_value_out_w);

    return TRUE;
#endif
}

PALEXPORT BOOL PALAPI pal_env_get_variable_bool(const wchar_t * environment_variable_in, BOOL* env_value_bool_out)
{
    wchar_t* environment_variable_value_out = nullptr;
    if (!pal_env_get_variable(environment_variable_in, &environment_variable_value_out))
    {
        return FALSE;
    }

    *env_value_bool_out = std::wcscmp(environment_variable_value_out, L"1") == 0
        || _wcsicmp(environment_variable_value_out, L"true") == 0 ?
        TRUE : FALSE;

    return TRUE;
}

PALEXPORT BOOL PALAPI pal_env_expand_str(const wchar_t * environment_in, wchar_t ** environment_out)
{
    std::wstring environment_in_str(environment_in);

#if PLATFORM_WINDOWS
    static std::wregex expression(LR"(%([0-9A-Za-z\\/]*)%)", std::regex_constants::icase);
#else
    static std::wregex expression(LR"(\$\{([^}]+)\})", std::regex_constants::icase);
#endif

    auto replacements = 0;
    std::wsmatch match;
    while (std::regex_search(environment_in_str, match, expression)) {
        wchar_t* environment_variable_value = nullptr;
#if PLATFORM_WINDOWS
        const auto text = match[1].str();
#else
        const auto text = match[1].str();
#endif

        if (!pal_env_get_variable(text.c_str(), &environment_variable_value))
        {
            continue;
        }

        const std::wstring environment_variable_value_wstring(environment_variable_value);
        delete environment_variable_value;

        environment_in_str.replace(match[0].first, match[0].second, environment_variable_value_wstring);

        replacements++;
    }

    const auto environment_in_str_len = environment_in_str.size() + 1;
    *environment_out = new wchar_t[environment_in_str_len];
    wcscpy_s(*environment_out, environment_in_str_len, environment_in_str.c_str());

    return replacements > 0 ? TRUE : FALSE;
}

// - Filesystem
PALEXPORT BOOL PALAPI pal_fs_get_directory_name_full_path(const wchar_t* path_in, wchar_t** path_out)
{
#if PLATFORM_WINDOWS
    if (path_in == nullptr)
    {
        return FALSE;
    }

    wchar_t path_in_without_filespec[PAL_MAX_PATH];
    wcscpy_s(path_in_without_filespec, PAL_MAX_PATH, path_in);

    if (PathIsDirectory(path_in))
    {
        if (S_OK != PathCchCanonicalize(path_in_without_filespec, PAL_MAX_PATH, path_in))
        {
            return FALSE;
        }
    }
    else if (S_OK != ::PathCchRemoveFileSpec(path_in_without_filespec, PAL_MAX_PATH))
    {
        return FALSE;
    }

    if (0 == wcscmp(path_in_without_filespec, L""))
    {
        return FALSE;
    }

    const auto path_out_len = wcslen(path_in_without_filespec) + 1;
    *path_out = new wchar_t[path_out_len];
    wcscpy_s(*path_out, path_out_len, path_in_without_filespec);

    return TRUE;
#else
    return FALSE;
#endif
}

PALEXPORT BOOL PALAPI pal_fs_get_directory_name(const wchar_t * path_in, wchar_t ** path_out)
{
    if (!pal_fs_get_directory_name_full_path(path_in, path_out)) {
        return FALSE;
    }

    const auto directory_name = wcsrchr(*path_out, PAL_DIRECTORY_SEPARATOR_C);

    const auto path_out_len = wcslen(directory_name) + 1;
    *path_out = new wchar_t[path_out_len];
    wcsncpy_s(*path_out, path_out_len - 1, &directory_name[1], path_out_len);

    return TRUE;
}

PALEXPORT BOOL PALAPI pal_fs_path_combine(const wchar_t * path_in_lhs, const wchar_t * path_in_rhs, wchar_t ** path_out)
{
#if PLATFORM_WINDOWS
    if (path_in_lhs == nullptr
        || path_in_rhs == nullptr)
    {
        return FALSE;
    }

    wchar_t path_combined[PAL_MAX_PATH];

    if (S_OK != PathCchCombine(path_combined, PAL_MAX_PATH, path_in_lhs, path_in_rhs))
    {
        return FALSE;
    }

    const auto path_combined_len = wcslen(path_combined) + 1;
    *path_out = new wchar_t[path_combined_len];
    wcscpy_s(*path_out, path_combined_len, path_combined);

    return TRUE;
#else
    return FALSE;
#endif
}

PALEXPORT BOOL PALAPI pal_fs_list_directories(const wchar_t * path_in, wchar_t *** paths_out, size_t* paths_out_len)
{
#if PLATFORM_WINDOWS
    wchar_t* path_root = nullptr;
    if (!pal_fs_get_directory_name_full_path(path_in, &path_root))
    {
        return FALSE;
    }

    const auto path_root_search_pattern_len = wcslen(path_root) + 1;
    const auto path_root_search_pattern = new wchar_t[path_root_search_pattern_len];
    wcscpy_s(path_root_search_pattern, path_root_search_pattern_len, path_root);

    if (!pal_str_endswithi(path_root_search_pattern, PAL_DIRECTORY_SEPARATOR_STR))
    {
        PathCchAppend(path_root_search_pattern, PAL_MAX_PATH, PAL_DIRECTORY_SEPARATOR_STR);
    }

    PathCchAppend(path_root_search_pattern, PAL_MAX_PATH, L"*");

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

        if (0 == _wcsicmp(file_info.cFileName, L".")
            || 0 == _wcsicmp(file_info.cFileName, L".."))
        {
            continue;
        }

        auto path_root_this_directory = new wchar_t[PAL_MAX_PATH];
        if (!pal_fs_path_combine(path_root, file_info.cFileName, &path_root_this_directory))
        {
            delete[] path_root_this_directory;
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
    return FALSE;
#endif
}

PALEXPORT BOOL PALAPI pal_fs_list_files(const wchar_t * path_in, const pal_list_files_filter_callback_t filter_callback_in,
    const wchar_t* extension_filter_in, wchar_t *** files_out, size_t * files_out_len)
{
#if PLATFORM_WINDOWS
    if (path_in == nullptr)
    {
        return false;
    }

    wchar_t* path_root = nullptr;
    if (!pal_fs_get_directory_name_full_path(path_in, &path_root))
    {
        return FALSE;
    }

    const auto path_root_search_pattern_len = wcslen(path_root) + 1;
    const auto path_root_search_pattern = new wchar_t[path_root_search_pattern_len];
    wcscpy_s(path_root_search_pattern, path_root_search_pattern_len, path_root);

    if (!pal_str_endswithi(path_root_search_pattern, PAL_DIRECTORY_SEPARATOR_STR))
    {
        PathCchAppend(path_root_search_pattern, PAL_MAX_PATH, PAL_DIRECTORY_SEPARATOR_STR);
    }

    PathCchAppend(path_root_search_pattern, PAL_MAX_PATH, extension_filter_in != nullptr ? extension_filter_in : L"*");

    WIN32_FIND_DATA file;
    const auto h_file = FindFirstFile(path_root_search_pattern, &file);
    if (h_file == INVALID_HANDLE_VALUE) {
        return FALSE;
    }

    std::vector<wchar_t*> filenames;

    do
    {
        if (file.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) {
            continue;
        }

        if (0 == _wcsicmp(file.cFileName, L".")
            || 0 == _wcsicmp(file.cFileName, L".."))
        {
            continue;
        }

        auto path_root_this_directory = new wchar_t[PAL_MAX_PATH];
        if (!pal_fs_path_combine(path_root, file.cFileName, &path_root_this_directory))
        {
            delete[] path_root_this_directory;
            continue;
        }

        const auto filter_callback_fn = filter_callback_in;
        if (filter_callback_fn != nullptr
            && !filter_callback_fn(path_root_this_directory))
        {
            delete[] path_root_this_directory;
            continue;
        }

        filenames.emplace_back(path_root_this_directory);

    } while (FindNextFile(h_file, &file));

    *files_out_len = filenames.size();

    const auto files_array = new wchar_t*[*files_out_len];

    for (auto i = 0u; i < *files_out_len; i++)
    {
        const auto filename = filenames[i];
        files_array[i] = filename;
    }

    *files_out = files_array;

    ::FindClose(h_file);

    return TRUE;
#else
    return FALSE;
#endif
}

PALEXPORT BOOL PALAPI pal_fs_file_exists(const wchar_t * file_path_in, BOOL *file_exists_bool_out)
{
#if PLATFORM_WINDOWS
    if (file_path_in == nullptr)
    {
        return FALSE;
    }

    *file_exists_bool_out = PathFileExists(file_path_in);

    return TRUE;
#else
    return FALSE;
#endif
}

PALEXPORT BOOL PALAPI pal_fs_get_current_directory(wchar_t ** current_directory_out)
{
#if PLATFORM_WINDOWS
    if (current_directory_out == nullptr)
    {
        return FALSE;
    }

    const auto h_module = GetModuleHandle(nullptr);
    if (h_module == nullptr)
    {
        return FALSE;
    }

    const auto filename = new wchar_t[PAL_MAX_PATH];
    if (0 == GetModuleFileName(h_module, filename, PAL_MAX_PATH))
    {
        return FALSE;
    }

    if (!pal_fs_get_directory_name_full_path(filename, current_directory_out))
    {
        delete[] filename;
        return FALSE;
    }

    delete[] filename;

    return TRUE;
#else
    return FALSE;
#endif
}

PALEXPORT BOOL PALAPI pal_fs_get_own_executable_name(wchar_t ** own_executable_name_out)
{
#if PLATFORM_WINDOWS

    const auto current_directory = new wchar_t[PAL_MAX_PATH];

    GetModuleFileName(GetModuleHandle(nullptr), current_directory, PAL_MAX_PATH);
    const auto last_slash = wcsrchr(current_directory, PAL_DIRECTORY_SEPARATOR_C);

    if (!last_slash) {
        delete[] current_directory;
        return FALSE;
    }

    *own_executable_name_out = _wcsdup(last_slash + 1);
    delete[] current_directory;

    return TRUE;
#else
    return FALSE;
#endif
}

// - String
PALEXPORT void PALAPI pal_str_from_utf16_to_utf8(const wchar_t* widechar_string_in, char** multibyte_string_out)
{
    if (widechar_string_in == nullptr)
    {
        return;
    }

    const auto widechar_string_in_len = static_cast<int>(wcslen(widechar_string_in) + 1);

    const auto required_size = ::WideCharToMultiByte(CP_UTF8, 0, widechar_string_in, widechar_string_in_len,
        nullptr, 0, nullptr, nullptr);

    *multibyte_string_out = new char[required_size];

    ::WideCharToMultiByte(CP_UTF8, 0, widechar_string_in, widechar_string_in_len,
        *multibyte_string_out, required_size, nullptr, nullptr);
}

PALEXPORT void PALAPI pal_str_from_utf8_to_utf16(const char* multibyte_string_in, wchar_t** widechar_string_out)
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

    wcscpy_s(*widechar_string_out, widechar_string.size(), widechar_string.c_str());
}

PALEXPORT BOOL PALAPI pal_str_to_lower_case(const wchar_t * widechar_string_in, wchar_t ** widechar_string_out)
{
    if (widechar_string_in == nullptr)
    {
        return FALSE;
    }
    *widechar_string_out = _wcsdup(widechar_string_in);
    _wcslwr_s(*widechar_string_out, wcslen(widechar_string_in));
    return TRUE;
}

PALEXPORT BOOL PALAPI pal_str_endswith(const wchar_t * src, const wchar_t * suffix)
{
    if (src == nullptr || suffix == nullptr)
    {
        return FALSE;
    }

    const auto diff = wcslen(src) - wcslen(suffix);
    return diff > 0 && 0 == wcscmp(&src[diff], suffix);
}

PALEXPORT BOOL PALAPI pal_str_endswithi(const wchar_t * src, const wchar_t * suffix)
{
    if (src == nullptr || suffix == nullptr)
    {
        return FALSE;
    }

    const auto diff = wcslen(src) - wcslen(suffix);
    return diff > 0 && 0 == _wcsicmp(&src[diff], suffix);
}

PALEXPORT BOOL PALAPI pal_str_startswith(const wchar_t * src, const wchar_t * suffix)
{
    if (src == nullptr || suffix == nullptr)
    {
        return FALSE;
    }

    const auto pos = wcsncmp(src, suffix, wcslen(suffix));
    return pos == 0 ? TRUE : FALSE;
}

PALEXPORT BOOL PALAPI pal_str_startswithi(const wchar_t * src, const wchar_t * suffix)
{
    if (src == nullptr || suffix == nullptr)
    {
        return FALSE;
    }

    const auto pos = _wcsnicmp(src, suffix, wcslen(suffix));
    return pos == 0 ? TRUE : FALSE;
}

PALEXPORT char* PALAPI pal_str_narrow(const wchar_t * src)
{
    char* narrow = nullptr;
    pal_str_from_utf16_to_utf8(src, &narrow);

    return narrow;
}

PALEXPORT wchar_t* PALAPI pal_str_widen(const char * src)
{
    wchar_t* widen = nullptr;
    pal_str_from_utf8_to_utf16(src, &widen);

    return widen;
}
