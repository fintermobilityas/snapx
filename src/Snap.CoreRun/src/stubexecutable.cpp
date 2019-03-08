#include "stubexecutable.hpp"
#include "vendor/semver/semver200.h"

#include <string>
#include <iostream>

int snap::stubexecutable::run(std::vector<std::string> arguments, const int cmd_show)
{
    auto exit_code = 1;
    std::string executable_full_path;
    std::string app_dir_str;

    const auto app_name = this_exe::get_process_name();
    if (app_name.empty())
    {
        LOGE << "Error: Unable to find own executable name";
        return exit_code;
    }

    app_dir_str = find_current_app_dir();
    if (app_dir_str.empty())
    {
        LOGE << "Error: Unable to find current app dir";
        return exit_code;
    }

    executable_full_path = app_dir_str + PAL_DIRECTORY_SEPARATOR_C + app_name;

    const auto argc = static_cast<uint32_t>(arguments.size());
    const auto argv = new char* [argc];

    for (auto i = 0u; i < argc; i++)
    {
        argv[i] = _strdup(arguments[i].c_str());
    }

    LOGV << "Starting executable: " << executable_full_path << ". Arguments: " << this_exe::build_argv_str(argc, argv);

    pal_pid_t process_pid;
    if (pal_process_daemonize(executable_full_path.c_str(), app_dir_str.c_str(), static_cast<int>(argc), argv, cmd_show, &process_pid))
    {
        LOGV << "Process successfully started. Pid: " << process_pid;
        exit_code = 0;
    } else
    {
        LOGE << "Failed to start process."; 
    }

    return exit_code;
}

std::string snap::stubexecutable::find_current_app_dir()
{
    auto cwd = std::make_unique<char*>(nullptr);
    if (!pal_process_get_cwd(cwd.get()))
    {
        LOGE << "Failed to get current working directory";
        return std::string();
    }

    std::string app_dir(*cwd);

    auto paths_out = std::make_unique<char**>(nullptr);
    size_t paths_out_len = 0;
    if (!pal_fs_list_directories(app_dir.c_str(), nullptr, nullptr, paths_out.get(), &paths_out_len))
    {
        LOGE << "Failed to list directories inside app dir: " << app_dir;
        return std::string();
    }

    std::vector<char*> paths(*paths_out, *paths_out + paths_out_len);

    if (paths.empty())
    {
        LOGE << "Could not find any directories in: " << app_dir;
        return std::string();
    }

    std::string most_recent_semver_str("0.0.0");
    version::Semver200_version most_recent_semver(most_recent_semver_str);
    auto app_dir_found = false;

    for (const auto &full_path : paths)
    {
        auto directory_name = std::make_unique<char*>(nullptr);
        if (!pal_path_get_directory_name(full_path, directory_name.get()))
        {
            LOGE << "Unable to get directory name for directory: " << full_path;
            continue;
        }

        const auto directory_name_str = std::string(*directory_name);
        if (!pal_str_startswith(directory_name_str.c_str(), "app-"))
        {
            LOGV << "Skipping non-app directory: " << full_path;
            continue;
        }

        auto current_app_ver_str = std::string(directory_name_str).substr(4); // Skip 'app-'
        version::Semver200_version current_app_semver;

        try
        {
            current_app_semver = version::Semver200_version(current_app_ver_str);
        }
        catch (const version::Parse_error& e)
        {
            LOGE << "Semver parse error! App version: " << current_app_ver_str << ". Full path: " << full_path << ". Exception message: " << e.what();
            continue;
        }

        if (current_app_semver > most_recent_semver)
        {
            most_recent_semver = current_app_semver;
            most_recent_semver_str = current_app_ver_str;
            app_dir_found = true;
            continue;
        }
    }

    if(!app_dir_found)
    {
        return std::string();
    }

    const auto app_dir_version_str = "app-" + most_recent_semver_str;

    auto final_dir = std::make_unique<char*>(nullptr);
    if (!pal_path_combine(app_dir.c_str(), app_dir_version_str.c_str(), final_dir.get()))
    {
        LOGE << "Error! Unable to build final dir. App dir: " << app_dir << ". App dir version: " << app_dir_version_str;
        return std::string();
    }

    std::string final_dir_str(*final_dir);
    LOGV << "Final app dir: " << final_dir_str;
    return final_dir_str;
}
