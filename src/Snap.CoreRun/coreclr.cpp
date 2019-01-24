#include "coreclr.hpp"
#include "vendor/semver/semver200.h"

#include <algorithm>
#include <cassert>

using snap::core_clr_directory;
using snap::core_clr_instance;
using snap::core_clr_instance_t;

#if PLATFORM_WINDOWS
static const char* core_clr_dll = "coreclr.dll";
static const char* core_clr_program_files_directory_path = R"(%ProgramW6432%\dotnet\shared\microsoft.netcore.app)";
#elif PLATFORM_LINUX
static const char* core_clr_dll = "libcoreclr.so";
// Default location on Ubuntu
static const char* core_clr_usr_share_dotnet_path = "/usr/share/dotnet/shared/Microsoft.NETCore.App"; // NB! case-insensitive
#endif

int snap::coreclr::run(const std::string& this_executable_path, const std::string & dotnet_executable_path, const std::vector<std::string>& arguments,
    const version::Semver200_version & clr_minimum_version)
{
    auto dotnet_executable_file_exists = FALSE;
    if (!pal_fs_file_exists(dotnet_executable_path.c_str(), &dotnet_executable_file_exists)
        || !dotnet_executable_file_exists)
    {
        LOG(ERROR) << "Coreclr: Executable does not exist. Path: " << dotnet_executable_path << std::endl;
        return -1;
    }

    char* dotnet_executable_working_directory = nullptr;
    if (!pal_fs_get_directory_name_absolute_path(dotnet_executable_path.c_str(), &dotnet_executable_working_directory))
    {
        LOG(ERROR) << "Coreclr: Unable to obtain directory full path for executable. Path: " << dotnet_executable_path << std::endl;
        return -1;
    }

    const auto core_clr_instance = try_load_core_clr(dotnet_executable_path, dotnet_executable_working_directory, arguments, clr_minimum_version);
    if (core_clr_instance == nullptr)
    {
        LOG(ERROR) << "Coreclr: " << core_clr_dll << " not found." << std::endl;
        return -1;
    }

    const auto trusted_platform_assemblies_str = build_trusted_platform_assemblies_str(dotnet_executable_path, core_clr_instance);
    if (trusted_platform_assemblies_str.empty())
    {
        LOG(ERROR) << "Coreclr: Unable to build trusted platform assemblies list (TPA)." << std::endl;
        return -1;
    }

    if (!core_clr_instance->initialize_coreclr(this_executable_path, dotnet_executable_path, dotnet_executable_working_directory, trusted_platform_assemblies_str))
    {
        return -1;
    }

    std::stringstream core_clr_version;
    core_clr_version << core_clr_instance->get_directory().get_version();

    const auto argc = arguments.size();
    std::string arguments_buffer;
    for (auto i = 0u; i < argc; i++)
    {
        arguments_buffer.append(arguments[i]);
        arguments_buffer.append(" ");
    }

    LOG(INFO) << "Coreclr: Executing assembly. " <<
        "Coreclr root directory: " << core_clr_instance->get_directory().get_root_path() << ". " <<
        "Coreclr dll: " << core_clr_instance->get_directory().get_dll_path() << ". " <<
        "Coreclr version: " << core_clr_version.str() << ". " <<
        "Assembly: " << dotnet_executable_path << ". " <<
        "Assembly working directory: " << dotnet_executable_working_directory << ". " <<
        "Assembly arguments count: " << argc << ". " <<
        "Assembly arguments: " << arguments_buffer <<
        std::endl;

    auto coreclr_exit_code = 0u;
    auto dotnet_exit_exit_code = 0;
    if (!core_clr_instance->execute_assembly(dotnet_executable_path, arguments, &coreclr_exit_code, &dotnet_exit_exit_code))
    {
        return -1;
    }

    LOG(INFO) << "Coreclr: Successfully executed assembly. " 
              << "Coreclr exit code: " << coreclr_exit_code << ". " 
              << "Dotnet assembly exit code: " << dotnet_exit_exit_code << ". ";

    return dotnet_exit_exit_code;
}

core_clr_instance* snap::coreclr::try_load_core_clr(const std::string & executable_path, const char* executable_working_directory, const std::vector<std::string>& arguments,
    const version::Semver200_version & clr_minimum_version)
{

    // 1. Try loading from executable working directory if the application is self-contained.
    auto core_clr_instance = try_load_core_clr(executable_working_directory, version::Semver200_version());
    if (core_clr_instance != nullptr)
    {
        return core_clr_instance;
    }

    // 2. Try loading from default coreclr well-known directory.
    char* core_clr_well_known_directory = nullptr;
#if PLATFORM_WINDOWS
    if (!pal_env_expand_str(core_clr_program_files_directory_path, &core_clr_well_known_directory))
    {
        return nullptr;
    }
#else
    auto core_root_env_variable_exists = pal_env_get_variable("CORE_ROOT", &core_clr_well_known_directory);
    auto core_root_dir_exists = FALSE;

    if (core_root_env_variable_exists)
    {
        pal_fs_directory_exists(core_clr_well_known_directory, &core_root_dir_exists);
    }

    if (!core_root_dir_exists)
    {
        assert(core_clr_well_known_directory == nullptr);
        core_clr_well_known_directory = new char[strlen(core_clr_usr_share_dotnet_path)];
        strcpy(core_clr_well_known_directory, core_clr_usr_share_dotnet_path);
    }

#endif

    assert(core_clr_well_known_directory != nullptr);

    const auto core_clr_directories = get_core_directories_from_path(
        core_clr_well_known_directory, clr_minimum_version);

    for (const auto& clr_directory : core_clr_directories)
    {
        core_clr_instance = try_load_core_clr(clr_directory.get_root_path(), clr_directory.get_version());
        if (core_clr_instance != nullptr)
        {
            break;
        }
    }


    delete[] core_clr_well_known_directory;

    return core_clr_instance;
}

std::vector<core_clr_directory> snap::coreclr::get_core_directories_from_path(const char* core_clr_root_path,
    const version::Semver200_version& clr_minimum_version)
{
    if(clr_minimum_version.empty())
    {
        LOG(WARNING) << "Clr minimum version is empty: " << clr_minimum_version << ". " 
                     << "Skipping searching core clr directories in path: " << core_clr_root_path << ". ";
        return std::vector<core_clr_directory>();
    }

    char** directories_out = nullptr;
    size_t directories_out_len = 0;
    if (!pal_fs_list_directories(core_clr_root_path, nullptr,
        nullptr, &directories_out, &directories_out_len))
    {
        return std::vector<core_clr_directory>();
    }

    std::vector<core_clr_directory> core_clr_directories;
    std::vector<char*> core_clr_paths(
        directories_out, directories_out + directories_out_len);

    for (const auto& core_clr_path : core_clr_paths)
    {
        char* clr_directory_core_clrversion = nullptr;
        if (!pal_fs_get_directory_name(core_clr_path, &clr_directory_core_clrversion))
        {
            delete[] core_clr_path;
            continue;
        }

        try
        {
            auto core_clr_version = version::Semver200_version(clr_directory_core_clrversion);

            if (clr_minimum_version > core_clr_version) {
                continue;
            }

            char* core_clr_dll_path = nullptr;
            if (!pal_fs_path_combine(core_clr_path, core_clr_dll, &core_clr_dll_path))
            {
                continue;
            }

            auto core_clr_dll_exists = FALSE;
            if (!pal_fs_file_exists(core_clr_dll_path, &core_clr_dll_exists)
                || !core_clr_dll_exists)
            {
                continue;
            }

            core_clr_directories.emplace_back(
                core_clr_directory(core_clr_path, core_clr_dll_path, core_clr_version));

            delete[] core_clr_dll_path;
        }
        catch (const version::Parse_error& e)
        {
            LOG(WARNING) << "Coreclr: Failed to parse semver version for path: " << core_clr_path << ". Why: " << e.what() << std::endl;
        }

        delete[] core_clr_path;
    }

    std::sort(core_clr_directories.begin(), core_clr_directories.end(), [](const auto& lhs, const auto& rhs) {
        return lhs.get_version() < rhs.get_version();
        });

    return core_clr_directories;
}

core_clr_instance* snap::coreclr::try_load_core_clr(const char* directory_path, const version::Semver200_version& core_clr_version)
{

    char* core_clr_dll_path = nullptr;
    if (!pal_fs_path_combine(directory_path, core_clr_dll, &core_clr_dll_path))
    {
        return nullptr;
    }

    auto core_clr_dll_exists = FALSE;
    if (!pal_fs_file_exists(core_clr_dll_path, &core_clr_dll_exists)
        || !core_clr_dll_exists)
    {
        return nullptr;
    }

    LOG(TRACE) << "Attempting to load: " << core_clr_dll_path;

    auto instance = new core_clr_instance(directory_path, core_clr_dll_path, core_clr_version);
    if (instance->try_load())
    {
        return instance;
    }

    delete instance;

    return nullptr;
}

std::vector<std::string> snap::coreclr::get_trusted_platform_assemblies(const char* trusted_platform_assemblies_path)
{
    if (trusted_platform_assemblies_path == nullptr)
    {
        return std::vector<std::string>();
    }

    LOG(TRACE) << "Adding TPAs from: " << trusted_platform_assemblies_path;

    static const std::vector<const char*> trusted_platform_assemblies_extension_list{
#if PLATFORM_WINDOWS
        "*.ni.dll", // Probe for .ni.dll first so that it's preferred if ni and il coexist in the same dir
        "*.dll",
        "*.ni.exe",
        "*.exe",
        "*.ni.winmd",
        "*.winmd"
#else
        ".dll",
        ".exe"
#endif
    };

    std::vector<std::string> trusted_platform_assemblies_list;

    for (const auto& extension : trusted_platform_assemblies_extension_list)
    {
        char** files_out = nullptr;
        size_t files_out_len = 0;
        if (!pal_fs_list_files(trusted_platform_assemblies_path, nullptr, extension, &files_out, &files_out_len))
        {
            continue;
        }

        std::vector<char*> files(files_out, files_out + files_out_len);

        for (auto const& filename : files)
        {
            const auto is_previously_added = std::find(
                trusted_platform_assemblies_list.begin(),
                trusted_platform_assemblies_list.end(), filename) != trusted_platform_assemblies_list.end();
            if (is_previously_added)
            {
                continue;
            }

            trusted_platform_assemblies_list.emplace_back(filename);
        }

        delete[] files_out;
    }

    LOG(TRACE) << "Successfully added " << trusted_platform_assemblies_list.size() << " assemblies to TPA list.";

    return trusted_platform_assemblies_list;
}

std::string snap::coreclr::build_trusted_platform_assemblies_str(const std::string& executable_path,
    core_clr_instance* const core_clr_instance)
{
    if (core_clr_instance == nullptr)
    {
        return std::string();
    }

    LOG(TRACE) << "Building TPA assemblies string.";

    std::vector<std::string> trusted_platform_assemblies;

    for (auto const& tpa : get_trusted_platform_assemblies(core_clr_instance->get_directory().get_root_path()))
    {
        trusted_platform_assemblies.emplace_back(tpa);
    }

    const auto is_managed_assembly_added_to_tpa = std::find(
        trusted_platform_assemblies.begin(),
        trusted_platform_assemblies.end(), executable_path) != trusted_platform_assemblies.end();
    if (!is_managed_assembly_added_to_tpa)
    {
        trusted_platform_assemblies.emplace_back(executable_path);
    }

    std::string trusted_platform_assemblies_str = std::string();

    for (const auto& assembly : trusted_platform_assemblies)
    {
        trusted_platform_assemblies_str.append(assembly);
        trusted_platform_assemblies_str.append(PAL_CORECLR_TPA_SEPARATOR_STR);
    }

    LOG(TRACE) << "Successfully built TPA assemblies string.";

    return trusted_platform_assemblies_str;
}
