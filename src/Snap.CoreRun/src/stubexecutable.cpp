#include "stubexecutable.hpp"

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
    const auto argc = static_cast<int>(arguments.size());
    const auto argv = new char*[argc];
    int exit_code = 1;
    std::string executable_full_path;
    std::string app_name_str;
    std::string app_dir_str;

    char* app_name = nullptr;
    if (!pal_fs_get_own_executable_name(&app_name))
    {
        std::cerr << "Error: Unable to find own executable name" << std::endl;
        goto done;
    }

    app_dir_str = find_current_app_dir();
    if (app_dir_str.empty())
    {
        std::cerr << "Error: Unable to find latest app dir" << std::endl;
        goto done;
    }

    app_name_str = std::string(app_name);
    executable_full_path = app_dir_str + PAL_DIRECTORY_SEPARATOR_C + app_name_str;

    for (auto i = 0; i < argc; i++)
    {
        argv[i] = _strdup(arguments[i].c_str());
    }

    pal_pid_t process_pid;
    if (pal_process_daemonize(executable_full_path.c_str(), app_dir_str.c_str(), argc, argv, cmd_show, &process_pid)
        && process_pid > 0)
    {
        exit_code = 0;
    }

done:
    if (app_name != nullptr)
    {
        delete app_name;
    }
    delete[] argv;
    return exit_code;
}

std::string snap::stubexecutable::find_current_app_dir()
{
    char* cwd = nullptr;
    if (!pal_fs_get_cwd(&cwd))
    {
        return std::string();
    }

    std::string app_dir(cwd);
    delete cwd;
    cwd = nullptr;

    char** paths_out = nullptr;
    size_t paths_out_len = 0;
    if (!pal_fs_list_directories(app_dir.c_str(), nullptr, nullptr, &paths_out, &paths_out_len))
    {
        return std::string();
    }

    std::vector<char*> paths(paths_out, paths_out + paths_out_len);
    delete[] paths_out;
    paths_out = nullptr;

    if (paths.empty())
    {
        return std::string();
    }

    std::string most_recent_semver_str("0.0.0");
    version::Semver200_version most_recent_semver(most_recent_semver_str);

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

        auto current_app_ver_str = std::string(directory_name).substr(4); // Skip 'app-'
        version::Semver200_version current_app_semver;

        try
        {
            current_app_semver = version::Semver200_version(current_app_ver_str);
        }
        catch (const version::Parse_error&)
        {
            continue;
        }

        if (current_app_semver > most_recent_semver)
        {
            most_recent_semver = current_app_semver;
            most_recent_semver_str = current_app_ver_str;
            continue;
        }
    }

    char* final_dir = nullptr;
    if (!pal_fs_path_combine(app_dir.c_str(), ("app-" + most_recent_semver_str).c_str(), &final_dir))
    {
        return std::string();
    }

    std::string final_dir_str(final_dir);
    delete final_dir;
    return final_dir_str;
}
