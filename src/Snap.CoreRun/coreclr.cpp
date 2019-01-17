#include "coreclr.hpp"
#include "vendor/semver/semver200.h"

#include <algorithm>

using snap::core_clr_directory;

static const wchar_t* server_gc_environment_var = L"CORECLR_SERVER_GC";
static const wchar_t* concurrent_gc_environment_var = L"CORECLR_CONCURRENT_GC";
static const wchar_t* core_root_environment_var = L"CORE_ROOT";

static const wchar_t* core_clr_dll = L"coreclr.dll";
static const wchar_t* core_clr_system32_directory_path = L"%windir%\\system32";
static const wchar_t* core_clr_program_files_directory_path = L"%programfiles%\\dotnet\\shared\\microsoft.netcore.app";

int snap::coreclr::run(const std::wstring & executable_path, const std::vector<std::wstring>& arguments,
    const version::Semver200_version & clr_minimum_version)
{
    const auto core_clr_instance = static_cast<HMODULE>(try_load_core_clr(executable_path, arguments, clr_minimum_version));
    if (!core_clr_instance)
    {
        std::cerr << "ERROR - " << core_clr_dll << " not found." << std::endl;
        return -1;
    }

    ICLRRuntimeHost2* runtime_host;
    const auto pfn_get_clr_runtime_host =
        reinterpret_cast<FnGetCLRRuntimeHost>(::GetProcAddress(core_clr_instance, "GetCLRRuntimeHost"));

    if (!pfn_get_clr_runtime_host)
    {
        std::cerr << "ERROR - GetCLRRuntimeHost not found." << std::endl;
        return -1;
    }

    const auto hr = pfn_get_clr_runtime_host(IID_ICLRRuntimeHost2, reinterpret_cast<IUnknown**>(&runtime_host));
    if (FAILED(hr))
    {
        std::cerr << "Error - " << "Failed to get ICLRRuntimeHost2 instance.\nError code: " << hr << std::endl;
        return -1;
    }

    return 0;
}

void* snap::coreclr::try_load_core_clr(const std::wstring & executable_path, const std::vector<std::wstring>& arguments,
    const version::Semver200_version & clr_minimum_version)
{
    wchar_t* executable_directory_path = nullptr;
    size_t executable_directory_path_len = 0;
    wchar_t* core_root_path_out = nullptr;

    if (!pal_get_directory_name_full_path(executable_path.c_str(), &executable_directory_path, &executable_directory_path_len))
    {
        return nullptr;
    }

    // 1. Try loading from executable working directory since the application might be self-contained.
    auto core_clr_instance = static_cast<HMODULE>(try_load_core_clr(executable_directory_path));

    // 2. Try loading from CORE_ROOT environment variable.
    if (pal_get_environment_variable(core_root_environment_var, &core_root_path_out))
    {
        core_clr_instance = static_cast<HMODULE>(try_load_core_clr(core_root_path_out));
    }

    // 2. Try loading from system32 directory.
    if (!core_clr_instance)
    {
        core_clr_instance = static_cast<HMODULE>(try_load_core_clr(core_clr_system32_directory_path));
    }

    // 3. Try loading from default coreclr well-known directory.
    if (!core_clr_instance)
    {
        wchar_t core_clr_program_files_directory_expanded[MAX_PATH];
        const auto core_clr_program_files_directory_expanded_length = ::ExpandEnvironmentStringsW(core_clr_program_files_directory_path,
            core_clr_program_files_directory_expanded, MAX_PATH);

        if (core_clr_program_files_directory_expanded_length > 0)
        {
            const auto core_clr_directories = get_core_directories_from_path(
                core_clr_program_files_directory_expanded, clr_minimum_version);

            for (const auto& clr_directory : core_clr_directories)
            {
                core_clr_instance = static_cast<HMODULE>(try_load_core_clr(clr_directory.path.c_str()));
                if (core_clr_instance)
                {
                    break;
                }
            }
        }
    }

    delete executable_directory_path;
    delete core_root_path_out;

    return core_clr_instance;
}

std::vector<core_clr_directory> snap::coreclr::get_core_directories_from_path(const wchar_t* core_clr_root_path,
    const version::Semver200_version& clr_minimum_version)
{
    wchar_t** paths_out = nullptr;
    size_t paths_out_len = 0;

    std::vector<core_clr_directory> core_clr_directories;

    if (!pal_list_directories(core_clr_root_path, &paths_out, &paths_out_len))
    {
        return core_clr_directories;
    }

    std::vector<wchar_t*> core_clr_paths(
        paths_out, paths_out + paths_out_len);

    for (const auto& core_clr_path : core_clr_paths)
    {
        wchar_t* clr_directory_version = nullptr;
        size_t clr_directory_len = 0;
        if (!pal_get_directory_name(core_clr_path, &clr_directory_version, &clr_directory_len))
        {
            delete core_clr_path;
            continue;
        }

        version::Semver200_version version;
        char* multibyte_string_version = nullptr;

        try
        {
            pal_convert_from_utf16_to_utf8(clr_directory_version, static_cast<int>(clr_directory_len), &multibyte_string_version);

            core_clr_directory clr_directory;
            clr_directory.path = core_clr_path;
            clr_directory.version = version::Semver200_version(multibyte_string_version);

            if (clr_directory.version < clr_minimum_version) {
                delete core_clr_path;
                continue;
            }

            core_clr_directories.emplace_back(clr_directory);
        }
        catch (const version::Parse_error& e)
        {
            std::cerr << "Error - Failed to parse semver version for path: " << core_clr_path << ". Why: " << e.what() << std::endl;
        }

        delete multibyte_string_version;
    }

    std::sort(core_clr_directories.begin(), core_clr_directories.end(), [](const auto& lhs, const auto& rhs) {
        return lhs.version < rhs.version;
        });

    return core_clr_directories;
}

void* snap::coreclr::try_load_core_clr(const wchar_t* directory_path)
{
    wchar_t* core_clr_path = nullptr;
    size_t core_clr_path_len = 0;

    if (!pal_path_combine(directory_path, core_clr_dll, &core_clr_path, &core_clr_path_len))
    {
        return nullptr;
    }

    const auto core_clr_instance = LoadLibraryEx(core_clr_path, nullptr, 0);
    if (!core_clr_instance) {
        return nullptr;
    }

    // Pin the module - CoreCLR.dll does not support being unloaded.
    HMODULE core_clr_dummy_module;
    if (!::GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_PIN, core_clr_path, &core_clr_dummy_module)) {
        return nullptr;
    }

    delete core_clr_path;

    return static_cast<void*>(core_clr_instance);
}

STARTUP_FLAGS snap::coreclr::create_clr_startup_flags()
{
    auto initial_startup_flags =
        static_cast<STARTUP_FLAGS>(
            STARTUP_LOADER_OPTIMIZATION_SINGLE_DOMAIN |
            STARTUP_SINGLE_APPDOMAIN |
            STARTUP_CONCURRENT_GC);

    const auto read_flag_for_environment_variable = [&](const STARTUP_FLAGS startup_flag, const wchar_t *environment_variable) {
        auto is_flag_enabled = false;
        if (!pal_get_environment_variable_bool(environment_variable, &is_flag_enabled))
        {
            return;
        }

        if (is_flag_enabled)
        {
            initial_startup_flags = static_cast<STARTUP_FLAGS>(initial_startup_flags | startup_flag);
            return;
        }

        initial_startup_flags = static_cast<STARTUP_FLAGS>(initial_startup_flags & ~startup_flag);
    };

    read_flag_for_environment_variable(STARTUP_SERVER_GC, server_gc_environment_var);
    read_flag_for_environment_variable(STARTUP_CONCURRENT_GC, concurrent_gc_environment_var);

    return initial_startup_flags;
}
