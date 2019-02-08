#include "stubexecutable.hpp"
#include "vendor/semver/semver200.h"

using std::string;

int snap::stubexecutable::run(std::vector<std::string> arguments, const int cmd_show)
{
    auto app_name(find_own_executable_name());
    if (app_name.empty())
    {
        LOG(ERROR) << "Stubexecutable: Unable to determine own executable name." << std::endl;
        return -1;
    }

    auto working_dir(find_latest_app_dir());
    if (working_dir.empty())
    {
        LOG(ERROR) << "Stubexecutable: Unable to determine application working directory." << std::endl;
        return -1;
    }

    const auto executable_full_path(working_dir + PAL_CORECLR_TPA_SEPARATOR_STR + app_name);

    std::string cmd_line("\"");
    cmd_line += executable_full_path;
    cmd_line += "\" ";

    for (auto const& argument : arguments)
    {
        cmd_line += argument;
    }

#if PLATFORM_WINDOWS
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

    if (!create_process_result) {

        LOG(ERROR) << "Stubexecutable: Unable to create process. " << 
            "Error code: " << create_process_result << ". " <<
            "Executable: " << executable_full_path << ". " <<
            "Cmdline: " << working_dir << ". " <<
            "Current directory: " << lp_current_directory_utf16_string << ". " <<
            "Cmdshow: " << cmd_show << ". " <<
            std::endl;

        return -1;
    }

    LOG(INFO) << "Stubexecutable: Successfully created process. " << 
            "Executable: " << executable_full_path << ". " <<
            "Cmdline: " << cmd_line << ". " <<
            "Current directory: " << working_dir << ". " <<
            "Cmdshow: " << cmd_show << ". " <<
            std::endl;

    AllowSetForegroundWindow(pi.dwProcessId);
    WaitForInputIdle(pi.hProcess, 5 * 1000);
#else
    return -1;
#endif

    return 0;
}

std::string snap::stubexecutable::find_root_app_dir()
{
    char* current_directory_out = nullptr;
    if (!pal_fs_get_cwd(&current_directory_out))
    {
        return std::string();
    }

    return std::string(current_directory_out);
}

std::string snap::stubexecutable::find_own_executable_name()
{
    char* own_executable_name_out = nullptr;
    if (!pal_fs_get_own_executable_name(&own_executable_name_out))
    {
        return std::string();
    }

    return std::string(own_executable_name_out);
}

std::string snap::stubexecutable::find_latest_app_dir()
{
    auto root_app_directory(find_root_app_dir());
    if (root_app_directory.empty())
    {
        return std::string();
    }

    char** paths_out = nullptr;
    size_t paths_out_len = 0;
    if (!pal_fs_list_directories(root_app_directory.c_str(), nullptr, 
        nullptr, &paths_out, &paths_out_len))
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
        catch (const version::Parse_error& e)
        {
            LOG(WARNING) << "Stubexecutable: Unable to parse app version. Why: " << e.what() << ". Path: " << directory << std::endl;
            continue;
        }

        if (current_app_semver <= most_recent_semver) {
            continue;
        }

        most_recent_semver = current_app_semver;
    }

    root_app_directory.assign(find_root_app_dir());
    std::stringstream ret;
    ret << root_app_directory << PAL_DIRECTORY_SEPARATOR_STR << "app-" << most_recent_semver;

    return ret.str();
}
