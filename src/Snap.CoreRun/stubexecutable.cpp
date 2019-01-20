#include "stubexecutable.hpp"
#include "vendor/semver/semver200.h"
#include <iostream>

using std::wstring;

#if PLATFORM_WINDOWS
int snap::stubexecutable::run(std::vector<wchar_t*> arguments, const int cmd_show)
#else
int snap::stubexecutable::run(std::vector<wchar_t*> arguments)
#endif
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

    const auto full_path(working_dir + PAL_DIRECTORY_SEPARATOR_STR + app_name);

    std::wstring cmd_line(L"\"");
    cmd_line += full_path;
    cmd_line += L"\" ";

    for (auto const& argument : arguments)
    {
        cmd_line += argument;
    }

    const auto lp_command_line = _wcsdup(cmd_line.c_str());
    const auto lp_current_directory = _wcsdup(working_dir.c_str());

#if PLATFORM_WINDOWS
    STARTUPINFO si;
    PROCESS_INFORMATION pi;

    si.cb = sizeof si;
    si.dwFlags = STARTF_USESHOWWINDOW;
    si.wShowWindow = cmd_show;
    if (!CreateProcess(nullptr, lp_command_line,
        nullptr, nullptr, true,
        0, nullptr, lp_current_directory, &si, &pi)) {
        delete lp_command_line;
        delete lp_current_directory;
        return -1;
    }

    delete lp_command_line;
    delete lp_current_directory;

    AllowSetForegroundWindow(pi.dwProcessId);
    WaitForInputIdle(pi.hProcess, 5 * 1000);
#else
#error TODO: IMPLEMENT ME
#endif

    return 0;
}

std::wstring snap::stubexecutable::find_root_app_dir()
{
    wchar_t* current_directory_out = nullptr;
    if (!pal_fs_get_current_directory(&current_directory_out))
    {
        return std::wstring();
    }

    return std::wstring(current_directory_out);
}

std::wstring snap::stubexecutable::find_own_executable_name()
{
    wchar_t* own_executable_name_out = nullptr;
    if (!pal_fs_get_own_executable_name(&own_executable_name_out))
    {
        return std::wstring();
    }

    return std::wstring(own_executable_name_out);
}

std::wstring snap::stubexecutable::find_latest_app_dir()
{
    auto root_app_directory(find_root_app_dir());
    if (root_app_directory.empty())
    {
        return std::wstring();
    }

    wchar_t** paths_out = nullptr;
    size_t paths_out_len = 0;
    if (!pal_fs_list_directories(root_app_directory.c_str(), &paths_out, &paths_out_len))
    {
        return std::wstring();
    }

    std::vector<wchar_t*> paths(paths_out, paths_out + paths_out_len);
    delete[] paths_out;

    if (paths.empty())
    {
        return std::wstring();
    }

    version::Semver200_version acc("0.0.0");
    std::wstring acc_s;

    for (const auto &directory : paths)
    {
        wchar_t* directory_name = nullptr;
        if (!pal_fs_get_directory_name(directory, &directory_name))
        {
            continue;
        }

        if (!pal_str_startswith(directory_name, L"app-"))
        {
            continue;
        }

        auto current_app_ver_w = std::wstring(directory_name).substr(4); // Skip 'app-'
        std::string current_app_ver_s(pal_str_narrow(current_app_ver_w.c_str()));

        version::Semver200_version current_app_semver;

        try
        {
            current_app_semver = version::Semver200_version(current_app_ver_s);
        }
        catch (const version::Parse_error& e)
        {
            std::wcerr << L"Error - Unable to parse app version. Why: " << e.what() << ". Path: " << directory << std::endl;
            continue;
        }

        if (current_app_semver <= acc) {
            continue;
        }

        acc = current_app_semver;
        acc_s = current_app_ver_w;
    }

    root_app_directory.assign(find_root_app_dir());
    std::wstringstream ret;
    ret << root_app_directory << PAL_DIRECTORY_SEPARATOR_STR << L"app-" << acc_s;

    return ret.str();
}
