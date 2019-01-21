#include "coreclr.hpp"
#include "vendor/semver/semver200.h"

#include <algorithm>

using snap::core_clr_directory;
using snap::core_clr_instance;
using snap::core_clr_instance_t;

#if PLATFORM_WINDOWS
static const wchar_t* core_clr_dll = L"coreclr.dll";
static const wchar_t* core_clr_program_files_directory_path = L"%programfiles%\\dotnet\\shared\\microsoft.netcore.app";
#endif

int snap::coreclr::run(const std::wstring & executable_path, const std::vector<std::wstring>& arguments,
    const version::Semver200_version & clr_minimum_version)
{
    auto executable_file_exists = FALSE;
    if (!pal_fs_file_exists(executable_path.c_str(), &executable_file_exists)
        || !executable_file_exists)
    {
        LOG(ERROR) << "Coreclr: Executable does not exist. Path: " << executable_path << std::endl;
        return -1;
    }

    wchar_t* executable_working_directory = nullptr;
    if (!pal_fs_get_directory_name_full_path(executable_path.c_str(), &executable_working_directory))
    {
        LOG(ERROR) << "Coreclr: Unable to obtain directory full path for executable. Path: " << executable_path << std::endl;
        return -1;
    }

    const auto core_clr_instance = try_load_core_clr(executable_path, arguments, clr_minimum_version);
    if (core_clr_instance == nullptr)
    {
        LOG(ERROR) << "Coreclr: " << core_clr_dll << " not found." << std::endl;
        return -1;
    }

    const auto trusted_platform_assemblies_str = build_trusted_platform_assemblies_str(executable_path, core_clr_instance);
    if (trusted_platform_assemblies_str.empty())
    {
        LOG(ERROR) << "Coreclr: Unable to build trusted platform assemblies list (TPA)." << std::endl;
        return -1;
    }

    if(!core_clr_instance->initialize_coreclr(executable_path, executable_working_directory, trusted_platform_assemblies_str))
    {
        return -1;
    }

    std::stringstream core_clr_version;
    core_clr_version << core_clr_instance->get_directory().get_version();

    const auto argc = arguments.size();
    std::wstring arguments_buffer;
    for (auto i = 0u; i < argc; i++)
    {
        arguments_buffer.append(arguments[i]);
        arguments_buffer.append(L" ");
    }

    LOG(INFO) << "Coreclr: Executing assembly. " <<
        "Coreclr root directory: " << core_clr_instance->get_directory().get_root_path() << ". " <<
        "Coreclr dll: " << core_clr_instance->get_directory().get_dll_path() << ". " <<
        "Coreclr version: " << core_clr_version.str() << ". " <<
        "Assembly: " << executable_path << ". " <<
        "Assembly directory: " << executable_working_directory << ". " <<
        "Arguments count: " << argc << ". " <<
        "Arguments: " << arguments_buffer <<
        std::endl;

    auto exit_code = 0u;
    if(!core_clr_instance->execute_assembly(executable_path, arguments, &exit_code))
    {
        return -1;
    }

    LOG(INFO) << "Coreclr: Successfully executed assembly. " << std::endl;

    return exit_code;
}

core_clr_instance* snap::coreclr::try_load_core_clr(const std::wstring & executable_path, const std::vector<std::wstring>& arguments,
    const version::Semver200_version & clr_minimum_version)
{
    wchar_t* executable_directory_path = nullptr;
    wchar_t* core_root_path_out = nullptr;
    if (!pal_fs_get_directory_name_full_path(executable_path.c_str(), &executable_directory_path))
    {
        return nullptr;
    }

    // 1. Try loading from executable working directory if the application is self-contained.
    auto core_clr_instance = try_load_core_clr(executable_directory_path, version::Semver200_version());

    // 2. Try loading from default coreclr well-known directory.
#if PLATFORM_WINDOWS
    if (core_clr_instance == nullptr)
    {
        wchar_t* core_clr_program_files_directory_expanded = nullptr;
        if (pal_env_expand_str(core_clr_program_files_directory_path, &core_clr_program_files_directory_expanded)
            && core_clr_program_files_directory_expanded != nullptr)
        {
            const auto core_clr_directories = get_core_directories_from_path(
                core_clr_program_files_directory_expanded, clr_minimum_version);

            for (const auto& clr_directory : core_clr_directories)
            {
                core_clr_instance = try_load_core_clr(clr_directory.get_root_path(), clr_directory.get_version());
                if (core_clr_instance != nullptr)
                {
                    break;
                }
            }

            delete[] core_clr_program_files_directory_expanded;
        }
    }
#else
#error TODO: Find a well-known directory on Unix.
#endif

    delete[] executable_directory_path;
    delete[] core_root_path_out;

    return core_clr_instance;
}

std::vector<core_clr_directory> snap::coreclr::get_core_directories_from_path(const wchar_t* core_clr_root_path,
    const version::Semver200_version& clr_minimum_version)
{
    wchar_t** paths_out = nullptr;
    size_t paths_out_len = 0;

    std::vector<core_clr_directory> core_clr_directories;

    if (!pal_fs_list_directories(core_clr_root_path, &paths_out, &paths_out_len))
    {
        return core_clr_directories;
    }

    std::vector<wchar_t*> core_clr_paths(
        paths_out, paths_out + paths_out_len);

    for (const auto& core_clr_path : core_clr_paths)
    {
        wchar_t* clr_directory_core_clrversion = nullptr;
        if (!pal_fs_get_directory_name(core_clr_path, &clr_directory_core_clrversion))
        {
            delete[] core_clr_path;
            continue;
        }

        try
        {
            auto core_clr_version = version::Semver200_version(pal_str_narrow(clr_directory_core_clrversion));

            if (core_clr_version < clr_minimum_version) {
                continue;
            }

            wchar_t* core_clr_dll_path = nullptr;
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
            LOG(WARNING) << L"Coreclr: Failed to parse semver version for path: " << core_clr_path << ". Why: " << e.what() << std::endl;
        }

        delete[] core_clr_path;
    }

    std::sort(core_clr_directories.begin(), core_clr_directories.end(), [](const auto& lhs, const auto& rhs) {
        return lhs.get_version() < rhs.get_version();
        });

    return core_clr_directories;
}

core_clr_instance* snap::coreclr::try_load_core_clr(const wchar_t* directory_path, const version::Semver200_version& core_clr_version)
{
    wchar_t* core_clr_dll_path = nullptr;
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

    auto instance = new core_clr_instance(directory_path, core_clr_dll_path, core_clr_version);
    if(instance->try_load())
    {
        return instance;
    }

    delete instance;

    return nullptr;
}

std::vector<std::wstring> snap::coreclr::get_trusted_platform_assemblies(const wchar_t* trusted_platform_assemblies_path)
{
    if (trusted_platform_assemblies_path == nullptr)
    {
        return std::vector<std::wstring>();
    }

    static const std::vector<const wchar_t*> trusted_platform_assemblies_extension_list{
        L"*.ni.dll", // Probe for .ni.dll first so that it's preferred if ni and il coexist in the same dir
        L"*.dll",
        L"*.ni.exe",
        L"*.exe",
#if PLATFORM_WINDOWS
        L"*.ni.winmd",
        L"*.winmd"
#endif
    };

    std::vector<std::wstring> trusted_platform_assemblies_list;

    for (const auto& extension : trusted_platform_assemblies_extension_list)
    {
        wchar_t** files_out = nullptr;
        size_t files_out_len = 0;
        if (!pal_fs_list_files(trusted_platform_assemblies_path, nullptr, extension, &files_out, &files_out_len))
        {
            continue;
        }

        std::vector<wchar_t*> files(files_out, files_out + files_out_len);

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
    }

    return trusted_platform_assemblies_list;
}

std::wstring snap::coreclr::build_trusted_platform_assemblies_str(const std::wstring& executable_path,
    core_clr_instance* const core_clr_instance)
{
    if (core_clr_instance == nullptr)
    {
        return std::wstring();
    }

    std::vector<std::wstring> trusted_platform_assemblies;
    auto trusted_platform_assemblies_str = std::wstring();

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

    for (const auto& assembly : trusted_platform_assemblies)
    {
        trusted_platform_assemblies_str.append(assembly);
        trusted_platform_assemblies_str.append(PAL_CORECLR_TPA_SEPARATOR_WIDE_STR);
    }

    return trusted_platform_assemblies_str;
}
