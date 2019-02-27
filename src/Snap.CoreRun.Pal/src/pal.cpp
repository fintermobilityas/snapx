#include "pal/pal.hpp"
#include <cassert>

#if _MSC_VER
#pragma warning( push )
#pragma warning( disable : 4068)
#endif

#pragma clang diagnostic push
#pragma clang diagnostic ignored "-Wunknown-pragmas"
#pragma clang diagnostic ignored "-Wnonportable-include-path"
#pragma ide diagnostic ignored "OCUnusedGlobalDeclarationInspection"

#if _MSC_VER
#pragma warning( pop )
#endif

#if PAL_PLATFORM_WINDOWS
#include <shlwapi.h> // PathIsDirectory, PathFileExists
#include <strsafe.h> // StringCchLengthA
#include <cctype> // toupper
#include <direct.h> // mkdir
#include <wchar.h>
#include "vendor/rcedit/rcedit.hpp"
#elif PAL_PLATFORM_LINUX
#include <sys/stat.h> // stat
#include <sys/types.h> // O_RDONLY
#include <unistd.h> // getcwd
#include <fcntl.h> // open
#include <dirent.h> // opendir
#include <libgen.h> // dirname
#include <dlfcn.h> // dlopen
#include <signal.h> // kill

static const char* symlink_entrypoint_executable = "/proc/self/exe";
#endif

#include <regex>

// - Generic
PAL_API BOOL PAL_CALLING_CONVENTION pal_isdebuggerpresent()
{
#if PAL_PLATFORM_WINDOWS
    return ::IsDebuggerPresent() ? TRUE : FALSE;
#elif PAL_PLATFORM_LINUX
    // https://github.com/dotnet/coreclr/blob/4a6753dcacf44df6a8e91b91029e4b7a4f12d917/src/pal/src/init/pal.cpp#L821
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
            debugger_present = std::stoi(tracer_pid + sizeof(TracerPid) - 1) != 0;
        }
    }

    close(status_fd);

    return debugger_present;
#else
    return FALSE;
#endif
}

PAL_API BOOL PAL_CALLING_CONVENTION pal_wait_for_debugger(void)
{
    while (!pal_isdebuggerpresent())
    {
        pal_usleep(100);
    }

    return TRUE;
}

PAL_API BOOL PAL_CALLING_CONVENTION pal_load_library(const char * name_in, BOOL pinning_required, void** instance_out)
{
    if (name_in == nullptr)
    {
        return FALSE;
    }

#if PAL_PLATFORM_WINDOWS
    pal_utf16_string name_in_utf16_string(name_in);

    auto h_module = LoadLibraryEx(name_in_utf16_string.data(), nullptr, 0);
    if (!h_module)
    {
        return FALSE;
    }

    if (pinning_required)
    {
        HMODULE dummy_module;
        if (!::GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_PIN, name_in_utf16_string.data(), &dummy_module)) {
            pal_free_library(h_module);
            return FALSE;
        }
    }

    *instance_out = static_cast<void*>(h_module);

    return TRUE;

#elif PAL_PLATFORM_LINUX
    PAL_UNUSED(pinning_required);

    auto instance = dlopen(name_in, RTLD_NOW | RTLD_LOCAL);
    if (!instance)
    {
        return FALSE;
    }

    *instance_out = instance;
    return TRUE;
#else
    return FALSE;
#endif
}

PAL_API BOOL PAL_CALLING_CONVENTION pal_free_library(void* instance_in)
{
    if (instance_in == nullptr)
    {
        return FALSE;
    }
#if PAL_PLATFORM_WINDOWS
    auto free_library_result = FreeLibrary(static_cast<HMODULE>(instance_in));
    if (free_library_result == 0)
    {
        return TRUE;
    }
    return FALSE;
#elif PAL_PLATFORM_LINUX
    return 0 == dlclose(instance_in) ? TRUE : FALSE;
#else
    return FALSE;
#endif
}

PAL_API BOOL PAL_CALLING_CONVENTION pal_getprocaddress(void* instance_in, const char* name_in, void** ptr_out)
{
    if (instance_in == nullptr)
    {
        return FALSE;
    }

#if PAL_PLATFORM_WINDOWS

    auto h_module = static_cast<HMODULE>(instance_in);
    auto h_module_ptr_out = ::GetProcAddress(h_module, name_in);
    if (h_module_ptr_out == nullptr)
    {
        return FALSE;
    }

    *ptr_out = reinterpret_cast<void*>(h_module_ptr_out);
    return TRUE;
#elif PAL_PLATFORM_LINUX
    auto dlsym_ptr_out = dlsym(instance_in, name_in);
    if (dlerror() != nullptr)
    {
        return FALSE;
    }

    *ptr_out = dlsym_ptr_out;

    return TRUE;
#else
    return FALSE;
#endif
}

PAL_API BOOL PAL_CALLING_CONVENTION pal_is_elevated() {
    BOOL is_elevated;
#if PAL_PLATFORM_WINDOWS
    // https://docs.microsoft.com/en-us/windows/desktop/api/securitybaseapi/nf-securitybaseapi-checktokenmembership
    SID_IDENTIFIER_AUTHORITY nt_authority = SECURITY_NT_AUTHORITY;
    PSID administrators_group;
    is_elevated = AllocateAndInitializeSid(
        &nt_authority,
        2,
        SECURITY_BUILTIN_DOMAIN_RID,
        DOMAIN_ALIAS_RID_ADMINS,
        0, 0, 0, 0, 0, 0,
        &administrators_group);
    if (is_elevated)
    {
        if (!CheckTokenMembership(nullptr, administrators_group, &is_elevated))
        {
            is_elevated = FALSE;
        }
        FreeSid(administrators_group);
    }
#elif PAL_PLATFORM_LINUX
    auto uid = getuid();
    const auto euid = geteuid();
    is_elevated = uid < 0 || uid != euid ? TRUE : FALSE;
#endif
    return is_elevated;
}

PAL_API BOOL PAL_CALLING_CONVENTION pal_set_icon(char * filename_in, char * icon_filename_in)
{
    if (!pal_fs_file_exists(filename_in)
        || !pal_fs_file_exists(icon_filename_in))
    {
        return FALSE;
    }

#if PAL_PLATFORM_WINDOWS || PAL_PLATFORM_MINGW
    pal_utf16_string filename_in_utf16_string(filename_in);
    pal_utf16_string icon_filename_in_utf16_string(icon_filename_in);
    snap::rcedit::ResourceUpdater resourceUpdater;
    if (!resourceUpdater.Load(filename_in_utf16_string.data()))
    {
        return FALSE;
    }
    if (!resourceUpdater.SetIcon(icon_filename_in_utf16_string.data()))
    {
        return FALSE;
    }
    if (!resourceUpdater.Commit())
    {
        return FALSE;
    }
    return TRUE;
#else
    return FALSE;
#endif
}

PAL_API BOOL PAL_CALLING_CONVENTION pal_process_is_running(pal_pid_t pid)
{
    if (pid < 0)
    {
        return FALSE;
    }

    BOOL process_exists = FALSE;
#if PAL_PLATFORM_WINDOWS
    auto process = OpenProcess(SYNCHRONIZE, FALSE, pid);
    if (process != nullptr)
    {
        assert(0 != CloseHandle(process));
        process_exists = TRUE;
    }
#elif PAL_PLATFORM_LINUX
    struct stat dontcare = { 0 };
    std::string proc_path("/proc/" + std::to_string(pid));
    if (stat(proc_path.c_str(), &dontcare) != -1)
    {
        process_exists = TRUE;
    }
#endif
    return process_exists;
}

PAL_API BOOL PAL_CALLING_CONVENTION pal_process_kill(pal_pid_t pid)
{
    if (pid < 0)
    {
        return FALSE;
    }

    BOOL process_killed;
#if PAL_PLATFORM_WINDOWS
    auto process = OpenProcess(SYNCHRONIZE, FALSE, pid);
    if (process != nullptr)
    {
        process_killed = TerminateProcess(process, 1);
        assert(0 != CloseHandle(process));
    }
    return process_killed;
#elif PAL_PLATFORM_LINUX
    auto result = kill(pid, SIGTERM);
    process_killed = result == 0;
    return process_killed;
#else
    return FALSE;
#endif
}

PAL_API BOOL PAL_CALLING_CONVENTION pal_process_get_pid(pal_pid_t* pid_out)
{
    BOOL has_pid;
#if PAL_PLATFORM_WINDOWS
    has_pid = TRUE;
    *pid_out = GetCurrentProcessId();
#elif PAL_PLATFORM_LINUX
    has_pid = TRUE;
    *pid_out = getpid();
#endif
    return has_pid;
}

PAL_API BOOL PAL_CALLING_CONVENTION pal_process_exec(const char *filename_in, const char *working_dir_in, 
    const int argc_in, char **argv_in, int *exit_code_out)
{
    if (filename_in == nullptr
        || working_dir_in == nullptr)
    {
        return FALSE;
    }

#if PAL_PLATFORM_WINDOWS
    auto cmd_line = std::string("\"");
    cmd_line += filename_in;
    cmd_line += "\" ";

    for (auto i = 0; i < argc_in; i++)
    {
        cmd_line += argv_in[i];
        if (i + 1 < argc_in)
        {
            cmd_line += " ";
        }
    }

    pal_utf16_string lp_command_line_utf16_string(cmd_line);
    pal_utf16_string lp_current_directory_utf16_string(working_dir_in);

    STARTUPINFO si = { 0 };
    PROCESS_INFORMATION pi = { nullptr };

    const auto create_process_result = CreateProcess(nullptr, 
        lp_command_line_utf16_string.data(),
        nullptr, nullptr, true,
        0, nullptr, lp_current_directory_utf16_string.data(), &si, &pi);

    if (!create_process_result)
    {
        return FALSE;
    }

    auto result = WaitForSingleObject(pi.hProcess, INFINITE);
    if (result != WAIT_OBJECT_0)
    {
        return FALSE;
    }

    DWORD exit_code;
    if (FALSE == GetExitCodeProcess(pi.hProcess, &exit_code))
    {
        return FALSE;
    }

    *exit_code_out = exit_code;

    return TRUE;
#elif PAL_PLATFORM_LINUX
    if(argc_in <= 0)
    {
        return FALSE;
    }

    if (0 != chdir(working_dir_in))
    {
        return -1;
    }

    *exit_code_out = execvp(filename_in, argv_in);

    return TRUE;
#else
    return FALSE;
#endif
    }

PAL_API BOOL PAL_CALLING_CONVENTION pal_process_daemonize(const char *filename_in, const char *working_dir_in,
    const int argc_in, char **argv_in,
    const int cmd_show_in /* Only applicable on Windows */,
    pal_pid_t *pid_out)
{
    if (filename_in == nullptr
        || working_dir_in == nullptr)
    {
        return FALSE;
    }

#if PAL_PLATFORM_WINDOWS
    auto cmd_line = std::string("\"");
    cmd_line += filename_in;
    cmd_line += "\" ";

    for (auto i = 0; i < argc_in; i++)
    {
        cmd_line += argv_in[i];
        if (i + 1 < argc_in)
        {
            cmd_line += " ";
        }
    }

    pal_utf16_string lp_command_line_utf16_string(cmd_line);
    pal_utf16_string lp_current_directory_utf16_string(working_dir_in);

    STARTUPINFO si = { 0 };
    PROCESS_INFORMATION pi = { nullptr };

    si.cb = sizeof si;
    si.dwFlags = STARTF_USESHOWWINDOW;
    si.wShowWindow = static_cast<WORD>(cmd_show_in);

    const auto create_process_result = CreateProcess(nullptr, lp_command_line_utf16_string.data(),
        nullptr, nullptr, true,
        0, nullptr, lp_current_directory_utf16_string.data(), &si, &pi);

    if (!create_process_result)
    {
        return FALSE;
    }

    *pid_out = pi.dwProcessId;

    AllowSetForegroundWindow(pi.dwProcessId);
    WaitForInputIdle(pi.hProcess, 5 * 1000);

    return TRUE;
#elif PAL_PLATFORM_LINUX
    if(argc_in <= 0)
    {
        return FALSE;
    }

    PAL_UNUSED(cmd_show_in);

    if (0 != chdir(working_dir_in))
    {
        return -1;
    }

    auto child_pid = fork();
    if (child_pid == 0)
    {
        execvp(filename_in, argv_in);
    }
    else {
        *pid_out = child_pid;
    }

    return TRUE;
#else
    return FALSE;
#endif
}

PAL_API BOOL PAL_CALLING_CONVENTION pal_usleep(unsigned int milliseconds)
{
    if (milliseconds < 0)
    {
        return FALSE;
    }

#if PAL_PLATFORM_WINDOWS
    ::Sleep(milliseconds);
    return TRUE;
#elif PAL_PLATFORM_LINUX
    usleep(milliseconds);
    return TRUE;
#else
    return FALSE;
#endif
}

PAL_API BOOL PAL_CALLING_CONVENTION pal_is_windows()
{
#if PAL_PLATFORM_WINDOWS
    return TRUE;
#else
    return FALSE;
#endif
}

PAL_API BOOL PAL_CALLING_CONVENTION pal_is_linux()
{
#if PAL_PLATFORM_LINUX
    return TRUE;
#else
    return FALSE;
#endif
}

PAL_API BOOL PAL_CALLING_CONVENTION pal_is_unknown_os()
{
    return pal_is_linux()
        || pal_is_windows() ? FALSE : TRUE;
}

// - Environment
PAL_API BOOL PAL_CALLING_CONVENTION pal_env_set(const char* name_in, const char* value_in)
{
    if(name_in == nullptr)
    {
        return FALSE;
    }
#if PAL_PLATFORM_WINDOWS
    pal_utf16_string name_in_utf16_string(name_in);
    pal_utf16_string value_in_utf16_string(value_in == nullptr ? "" : value_in);
    const auto success = SetEnvironmentVariable(name_in_utf16_string.data(), value_in_utf16_string.empty() ? nullptr : value_in_utf16_string.data());
    return success == TRUE;
#elif PAL_PLATFORM_LINUX
    const auto success = value_in == nullptr ? unsetenv(name_in) : setenv(name_in, value_in, 1 /* overwrite */);
    return success == 0;
#else
    return FALSE;
#endif
}

PAL_API BOOL PAL_CALLING_CONVENTION pal_env_get(const char * environment_variable_in, char ** environment_variable_value_out)
{
    if (environment_variable_in == nullptr)
    {
        return FALSE;
    }

#if PAL_PLATFORM_WINDOWS
    pal_utf16_string environment_variable_in_utf16_string(environment_variable_in);

#if PAL_PLATFORM_MINGW
    auto w_env = _wgetenv(environment_variable_in_utf16_string.data());
    if (w_env != nullptr)
    {
        *environment_variable_value_out = pal_utf8_string(w_env).dup();
        return TRUE;
    }
    return FALSE;
#else
    const int buffer_size = 65535;
    wchar_t buffer[buffer_size];
    auto actual_len = GetEnvironmentVariable(environment_variable_in_utf16_string.data(), buffer, buffer_size);
    if(actual_len <= 0)
    {
        return FALSE;
    }

    *environment_variable_value_out = pal_utf8_string(buffer + '\0').dup();

    return TRUE;
#endif
#else
    const auto value = ::getenv(environment_variable_in);
    if (value == nullptr)
    {
        return FALSE;
    }

    *environment_variable_value_out = strdup(value);
    return TRUE;
#endif
}

PAL_API BOOL PAL_CALLING_CONVENTION pal_env_get_bool(const char * environment_variable_in)
{
    char* environment_variable_value_out = nullptr;
    if (!pal_env_get(environment_variable_in, &environment_variable_value_out))
    {
        return FALSE;
    }

    auto true_or_false = FALSE;
    if (pal_str_iequals(environment_variable_value_out, "1")
        || pal_str_iequals(environment_variable_value_out, "true"))
    {
        true_or_false = TRUE;
    }

    delete environment_variable_value_out;

    return true_or_false;
}

PAL_API BOOL PAL_CALLING_CONVENTION pal_env_expand_str(const char * environment_in, char ** environment_out)
{
    if (environment_in == nullptr)
    {
        return FALSE;
    }

    std::string environment_in_str(environment_in);

#if PAL_PLATFORM_WINDOWS
    static std::regex expression(R"(%([0-9A-Za-z\\/\(\)]*)%)", std::regex_constants::icase);
#else
    static std::regex expression(R"(\$\{([^}]+)\})", std::regex_constants::icase);
#endif

    auto replacements = 0;
    std::smatch match;
    while (std::regex_search(environment_in_str, match, expression)) {
        const auto match_str = match[1].str();
        char* environment_variable_value = nullptr;
        if (!pal_env_get(match_str.c_str(), &environment_variable_value))
        {
            continue;
        }

        const std::string environment_variable_value_s(environment_variable_value);
        delete environment_variable_value;

        environment_in_str.replace(match[0].first, match[0].second, environment_variable_value_s);

        replacements++;
    }

    if (replacements <= 0)
    {
        return FALSE;
    }

    *environment_out = _strdup(environment_in_str.c_str());

    return TRUE;
}

// - Filesystem

BOOL pal_fs_chmod(const char *path_in, uint32_t mode) {
    if (path_in == nullptr) {
        return FALSE;
    }

    BOOL is_success;
#if PAL_PLATFORM_WINDOWS
    pal_utf16_string path_in_utf16_string(path_in);
    is_success = 0 == _wchmod(path_in_utf16_string.data(), mode) ? TRUE : FALSE;
#elif PAL_PLATFORM_LINUX
    is_success = 0 == chmod(path_in, mode) ? TRUE : FALSE;
#endif
    return is_success;
}

PAL_API BOOL PAL_CALLING_CONVENTION pal_fs_get_directory_name_absolute_path(const char* path_in, char** path_out)
{
    if (path_in == nullptr)
    {
        return FALSE;
    }
#if PAL_PLATFORM_WINDOWS

    pal_utf16_string path_in_utf16_string(path_in);
    wchar_t path_in_without_filespec[PAL_MAX_PATH];
    wcscpy_s(path_in_without_filespec, PAL_MAX_PATH, path_in_utf16_string.data());

    PathRemoveFileSpec(path_in_without_filespec);

    *path_out = pal_utf8_string(path_in_without_filespec).dup();

    return TRUE;
#else
    auto path_in_cpy = strdup(path_in);
    char* dir = dirname(path_in_cpy);
    if (dir)
    {
        //  Both dirname() and basename() return pointers to null-terminated
        // strings.  (Do not pass these pointers to free(3).)
        *path_out = strdup(dir);
        return TRUE;
    }
    delete path_in_cpy;
    return FALSE;
#endif
    }

PAL_API BOOL PAL_CALLING_CONVENTION pal_fs_get_directory_name(const char * path_in, char ** path_out)
{
    if (path_in == nullptr)
    {
        return FALSE;
    }

    std::string path_in_s(path_in);

    const auto directory_name_start_pos = path_in_s.find_last_of(PAL_DIRECTORY_SEPARATOR_C);
    if (directory_name_start_pos == std::string::npos)
    {
        return FALSE;
    }

    const auto directory_name = path_in_s.substr(directory_name_start_pos + 1);

    *path_out = _strdup(directory_name.c_str());

    return TRUE;
}

#if PAL_PLATFORM_LINUX

// https://github.com/qpalzmqaz123/path_combine/blob/master/path_combine.c

inline int unix_path_combine_cleanup(char *path)
{
    char *str;
    char *parent_dir;
    char *current_dir;
    char *tail;

    if (0 == strlen(path)) {
        return 0;
    }

    /* resolve parent dir */
    while (TRUE) {
        str = strstr(path, "/../");
        if (nullptr == str) {
            break;
        }

        *str = 0;
        if (nullptr == strchr(path, '/')) {
            return 1;
        }
        parent_dir = strrchr(path, '/') + 1;

        current_dir = str + 4;

        /* replace parent dir */
        memcpy(parent_dir, current_dir, strlen(current_dir) + 1);
    }

    /* resolve current dir */
    while (TRUE) {
        str = strstr(path, "/./");
        if (nullptr == str) {
            break;
        }

        memcpy(str + 1, str + 3, strlen(str + 3) + 1);
    }

    /* remove tail '/' or '/.' */
    tail = path + strlen(path) - 1;
    if ('/' == *tail) {
        *tail = 0;
    }
    else if (0 == strcmp(tail - 1, "/.")) {
        *(tail - 1) = 0;
    }
    else if (0 == strcmp(tail - 2, "/..")) {
        strcat(path, "/");
        unix_path_combine_cleanup(path);
    }

    return 0;
}

inline char* unix_path_combine(const char *path1, const char *path2, char *buffer)
{
    if (pal_str_is_null_or_whitespace(path1)
        || pal_str_is_null_or_whitespace(path2)) {
        return nullptr;
    }
    else if (nullptr == path1) {
        strcpy(buffer, path2);
        goto EXIT;
    }
    else if (nullptr == path2) {
        strcpy(buffer, path1);
        goto EXIT;
    }

    if ('/' == path2[0]) {
        strcpy(buffer, path2);
        goto EXIT;
    }

    strcpy(buffer, path1);

    if ('/' != path1[strlen(path1) - 1]) {
        strcat(buffer, "/");
    }

    strcat(buffer, path2);

EXIT:
    return 0 == unix_path_combine_cleanup(buffer) ? buffer : nullptr;
}
#endif

PAL_API BOOL PAL_CALLING_CONVENTION pal_fs_path_combine(const char * path1, const char * path2, char ** path_out)
{
    if (path1 == nullptr
        || path2 == nullptr)
    {
        return FALSE;
    }
#if PAL_PLATFORM_WINDOWS

    pal_utf16_string path_in_lhs_utf16_string(path1);
    pal_utf16_string path_in_rhs_utf16_string(path2);

    wchar_t path_combined[PAL_MAX_PATH];
    if (nullptr == PathCombine(path_combined, path_in_lhs_utf16_string.data(), path_in_rhs_utf16_string.data()))
    {
        return false;
    }

    *path_out = pal_utf8_string(path_combined).dup();

    return TRUE;
#elif PAL_PLATFORM_LINUX
    char buffer[1024];
    if (nullptr == unix_path_combine(path1, path2, buffer))
    {
        return FALSE;
    }

    *path_out = strdup(buffer);

    return TRUE;
#else
    return FALSE;
#endif
}

PAL_API BOOL PAL_CALLING_CONVENTION pal_fs_file_exists(const char * file_path_in)
{
    if (file_path_in == nullptr)
    {
        return FALSE;
    }

    BOOL file_exists;
#if PAL_PLATFORM_WINDOWS
    pal_utf16_string file_path_in_utf16_string(file_path_in);
    auto file_attributes = GetFileAttributes(file_path_in_utf16_string.data());

    if (file_attributes == INVALID_FILE_ATTRIBUTES
        || file_attributes & FILE_ATTRIBUTE_DIRECTORY)
    {
        file_exists = FALSE;
}
    else
    {
        file_exists = PathFileExists(file_path_in_utf16_string.data()) == TRUE ? TRUE : FALSE;
    }

    return file_exists;
#elif PAL_PLATFORM_LINUX
    struct stat st = { 0 };
    file_exists = stat(file_path_in, &st) == 0 && (st.st_mode & S_IFDIR) == 0;
    return file_exists;
#else
    return FALSE;
#endif
}

PAL_API BOOL PAL_CALLING_CONVENTION pal_fs_list_impl(const char * path_in, const pal_fs_list_filter_callback_t filter_callback_in,
    const char* filter_extension_in, char *** paths_out, size_t * paths_out_len, const int type)
{
    if (path_in == nullptr)
    {
        return false;
    }

    std::vector<char*> paths;
    BOOL paths_success;

#if PAL_PLATFORM_WINDOWS

    const pal_utf16_string extension_filter_in_utf16_string(
        filter_extension_in != nullptr ? filter_extension_in : "*");

    pal_utf16_string path_root_utf16_string(path_in);
    if (!path_root_utf16_string.ends_with(PAL_DIRECTORY_SEPARATOR_WIDE_STR))
    {
        path_root_utf16_string.append(PAL_DIRECTORY_SEPARATOR_WIDE_STR);
    }

    path_root_utf16_string.append(extension_filter_in_utf16_string.str());

    WIN32_FIND_DATA file;
    const auto h_file = FindFirstFile(path_root_utf16_string.data(), &file);
    if (h_file == INVALID_HANDLE_VALUE)
    {
        return FALSE;
    }

    do
    {
        switch (type)
        {
        case 0:
            if (!(file.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY))
            {
                continue;
            }
            break;
        case 1:
            if (file.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY)
            {
                continue;
            }
            break;
        default:
            continue;
        }

        const auto relative_path = pal_utf8_string(file.cFileName).str();
        if (relative_path == "." || relative_path == "..")
        {
            continue;
        }

        char* absolute_path = nullptr;
        if (!pal_fs_path_combine(path_in, relative_path.data(), &absolute_path))
        {
            delete[] absolute_path;
            continue;
        }

        const auto filter_callback_fn = filter_callback_in;
        if (filter_callback_fn != nullptr
            && !filter_callback_fn(absolute_path))
        {
            delete[] absolute_path;
            continue;
        }

        paths.emplace_back(absolute_path);

    } while (FindNextFile(h_file, &file));

    ::FindClose(h_file);

    paths_success = TRUE;

#elif PAL_PLATFORM_LINUX
    std::string filter_extension_s(filter_extension_in == nullptr ? std::string() : filter_extension_in);

    DIR* dir = opendir(path_in);
    paths_success = dir != nullptr;
    if (paths_success)
    {
        struct dirent* entry;
        while ((entry = readdir(dir)) != nullptr)
        {
            std::string absolute_path_s;
            std::string entry_name(entry->d_name);

            switch (type)
            {
            case 0:
                if (entry->d_type != DT_DIR)
                {
                    continue;
                }

                if (entry_name == "." || entry_name == "..")
                {
                    continue;
                }

                absolute_path_s.assign(path_in);
                absolute_path_s.append("/");
                absolute_path_s.append(entry_name);

                break;
            case 1:
                switch (entry->d_type)
                {
                default:
                    continue;
                    // Regular file
                case DT_REG:
                    if (filter_extension_in != nullptr
                        && FALSE == pal_str_endswith(entry_name.c_str(), filter_extension_in))
                    {
                        continue;
                    }

                    absolute_path_s.assign(path_in);
                    absolute_path_s.append("/");
                    absolute_path_s.append(entry_name);
                    break;

                    // Handle symlinks and file systems that do not support d_type
                case DT_LNK:
                case DT_UNKNOWN:
                    if (filter_extension_in != nullptr
                        && FALSE == pal_str_endswith(entry_name.c_str(), filter_extension_in))
                    {
                        continue;
                    }

                    absolute_path_s.assign(path_in);
                    absolute_path_s.append("/");
                    absolute_path_s.append(entry_name);

                    struct stat file_stat = { 0 };
                    if (stat(absolute_path_s.c_str(), &file_stat) == -1)
                    {
                        absolute_path_s.clear();
                        continue;
                    }

                    // Must be a regular file.
                    if (!S_ISREG(file_stat.st_mode))
                    {
                        absolute_path_s.clear();
                        continue;
                    }

                    break;
                }
                break;
            default:
                // void
                break;
            }

            if (absolute_path_s.empty())
            {
                continue;
            }

            const auto filter_callback_fn = filter_callback_in;
            if (filter_callback_fn != nullptr
                && !filter_callback_fn(absolute_path_s.c_str()))
            {
                continue;
            }

            paths.emplace_back(strdup(absolute_path_s.data()));
    }

        closedir(dir);
}
#endif

    *paths_out_len = paths.size();

    const auto paths_array = new char*[*paths_out_len];

    for (auto i = 0u; i < *paths_out_len; i++)
    {
        paths_array[i] = paths[i];
    }

    *paths_out = paths_array;

    return TRUE;
}

PAL_API BOOL PAL_CALLING_CONVENTION pal_fs_list_directories(const char * path_in, const pal_fs_list_filter_callback_t filter_callback_in,
    const char* filter_extension_in, char *** directories_out, size_t* directories_out_len)
{
    return pal_fs_list_impl(path_in, filter_callback_in, filter_extension_in, directories_out, directories_out_len, 0);
}

PAL_API BOOL PAL_CALLING_CONVENTION pal_fs_list_files(const char * path_in, const pal_fs_list_filter_callback_t filter_callback_in,
    const char* filter_extension_in, char *** files_out, size_t * files_out_len)
{
    return pal_fs_list_impl(path_in, filter_callback_in, filter_extension_in, files_out, files_out_len, 1);
}

PAL_API BOOL PAL_CALLING_CONVENTION pal_fs_get_cwd(char ** working_directory_out)
{
#if PAL_PLATFORM_WINDOWS
    wchar_t* buffer = nullptr;
    if ((buffer = _wgetcwd(nullptr, 0)) == nullptr)
    {
        return FALSE;
    }

    *working_directory_out = pal_utf8_string(buffer).dup();

    delete buffer;

    return TRUE;
#elif PAL_PLATFORM_LINUX
    char cwd[PATH_MAX];
    if (getcwd(cwd, sizeof(cwd)) != nullptr)
    {
        *working_directory_out = strdup(cwd);
        return TRUE;
    }
    return FALSE;
#else
    return FALSE;
#endif
}

PAL_API BOOL PAL_CALLING_CONVENTION pal_fs_get_process_real_path(char** real_path_out)
{
#if PAL_PLATFORM_WINDOWS

    wchar_t buffer[PAL_MAX_PATH];
    if (0 == GetModuleFileName(nullptr, buffer, PAL_MAX_PATH))
    {
        return FALSE;
    }
    *real_path_out = pal_utf8_string(buffer).dup();
    return TRUE;
#elif PAL_PLATFORM_LINUX
    char* real_path = nullptr;
    if (pal_fs_get_absolute_path(symlink_entrypoint_executable, &real_path))
    {
        *real_path_out = real_path;
        return TRUE;
    }
    return FALSE;
#else
    return FALSE;
#endif
}

PAL_API BOOL PAL_CALLING_CONVENTION pal_fs_get_own_executable_name(char ** own_executable_name_out)
{
    char* real_path = nullptr;
    if (!pal_fs_get_process_real_path(&real_path))
    {
        return FALSE;
    }

    std::string real_path_str(real_path);
    const auto directory_separator_pos = real_path_str.find_last_of(PAL_DIRECTORY_SEPARATOR_C);
    if (std::string::npos == directory_separator_pos)
    {
        return FALSE;
    }

    const auto exe_name = real_path_str.substr(directory_separator_pos + 1);
    *own_executable_name_out = _strdup(exe_name.c_str());

    return TRUE;
}

PAL_API BOOL PAL_CALLING_CONVENTION pal_fs_get_absolute_path(const char * path_in, char ** path_absolute_out)
{
    if (path_in == nullptr)
    {
        return FALSE;
    }

#if PAL_PLATFORM_WINDOWS

    pal_utf16_string path_in_utf16_string(path_in);
    pal_utf16_string path_absolute_out_utf16_string(PAL_MAX_PATH);
    const auto path_absolute_out_len = GetLongPathName(path_in_utf16_string.data(),
        path_absolute_out_utf16_string.data(), PAL_MAX_PATH);
    if (path_absolute_out_len == 0)
    {
        return FALSE;
    }

    *path_absolute_out = pal_utf8_string(path_absolute_out_utf16_string.data()).dup();

    return TRUE;
#elif PAL_PLATFORM_LINUX
    char real_path[PATH_MAX];
    if (realpath(path_in, real_path) != nullptr && real_path[0] != '\0')
    {
        std::string real_path_str(real_path);

        // realpath should return canonicalized path without the trailing slash
        assert(real_path_str.back() != '/');

        *path_absolute_out = strdup(real_path_str.c_str());

        return TRUE;
    }
    return FALSE;
#else
    return FALSE;
#endif
}

PAL_API BOOL PAL_CALLING_CONVENTION pal_fs_directory_exists(const char * path_in)
{
    if (path_in == nullptr)
    {
        return FALSE;
    }

    BOOL directory_exists = FALSE;
#if PAL_PLATFORM_WINDOWS    

    pal_utf16_string path_in_utf16_string(path_in);

    DWORD attributes = 0;
    if (!path_in_utf16_string.ends_with(PAL_DIRECTORY_SEPARATOR_WIDE_STR))
    {
        attributes = GetFileAttributes(path_in_utf16_string.append(PAL_DIRECTORY_SEPARATOR_WIDE_STR).data());
    }
    else {
        attributes = GetFileAttributes(path_in_utf16_string.data());
    }

    if (attributes == INVALID_FILE_ATTRIBUTES)
    {
        directory_exists = FALSE;
}
    else if (attributes & FILE_ATTRIBUTE_DIRECTORY)
    {
        directory_exists = TRUE;
    }

#elif PAL_PLATFORM_LINUX
    auto directory = opendir(path_in);
    if (directory)
    {
        directory_exists = TRUE;
        assert(0 == closedir(directory));
    }
#endif
    return directory_exists;
}

PAL_API BOOL PAL_CALLING_CONVENTION pal_fs_get_file_size(const char* filename_in, size_t* file_size_out)
{
    if (filename_in == nullptr)
    {
        return FALSE;
    }

#if PAL_PLATFORM_WINDOWS
    pal_utf16_string path_in_utf16_string(filename_in);

    auto h_file = CreateFile(path_in_utf16_string.data(),
        GENERIC_READ,
        FILE_SHARE_READ,
        nullptr,
        OPEN_EXISTING,
        FILE_ATTRIBUTE_NORMAL,
        nullptr);

    if (h_file == INVALID_HANDLE_VALUE)
    {
        return FALSE;
    }

    *file_size_out = GetFileSize(h_file, nullptr);

    assert(0 != CloseHandle(h_file));

    return TRUE;
#elif PAL_PLATFORM_LINUX
    auto h_file = fopen(filename_in, "rb");
    if (h_file == nullptr)
    {
        return FALSE;
    }

    fseek(h_file, 0, SEEK_END);
    *file_size_out = static_cast<size_t>(ftell(h_file));
    assert(0 == fclose(h_file));

    return TRUE;
#else
    return FALSE;
#endif
}

PAL_API BOOL PAL_CALLING_CONVENTION pal_fs_read_file(const char* filename_in, const char* mode_in, char** bytes_out, size_t* bytes_read_out)
{
    if (filename_in == nullptr
        || mode_in == nullptr)
    {
        return FALSE;
    }

    size_t total_bytes_to_read;
    if (!pal_fs_get_file_size(filename_in, &total_bytes_to_read) || total_bytes_to_read <= 0)
    {
        return FALSE;
    }

#if PAL_PLATFORM_WINDOWS
    pal_utf16_string path_in_utf16_string(filename_in);

    auto h_file = CreateFile(path_in_utf16_string.data(),
        GENERIC_READ,
        FILE_SHARE_READ,
        nullptr,
        OPEN_EXISTING,
        FILE_ATTRIBUTE_NORMAL,
        nullptr);

    if (h_file == INVALID_HANDLE_VALUE)
    {
        return FALSE;
    }

    size_t read_offset = 0;
    DWORD read_buffer_bytes_read = 0;
    const auto read_buffer_size = 4096;
    char read_buffer[read_buffer_size];
    while (ReadFile(h_file, read_buffer, read_buffer_size, &read_buffer_bytes_read, nullptr)
        && read_buffer_bytes_read > 0)
    {
        if (read_offset == 0)
        {
            *bytes_out = new char[total_bytes_to_read];
            if(total_bytes_to_read < read_buffer_size)
            {
                std::memcpy(*bytes_out, &read_buffer, read_buffer_bytes_read);
                read_offset = read_buffer_bytes_read;
                break;
            }
        }
        std::memcpy(*bytes_out + read_offset, &read_buffer[0], read_buffer_bytes_read * sizeof read_buffer[0]);
        read_offset += read_buffer_bytes_read;
    }

    assert(0 != CloseHandle(h_file));

    if (read_offset != total_bytes_to_read)
    {
        if (read_offset > 0)
        {
            delete[] bytes_out;
        }
        return FALSE;
    }

    *bytes_read_out = read_offset;

    return TRUE;
#elif PAL_PLATFORM_LINUX
    auto h_file = fopen(filename_in, mode_in);
    if (h_file == nullptr)
    {
        return FALSE;
    }
    auto buffer = new char[total_bytes_to_read];
    auto total_bytes_read = fread(buffer, 1, total_bytes_to_read, h_file);
    assert(0 == fclose(h_file));
    if (total_bytes_read != total_bytes_to_read)
    {
        delete[] buffer;
        return FALSE;
    }
    *bytes_out = buffer;
    *bytes_read_out = total_bytes_read;
    return TRUE;
#else
    return FALSE;
#endif
}

PAL_API BOOL PAL_CALLING_CONVENTION pal_fs_mkdir(const char* directory_in, uint32_t mode_in)
{
    if (directory_in == nullptr || mode_in <= 0)
    {
        return FALSE;
    }

#if PAL_PLATFORM_WINDOWS
    pal_utf16_string directory_in_utf16_string(directory_in);
    const auto status = _wmkdir(directory_in_utf16_string.data());
    return status == 0 ? TRUE : FALSE;
#else
    const auto status = mkdir(directory_in, mode_in);
    return status == 0 ? TRUE : FALSE;
#endif
}

PAL_API BOOL PAL_CALLING_CONVENTION pal_fs_rmfile(const char* filename_in)
{
    if (filename_in == nullptr)
    {
        return FALSE;
    }
#if PAL_PLATFORM_WINDOWS
    pal_utf16_string filename_in_utf16_string(filename_in);
    const auto success = DeleteFile(filename_in_utf16_string.data());
    return success == TRUE;
#elif PAL_PLATFORM_LINUX
    const auto success = remove(filename_in);
    return success == 0 ? TRUE : FALSE;
#else 
    return FALSE;
#endif
}

PAL_API BOOL PAL_CALLING_CONVENTION pal_fs_rmdir(const char* directory_in, BOOL recursive)
{
    if (directory_in == nullptr)
    {
        return FALSE;
    }

#if PAL_PLATFORM_WINDOWS
    pal_utf16_string directory_in_utf16_string(directory_in);
    if (!recursive)
    {
        const auto status = _wrmdir(directory_in_utf16_string.data());
        return status == 0 ? TRUE : FALSE;
    }
#elif PAL_PLATFORM_LINUX
    if(!recursive)
    {
        const auto status = rmdir(directory_in);
        return status == 0 ? TRUE : FALSE;    
    }
#else
    return FALSE;
#endif

    char** files_array = nullptr;
    size_t files_array_len = 0u;
    if (pal_fs_list_files(directory_in, nullptr, nullptr, &files_array, &files_array_len))
    {
        std::vector<std::string> files(files_array, files_array + files_array_len);
        for (const auto &filename : files)
        {
            pal_fs_rmfile(filename.c_str());
        }

        delete[] files_array;
        files_array = nullptr;
        files_array_len = 0;
    }

    char** directories_array = nullptr;
    size_t directories_array_len = 0u;
    if (!pal_fs_list_directories(directory_in, nullptr, nullptr, &directories_array, &directories_array_len)
        || directories_array_len <= 0)
    {
        return pal_fs_rmdir(directory_in, FALSE);
    }

    std::vector<std::string> directories(directories_array, directories_array + directories_array_len);

    delete[] directories_array;
    directories_array = nullptr;
    directories_array_len = 0;

    for (const auto &directory : directories)
    {
        if (!pal_fs_rmdir(directory.c_str(), TRUE))
        {
            return FALSE;
        }
    }

    return pal_fs_rmdir(directory_in, FALSE);
}

PAL_API BOOL PAL_CALLING_CONVENTION pal_fs_fopen(const char* filename_in, const char* mode_in, pal_file_handle_t** file_handle_out)
{
    if (filename_in == nullptr
        || mode_in == nullptr)
    {
        return FALSE;
    }

    BOOL fopen_success = FALSE;
#if PAL_PLATFORM_WINDOWS
    pal_utf16_string filename_in_utf16_string(filename_in);
    pal_utf16_string mode_in_utf16_string(mode_in);
    FILE* p_file = nullptr;
    auto result_errno = _wfopen_s(&p_file, filename_in_utf16_string.data(), mode_in_utf16_string.data());
    if (result_errno == 0)
    {
        *file_handle_out = p_file;
        fopen_success = TRUE;
    }
#elif PAL_PLATFORM_LINUX
    FILE* h_file = fopen(filename_in, mode_in);
    if (h_file != nullptr)
    {
        *file_handle_out = h_file;
        fopen_success = TRUE;
    }
#endif
    return fopen_success;
    }

PAL_API BOOL PAL_CALLING_CONVENTION pal_fs_fwrite(pal_file_handle_t* pal_file_handle_in, const void* data_in, size_t data_len_in)
{
    if (pal_file_handle_in == nullptr
        || data_in == nullptr
        || data_len_in <= 0)
    {
        return FALSE;
    }

    BOOL fwrite_success = FALSE;
#if PAL_PLATFORM_WINDOWS || PAL_PLATFORM_LINUX
    auto bytes_written = fwrite(data_in, 1, data_len_in, pal_file_handle_in);
    if (bytes_written == data_len_in)
    {
        fwrite_success = TRUE;
    }
#endif
    return fwrite_success;
}

PAL_API BOOL PAL_CALLING_CONVENTION pal_fs_write(const char* filename, const char* mode_in, const void* data_in, size_t data_len_in)
{
    pal_file_handle_t* file_handle = nullptr;
    if (!pal_fs_fopen(filename, mode_in, &file_handle))
    {
        return FALSE;
    }

    if (!pal_fs_fwrite(file_handle, data_in, data_len_in))
    {
        return FALSE;
    }

    if (!pal_fs_fclose(file_handle))
    {
        return FALSE;
    }

    return TRUE;
}

PAL_API BOOL PAL_CALLING_CONVENTION pal_fs_fclose(pal_file_handle_t*& pal_file_handle_in)
{
    if (pal_file_handle_in == nullptr)
    {
        return FALSE;
    }
    BOOL fclose_success;
#if PAL_PLATFORM_WINDOWS || PAL_PLATFORM_LINUX
    fclose_success = fclose(pal_file_handle_in) == 0 ? TRUE : FALSE;
    if (fclose_success)
    {
        pal_file_handle_in = nullptr;
    }
#endif
    return fclose_success;
}

PAL_API BOOL PAL_CALLING_CONVENTION pal_str_endswith(const char * src, const char * str)
{
    if (src == nullptr || str == nullptr)
    {
        return FALSE;
    }

    const auto value = std::string(src);
    const auto ending = std::string(str);

    if (ending.size() > value.size()) {
        return FALSE;
    }

    return std::equal(ending.rbegin(), ending.rend(), value.rbegin()) ? TRUE : FALSE;
}

PAL_API BOOL PAL_CALLING_CONVENTION pal_str_startswith(const char * src, const char * str)
{
    if (src == nullptr || str == nullptr)
    {
        return FALSE;
    }

    const auto text = std::string(src);
    const auto substr = std::string(str);
    const auto diff = std::strncmp(text.c_str(), substr.c_str(), substr.size());

    return diff == 0;
}

PAL_API BOOL PAL_CALLING_CONVENTION pal_str_iequals(const char* lhs, const char* rhs)
{
    std::string str1(lhs == nullptr ? std::string() : lhs);
    std::string str2(rhs == nullptr ? std::string() : rhs);

    auto equals = ((str1.size() == str2.size()) && std::equal(str1.begin(), str1.end(), str2.begin(), [](char & c1, char & c2) {
        return (c1 == c2 || std::toupper(c1) == std::toupper(c2));
    })) ? TRUE : FALSE;

    return equals;
}

PAL_API BOOL PAL_CALLING_CONVENTION pal_str_is_null_or_whitespace(const char* str)
{
    if (str == nullptr)
    {
        return TRUE;
    }

#if PAL_PLATFORM_WINDOWS
    pal_utf16_string value(str);
    return value.empty_or_whitespace() ? TRUE : FALSE;
#elif PAL_PLATFORM_LINUX
    pal_utf8_string value(str);
    return value.empty_or_whitespace() ? TRUE : FALSE;
#else
    return FALSE;
#endif
}

#if _MSC_VER
#pragma warning( push )
#pragma warning( disable : 4068)
#endif

#pragma clang diagnostic pop

#if _MSC_VER
#pragma warning( pop )
#endif
