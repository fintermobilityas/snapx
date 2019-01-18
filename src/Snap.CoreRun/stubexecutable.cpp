#include "stubexecutable.hpp"
#include "vendor/semver/semver200.h"

using std::wstring;

int snap::stubexecutable::run_current_snap_windows(std::vector<std::wstring> arguments, int cmdShow)
{
    std::wstring app_name;
    app_name.assign(find_own_executable_name());
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

    STARTUPINFO si = { 0 };
    PROCESS_INFORMATION pi = { nullptr };

    si.cb = sizeof si;
    si.dwFlags = STARTF_USESHOWWINDOW;
    si.wShowWindow = cmdShow;

    std::wstring cmd_line(L"\"");
    cmd_line += full_path;
    cmd_line += L"\" ";

    for (auto const& argument : arguments)
    {
        cmd_line += argument;
    }

    const auto lp_command_line = _wcsdup(cmd_line.c_str());
    const auto lp_current_directory = _wcsdup(working_dir.c_str());
    if (!CreateProcess(nullptr, lp_command_line,
        nullptr, nullptr, true,
        0, nullptr, lp_current_directory, &si, &pi)) {
        return -1;
    }

    AllowSetForegroundWindow(pi.dwProcessId);
    WaitForInputIdle(pi.hProcess, 5 * 1000);
    return 0;
}

std::wstring snap::stubexecutable::find_root_app_dir()
{
    const auto our_directory = new wchar_t[PAL_MAX_PATH];

    GetModuleFileName(GetModuleHandle(nullptr), our_directory, PAL_MAX_PATH);
    const auto last_slash = wcsrchr(our_directory, PAL_DIRECTORY_SEPARATOR_C);
    if (!last_slash) {
        delete[] our_directory;
        return std::wstring();
    }

    // Null-terminate the string at the slash so now it's a directory
    *last_slash = 0x0;

    auto null_terminated = std::wstring(our_directory);

    delete[] our_directory;

    return null_terminated;
}

std::wstring snap::stubexecutable::find_own_executable_name()
{
    const auto our_directory = new wchar_t[PAL_MAX_PATH];

    GetModuleFileName(GetModuleHandle(nullptr), our_directory, PAL_MAX_PATH);
    const auto last_slash = wcsrchr(our_directory, PAL_DIRECTORY_SEPARATOR_C);
    if (!last_slash) {
        delete[] our_directory;
        return std::wstring();
    }

    std::wstring ret = _wcsdup(last_slash + 1);
    delete[] our_directory;
    return ret;
}

std::wstring snap::stubexecutable::find_latest_app_dir()
{
    std::wstring our_dir;
    our_dir.assign(find_root_app_dir());
    if (our_dir.empty())
    {
        return std::wstring();
    }

    our_dir += L"\\app-*";

    WIN32_FIND_DATA file_info = { 0 };
    auto h_file = FindFirstFile(our_dir.c_str(), &file_info);
    if (h_file == INVALID_HANDLE_VALUE) {
        return std::wstring();
    }

    version::Semver200_version acc("0.0.0");
    std::wstring acc_s;

    do {
        std::wstring app_ver = file_info.cFileName;
        app_ver = app_ver.substr(4);   // Skip 'app-'
        if (!(file_info.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY)) {
            continue;
        }

        std::string s(app_ver.begin(), app_ver.end());

        version::Semver200_version this_version(s);

        if (this_version > acc) {
            acc = this_version;
            acc_s = app_ver;
        }
    } while (FindNextFile(h_file, &file_info));

    if (acc == version::Semver200_version("0.0.0")) {
        return std::wstring();
    }

    our_dir.assign(find_root_app_dir());
    std::wstringstream ret;
    ret << our_dir << L"\\app-" << acc_s;

    FindClose(h_file);
    return ret.str();
}
