#pragma once

#include "pal.hpp"
#include "semver200.h"
#include "coreclrhost.hpp"

#include <vector>
#include <iostream>

namespace snap {

    using std::string;

    typedef void core_clr_instance_t;

    class core_clr_directory
    {
    private:

        std::string m_root_path;
        std::string m_dll_path;
        version::Semver200_version m_version;

    public:
        core_clr_directory()
        {

        }

        core_clr_directory(std::string root_path, std::string dll_path, version::Semver200_version version) :
            m_root_path(std::move(root_path)),
            m_dll_path(std::move(dll_path)),
            m_version(std::move(version))
        {

        }

        core_clr_directory(const char* root_path, const char* dll_path, version::Semver200_version version) :
            core_clr_directory(std::string(root_path), std::string(dll_path), version)
        {

        }

        const char* get_dll_path() const
        {
            return m_dll_path.c_str();
        }

        const char* get_root_path() const
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
        coreclr_shutdown_2_ptr m_coreclr_shutdown_2;
        coreclr_execute_assembly_ptr m_coreclr_execute_assembly;
        std::string m_coreclr_appdomain_friendly_name;
        unsigned int m_coreclr_appdomain_id;

    public:
        core_clr_instance() = delete;

        core_clr_instance(const core_clr_directory& directory) : core_clr_instance(directory.get_root_path(), directory.get_dll_path(), directory.get_version())
        {

        }

        core_clr_instance(const char* core_clr_root_path, const char* core_clr_dll_path, const version::Semver200_version core_clr_version) :
            m_directory(core_clr_directory(core_clr_root_path, core_clr_dll_path, core_clr_version)),
            m_loaded(FALSE),
            m_initialized(FALSE),
            m_coreclr_dll_ptr(nullptr),
            m_coreclr_host_handle(nullptr),
            m_coreclr_initialize(nullptr),
            m_coreclr_shutdown_2(nullptr),
            m_coreclr_execute_assembly(nullptr),
            m_coreclr_appdomain_id(0u)
        {

        }

        ~core_clr_instance()
        {
            if (!m_loaded || m_coreclr_dll_ptr == nullptr)
            {
                return;
            }

            if (!pal_free_library(m_coreclr_dll_ptr))
            {
                LOG(WARNING) << "Failed to free coreclr library";
            }

            m_coreclr_dll_ptr = nullptr;
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

            BOOL pinning_required = FALSE;
#if PLATFORM_WINDOWS
            pinning_required = TRUE;
#endif

            if (!pal_load_library(m_directory.get_dll_path(), pinning_required, &m_coreclr_dll_ptr))
            {
                return FALSE;
            }

            LOG(TRACE) << "Successfully loaded coreclr dll: " << m_directory.get_dll_path();

            void* coreclr_initialize_void_ptr = nullptr;
            if (!pal_getprocaddress(m_coreclr_dll_ptr, "coreclr_initialize", &coreclr_initialize_void_ptr)) {
                LOG(ERROR) << "Failed to load function: m_coreclr_initialize";
                return FALSE;
            }

            void* coreclr_shutdown_2_void_ptr = nullptr;
            if (!pal_getprocaddress(m_coreclr_dll_ptr, "coreclr_shutdown_2", &coreclr_shutdown_2_void_ptr)) {
                LOG(ERROR) << "Failed to load function: coreclr_shutdown_2";
                return FALSE;
            }

            void* coreclr_execute_assembly_void_ptr = nullptr;
            if (!pal_getprocaddress(m_coreclr_dll_ptr, "coreclr_execute_assembly", &coreclr_execute_assembly_void_ptr)) {
                LOG(ERROR) << "Failed to load function: coreclr_execute_assembly";
                return FALSE;
            }

            LOG(TRACE) << "Successfully loaded symbols from coreclr dll. Addr: " << coreclr_initialize_void_ptr;

            m_coreclr_initialize = reinterpret_cast<coreclr_initialize_ptr>(coreclr_initialize_void_ptr);
            m_coreclr_shutdown_2 = reinterpret_cast<coreclr_shutdown_2_ptr>(coreclr_shutdown_2_void_ptr);
            m_coreclr_execute_assembly = reinterpret_cast<coreclr_execute_assembly_ptr>(coreclr_execute_assembly_void_ptr);

            m_loaded = m_coreclr_initialize != nullptr
                && m_coreclr_shutdown_2 != nullptr
                && m_coreclr_execute_assembly != nullptr;

            return m_loaded;
        }

        BOOL initialize_coreclr(const std::string& this_executable_path, const std::string& dotnet_executable_path, const char* dotnet_executable_working_directory,
            const std::string& trusted_platform_assemblies)
        {
            if (dotnet_executable_path.empty()
                || dotnet_executable_working_directory == nullptr
                || trusted_platform_assemblies.empty()
                || !is_loaded()
                || m_initialized)
            {
                return FALSE;
            }

            const auto app_domain_friendly_name_last_slash_pos = dotnet_executable_path.find_last_of(PAL_DIRECTORY_SEPARATOR_STR);
            if (app_domain_friendly_name_last_slash_pos == std::string::npos)
            {
                LOG(ERROR) << "Unable to determine appdomain name using executable name: " << dotnet_executable_path;
                return FALSE;
            }

            m_coreclr_appdomain_friendly_name = dotnet_executable_path.substr(app_domain_friendly_name_last_slash_pos + 1);

            std::string target_app;
            target_app.assign(dotnet_executable_path);

            std::string target_app_path;
            target_app_path.assign(dotnet_executable_working_directory);

            std::string target_app_paths;
            target_app_paths.assign(target_app_path);

            std::string app_paths;
            app_paths.assign(target_app_path);

            std::string coreclr_root_path;
            coreclr_root_path.assign(m_directory.get_root_path());

            std::string app_ni_paths;
#if PLATFORM_WINDOWS
            // App (NI) paths are the paths that will be probed for native images not found in the TPA list. 
            app_ni_paths.assign(target_app_path);
            app_ni_paths.append(PAL_CORECLR_TPA_SEPARATOR_STR);
            app_ni_paths.append(target_app_path);
            app_ni_paths.append("NI");
#elif PLATFORM_LINUX
            app_ni_paths.assign(dotnet_executable_working_directory);
#endif

            std::string native_dll_search_directories;
            native_dll_search_directories.assign(PAL_CORECLR_TPA_SEPARATOR_STR);
            native_dll_search_directories.append(app_paths);
            native_dll_search_directories.append(coreclr_root_path);

#if PLATFORM_WINDOWS
            std::string platform_resource_roots;
            platform_resource_roots.assign(app_paths);
#elif PLATFORM_LINUX

            auto use_server_gc_bool = pal_env_get_variable_bool("COMPlus_gcServer");
            auto use_globalization_invariant_bool = pal_env_get_variable_bool("CORECLR_GLOBAL_INVARIANT");

            const char* use_server_gc_value = use_server_gc_bool ? "true" : "false";
            const char* use_globalization_invariant_value = use_globalization_invariant_bool ? "true" : "false";
#endif

            const char *property_keys[] = {
               "TRUSTED_PLATFORM_ASSEMBLIES",
               "APP_PATHS",
               "APP_NI_PATHS",
               "NATIVE_DLL_SEARCH_DIRECTORIES",
#if PLATFORM_WINDOWS
               "APP_LOCAL_WINMETADATA",
#elif PLATFORM_LINUX
               "System.GC.Server",
               "System.Globalization.Invariant"
#endif
            };
            
            const char *property_values[] = {
                // TRUSTED_PLATFORM_ASSEMBLIES
                trusted_platform_assemblies.c_str(),
                // APP_PATHS
                app_paths.c_str(),
                // APP_NI_PATHS
                app_ni_paths.c_str(),
                // NATIVE_DLL_SEARCH_DIRECTORIES
                native_dll_search_directories.c_str(),
#if PLATFORM_WINDOWS
                // APP_LOCAL_WINMETADATA
                platform_resource_roots.c_str(),
#elif PLATFORM_LINUX
                // System.GC.Server
                use_server_gc_value,
                // System.Globalization.Invariant
                use_globalization_invariant_value
#endif
            };

            const auto st = fn_initialize()(
                this_executable_path.c_str(),
                m_coreclr_appdomain_friendly_name.c_str(),
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

        BOOL execute_assembly(const std::string& executable_path, const std::vector<std::string>& arguments, unsigned int* coreclr_exit_code, int* dotnet_exit_code)
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
                    argv[i] = strdup(arguments[i].c_str());
                }

                return argv;
            };

            const auto argc = static_cast<int>(arguments.size());
            const auto argv = to_clr_arguments();
            const auto target_app = std::string(executable_path.c_str());

            auto st = fn_execute_assembly()(
                m_coreclr_host_handle,
                m_coreclr_appdomain_id,
                argc,
                const_cast<const char**>(argv),
                target_app.c_str(),
                coreclr_exit_code);

            if (!(st >= 0))
            {
                LOG(ERROR) << "Coreclr: coreclr_execute_assembly failed. Status: " << st;
                *coreclr_exit_code = -1;
                *dotnet_exit_code = -1;
                return FALSE;
            }

            st = fn_shutdown2()(m_coreclr_host_handle, m_coreclr_appdomain_id, dotnet_exit_code);
            if (!(st >= 0))
            {
                LOG(ERROR) << "Coreclr: coreclr_shutdown2 failed. Status: " << st;
                *dotnet_exit_code = -1;
                return FALSE;
            }

            delete[] argv;

            return TRUE;
        }

    private:

        coreclr_initialize_ptr fn_initialize() const
        {
            if (m_coreclr_initialize == nullptr)
            {
                throw std::runtime_error("m_coreclr_initialize is nullptr");
            }
            return m_coreclr_initialize;
        }

        coreclr_shutdown_2_ptr fn_shutdown2() const
        {
            if (m_coreclr_shutdown_2 == nullptr)
            {
                throw std::runtime_error("m_coreclr_shutdown_2 is nullptr");
            }
            return m_coreclr_shutdown_2;
        }

        coreclr_execute_assembly_ptr fn_execute_assembly() const
        {
            if (m_coreclr_execute_assembly == nullptr)
            {
                throw std::runtime_error("coreclr_execute_assembly_ptr is nullptr");
            }
            return m_coreclr_execute_assembly;
        }

    };

    class coreclr
    {
    public:

        static int run(const std::string& this_executable_path, const std::string& dotnet_executable_path, const std::vector<std::string>& arguments,
            const version::Semver200_version& clr_minimum_version);

    private:

        static std::vector<core_clr_directory> get_core_directories_from_path(const char* core_clr_root_path,
            const version::Semver200_version& clr_minimum_version);

        static core_clr_instance* try_load_core_clr(const char* directory_path, const version::Semver200_version& core_clr_version);

        static std::vector<std::string> get_trusted_platform_assemblies(const char* trusted_platform_assemblies_path);

        static std::string build_trusted_platform_assemblies_str(const std::string& executable_path,
            core_clr_instance* const core_clr_instance);

    };
};