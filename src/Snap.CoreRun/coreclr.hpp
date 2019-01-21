#pragma once

#include "corerun.hpp"
#include "vendor/semver/semver200.h"
#include "vendor/coreclr/coreclrhost.hpp"

#include <vector>
#include <iostream>

namespace snap {

    using std::wstring;

    typedef void core_clr_instance_t;

    class core_clr_directory
    {
    private:

        std::wstring m_dll_path;
        std::wstring m_root_path;
        version::Semver200_version m_version;

    public:
        core_clr_directory()
        {

        }

        core_clr_directory(std::wstring root_path, std::wstring dll_path, version::Semver200_version version) :
            m_root_path(std::move(root_path)),
            m_dll_path(std::move(dll_path)),
            m_version(std::move(version))
        {

        }

        core_clr_directory(const wchar_t* root_path, const wchar_t* dll_path, version::Semver200_version version) :
            core_clr_directory(std::wstring(root_path), std::wstring(dll_path), version)
        {

        }

        const wchar_t* get_dll_path() const
        {
            return m_dll_path.c_str();
        }

        const wchar_t* get_root_path() const
        {
            return m_root_path.c_str();
        }

        version::Semver200_version get_version() const
        {
            return m_version;
        }

    };

    class core_clr_instance
    {
    private:

        core_clr_directory m_directory;
        BOOL m_loaded;
        BOOL m_initialized;
        void* m_coreclr_dll_ptr;
        void* m_coreclr_host_handle;
        coreclr_initialize_ptr m_coreclr_initialize;
        coreclr_shutdown_ptr m_coreclr_shutdown;
        coreclr_shutdown_2_ptr m_coreclr_shutdown_2;
        coreclr_create_delegate_ptr m_coreclr_create_delegate;
        coreclr_execute_assembly_ptr m_coreclr_execute_assembly;
        wchar_t* m_coreclr_property_keys;
        wchar_t* m_coreclr_property_values;
        std::wstring m_coreclr_appdomain_friendly_name;
        unsigned int m_coreclr_appdomain_id;

    public:
        core_clr_instance() = delete;

        core_clr_instance(const core_clr_directory& directory) : core_clr_instance(directory.get_root_path(), directory.get_dll_path(), directory.get_version())
        {

        }

        core_clr_instance(const wchar_t* core_clr_root_path, const wchar_t* core_clr_dll_path, const version::Semver200_version core_clr_version) :
            m_directory(core_clr_directory(core_clr_root_path, core_clr_dll_path, core_clr_version)),
            m_loaded(FALSE),
            m_initialized(FALSE),
            m_coreclr_dll_ptr(nullptr),
            m_coreclr_host_handle(nullptr),
            m_coreclr_initialize(nullptr),
            m_coreclr_shutdown(nullptr),
            m_coreclr_shutdown_2(nullptr),
            m_coreclr_create_delegate(nullptr),
            m_coreclr_execute_assembly(nullptr),
            m_coreclr_property_keys(nullptr),
            m_coreclr_property_values(nullptr),
            m_coreclr_appdomain_id(0u)
        {

        }

        core_clr_directory get_directory() const
        {
            return m_directory;
        }

        BOOL is_loaded()
        {
            return m_loaded;
        }

        BOOL try_load()
        {
            if (m_loaded)
            {
                return TRUE;
            }

            const auto dll_path = m_directory.get_dll_path();

#if PLATFORM_WINDOWS
            m_coreclr_dll_ptr = LoadLibraryEx(dll_path, nullptr, 0);
            if (!m_coreclr_dll_ptr) {
                return FALSE;
            }

            // Pin the module since CoreCLR.dll does not support being unloaded.
            HMODULE core_clr_dummy_module;
            if (!::GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_PIN, dll_path, &core_clr_dummy_module)) {
                return FALSE;
            }

            m_coreclr_initialize = reinterpret_cast<coreclr_initialize_ptr>(::GetProcAddress(static_cast<HMODULE>(m_coreclr_dll_ptr), "coreclr_initialize"));
            m_coreclr_shutdown = reinterpret_cast<coreclr_shutdown_ptr>(::GetProcAddress(static_cast<HMODULE>(m_coreclr_dll_ptr), "coreclr_shutdown"));
            m_coreclr_shutdown_2 = reinterpret_cast<coreclr_shutdown_2_ptr>(::GetProcAddress(static_cast<HMODULE>(m_coreclr_dll_ptr), "coreclr_shutdown_2"));
            m_coreclr_create_delegate = reinterpret_cast<coreclr_create_delegate_ptr>(::GetProcAddress(static_cast<HMODULE>(m_coreclr_dll_ptr), "coreclr_create_delegate"));
            m_coreclr_execute_assembly = reinterpret_cast<coreclr_execute_assembly_ptr>(::GetProcAddress(static_cast<HMODULE>(m_coreclr_dll_ptr), "coreclr_execute_assembly"));
#endif

            m_loaded = m_coreclr_initialize != nullptr
                && m_coreclr_shutdown != nullptr
                && m_coreclr_shutdown_2 != nullptr
                && m_coreclr_create_delegate != nullptr
                && m_coreclr_execute_assembly != nullptr;

            return m_loaded;
        }

        BOOL initialize_coreclr(const std::wstring& executable_path, const wchar_t* executable_working_directory,
            const std::wstring& trusted_platform_assemblies)
        {
            if (executable_path.empty()
                || executable_working_directory == nullptr
                || trusted_platform_assemblies.empty()
                || !is_loaded()
                || m_initialized)
            {
                return FALSE;
            }

            const auto friendly_name_last_slash = executable_path.find_last_of(PAL_DIRECTORY_SEPARATOR_STR);
            m_coreclr_appdomain_friendly_name = executable_path.substr(friendly_name_last_slash + 1);

            const auto target_app = std::string(pal_str_narrow(executable_path.c_str()));
            const auto target_app_path = std::string(pal_str_narrow(executable_working_directory));
            const auto target_app_paths = std::string(target_app_path);
            const auto app_paths = std::string(target_app_path);
            const auto coreclr_root_path = std::string(pal_str_narrow(m_directory.get_root_path()));
            const auto trusted_platform_assemblies_utf8 = std::string(pal_str_narrow(trusted_platform_assemblies.c_str()));
            const auto appdomain_friendly_name_utf8 = std::string(pal_str_narrow(m_coreclr_appdomain_friendly_name.c_str()));

            // App (NI) paths are the paths that will be probed for native images not found on the TPA list. 
            auto app_ni_paths = std::string(target_app_path);
            app_ni_paths.append(PAL_CORECLR_TPA_SEPARATOR_NARROW_STR);
            app_ni_paths.append(target_app_path);
            app_ni_paths.append("NI");

            auto native_dll_search_directories = std::string(app_paths);
            native_dll_search_directories.append(PAL_CORECLR_TPA_SEPARATOR_NARROW_STR);
            native_dll_search_directories.append(coreclr_root_path);

#if PLATFORM_WINDOWS
            auto platform_resource_roots = std::string(app_paths);
#endif

            const char *property_keys[] = {
               "TRUSTED_PLATFORM_ASSEMBLIES",
               "APP_PATHS",
               "APP_NI_PATHS",
               "NATIVE_DLL_SEARCH_DIRECTORIES",
#if PLATFORM_WINDOWS
               "APP_LOCAL_WINMETADATA"
#endif
            };

            const char *property_values[] = {
                // TRUSTED_PLATFORM_ASSEMBLIES
                trusted_platform_assemblies_utf8.c_str(),
                // APP_PATHS
                app_paths.c_str(),
                // APP_NI_PATHS
                app_ni_paths.c_str(),
                // NATIVE_DLL_SEARCH_DIRECTORIES
                native_dll_search_directories.c_str(),
#if PLATFORM_WINDOWS
                // APP_LOCAL_WINMETADATA
                platform_resource_roots.c_str()
#endif
            };

            int st = fn_initialize()(
                target_app.c_str(),
                appdomain_friendly_name_utf8.c_str(),
                sizeof(property_keys) / sizeof(property_keys[0]),
                property_keys,
                property_values,
                &m_coreclr_host_handle,
                &m_coreclr_appdomain_id);

            if (!(st >= 0))
            {
                LOG(ERROR) << "Coreclr: coreclr_initialize failed. Status: " << st << std::endl;
                return FALSE;
            }

            m_initialized = TRUE;

            return TRUE;
        }

        BOOL execute_assembly(const std::wstring& executable_path, const std::vector<std::wstring>& arguments, unsigned int* exit_code)
        {
            if (!m_initialized)
            {
                return FALSE;
            }

            auto to_clr_arguments = [arguments]()
            {
                const auto arguments_len = static_cast<int>(arguments.size());
                auto argv = new char*[arguments_len];

                for (auto i = 0; i < arguments_len; i++)
                {
                    argv[i] = pal_str_narrow(arguments[i].c_str());
                }

                return argv;
            };

            const auto argc = static_cast<int>(arguments.size());
            const auto argv = to_clr_arguments();
            const auto target_app = std::string(pal_str_narrow(executable_path.c_str()));

            auto st = fn_execute_assembly()(
                m_coreclr_host_handle,
                m_coreclr_appdomain_id,
                argc,
                const_cast<const char**>(argv),
                target_app.c_str(),
                exit_code);

            if (!(st >= 0))
            {
                LOG(ERROR) << "Coreclr: coreclr_execute_assembly failed. Status: " << st << std::endl;
                return FALSE;
            }

            delete[] argv;

            return TRUE;
        }

    private:

        coreclr_initialize_ptr fn_initialize()
        {
            return m_coreclr_initialize;
        }

        coreclr_shutdown_ptr fn_shutdown()
        {
            return m_coreclr_shutdown;
        }

        coreclr_shutdown_2_ptr fn_shutdown2()
        {
            return m_coreclr_shutdown_2;
        }

        coreclr_create_delegate_ptr fn_create_delegate()
        {
            return m_coreclr_create_delegate;
        }

        coreclr_execute_assembly_ptr fn_execute_assembly()
        {
            return m_coreclr_execute_assembly;
        }

    };

    class coreclr
    {
    public:

        static int run(const std::wstring& executable_path, const std::vector<std::wstring>& arguments,
            const version::Semver200_version& clr_minimum_version);

    private:

        static std::vector<core_clr_directory> get_core_directories_from_path(const wchar_t* core_clr_root_path,
            const version::Semver200_version& clr_minimum_version);

        static core_clr_instance* try_load_core_clr(const std::wstring & executable_path, const std::vector<std::wstring>& arguments,
            const version::Semver200_version & clr_minimum_version);

        static core_clr_instance* try_load_core_clr(const wchar_t* directory_path, const version::Semver200_version& core_clr_version);

        static std::vector<std::wstring> get_trusted_platform_assemblies(const wchar_t* trusted_platform_assemblies_path);

        static std::wstring build_trusted_platform_assemblies_str(const std::wstring& executable_path,
            core_clr_instance* const core_clr_instance);

    };
};
