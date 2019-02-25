#include "stubexecutable.hpp"
#include "vendor/semver/semver200.h"

using std::string;

inline std::string join(const std::vector<std::string>& strings, const char* delimiter = " ")
{
    std::string str = std::string();
    for (auto const& s : strings)
    {
        str += s + delimiter;
    }
    return str;
}

int snap::stubexecutable::run(std::vector<std::string> arguments, const int cmd_show)
{
    auto app_name(find_own_executable_name());
    if (app_name.empty())
    {
        std::cerr << "Error: Unable to find own executable name" << std::endl;
        return -1;
    }

    auto working_dir(find_latest_app_dir());
    if (working_dir.empty())
    {
        std::cerr << "Error: Unable to find latest app dir" << std::endl;
        return -1;
    }

    std::cout << "Working directory: " << working_dir << std::endl;

    const auto executable_full_path(working_dir + PAL_DIRECTORY_SEPARATOR_C + app_name);
    arguments.insert(arguments.begin(), executable_full_path);

    const auto argc = static_cast<int>(arguments.size());
    const auto argv = new char*[argc];

    std::cout << "Executable: " << executable_full_path << std::endl;
    std::cout << "Arguments: " << join(arguments, " ") << std::endl;

    for (auto i = 0; i < argc; i++)
    {
        argv[i] = strdup(arguments[i].c_str());
    }

    auto process_pid = 0;
    if(!pal_process_daemonize(executable_full_path.c_str(), working_dir.c_str(), argc, argv, cmd_show, &process_pid)
        || process_pid <= 0)
    {
        delete[] argv;
        return -1;
    }

    delete[] argv;
    return 0;
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

        version::Semver200_version current_app_semver;

        try
        {
            current_app_semver = version::Semver200_version(current_app_ver);
        }
        catch (const version::Parse_error&)
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
