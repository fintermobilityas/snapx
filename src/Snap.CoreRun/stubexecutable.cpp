#include "stubexecutable.hpp"
#include "vendor/semver/semver200.h"

#if PLATFORM_LINUX
#include <unistd.h> // fork
#include <sys/types.h> // pid_t
#include <sys/wait.h> // wait
#endif

using std::string;

int snap::stubexecutable::run(std::vector<std::string> arguments, const int cmd_show)
{
    auto app_name(find_own_executable_name());
    if (app_name.empty())
    {
        return -1;
    }

    auto working_dir(find_latest_app_dir());
    if (working_dir.empty())
    {
        return -1;
    }

    const auto executable_full_path(working_dir + PAL_DIRECTORY_SEPARATOR_C + app_name);
#if PLATFORM_WINDOWS

    std::string cmd_line("\"");
    cmd_line += executable_full_path;
    cmd_line += "\" ";

    for (auto const& argument : arguments)
    {
        cmd_line += argument;
    }

    pal_utf16_string lp_command_line_utf16_string(cmd_line);
    pal_utf16_string lp_current_directory_utf16_string(working_dir);

    STARTUPINFO si = { 0 };
    PROCESS_INFORMATION pi = { nullptr };

    si.cb = sizeof si;
    si.dwFlags = STARTF_USESHOWWINDOW;
    si.wShowWindow = cmd_show;

    const auto create_process_result = CreateProcess(nullptr, lp_command_line_utf16_string.data(),
        nullptr, nullptr, true,
        0, nullptr, lp_current_directory_utf16_string.data(), &si, &pi);

    if (!create_process_result)
    {
        return -1;
    }

    AllowSetForegroundWindow(pi.dwProcessId);
    WaitForInputIdle(pi.hProcess, 5 * 1000);
#elif PLATFORM_LINUX
    const auto argv = new char*[arguments.size() + 1];
    argv[0] = strdup(executable_full_path.c_str());
    for (auto i = 1; i < arguments.size(); i++) {
        argv[i] = strdup(arguments[i].c_str());
    }

    auto exitCode = -1;

    if (0 != chdir(working_dir.c_str()))
    {
        goto done;
    }

    exitCode = execvp(executable_full_path.c_str(), argv);

done:
    delete[] argv;
    return exitCode;
#endif
    return -1;
}

std::string snap::stubexecutable::find_app_dir()
{
    char* current_directory = nullptr;
    if (!pal_fs_get_cwd(&current_directory))
    {
        return std::string();
    }

    return std::string(current_directory);
}

std::string snap::stubexecutable::find_own_executable_name()
{
    char* own_executable_name = nullptr;
    if (!pal_fs_get_own_executable_name(&own_executable_name))
    {
        return std::string();
    }

    return std::string(own_executable_name);
}

std::string snap::stubexecutable::find_latest_app_dir()
{
    auto app_dir(find_app_dir());
    if (app_dir.empty())
    {
        return std::string();
    }

    char** paths_out = nullptr;
    size_t paths_out_len = 0;
    if (!pal_fs_list_directories(app_dir.c_str(), nullptr, nullptr, &paths_out, &paths_out_len))
    {
        return std::string();
    }

    std::vector<char*> paths(paths_out, paths_out + paths_out_len);
    delete[] paths_out;

    if (paths.empty())
    {
        return std::string();
    }

    version::Semver200_version most_recent_semver("0.0.0");

    for (const auto &directory : paths)
    {
        char* directory_name = nullptr;
        if (!pal_fs_get_directory_name(directory, &directory_name))
        {
            continue;
        }

        if (!pal_str_startswith(directory_name, "app-"))
        {
            continue;
        }

        auto current_app_ver = std::string(directory_name).substr(4); // Skip 'app-'
        std::string current_app_ver_s(current_app_ver.c_str());

        version::Semver200_version current_app_semver;

        try
        {
            current_app_semver = version::Semver200_version(current_app_ver_s);
        }
        catch (const version::Parse_error)
        {
            continue;
        }

        if (current_app_semver <= most_recent_semver)
        {
            continue;
        }

        most_recent_semver = current_app_semver;
    }

    app_dir.assign(find_app_dir());
    std::stringstream ret;
    ret << app_dir << PAL_DIRECTORY_SEPARATOR_STR << "app-" << most_recent_semver;

    return ret.str();
}
