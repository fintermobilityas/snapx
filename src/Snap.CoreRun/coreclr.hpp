#pragma once

#include "corerun.hpp"
#include "vendor/semver/semver200.h"
#include "vendor/coreclr/mscoree.h"	// Generated from mscoree.idl

#include <vector>
#include <iostream>

namespace snap {

    using std::wstring;

    typedef void core_clr_instance_t;

#if PLATFORM_WINDOWS
    // Class used to manage activation context.
    // See: https://docs.microsoft.com/en-us/windows/desktop/SbsCs/using-the-activation-context-api
    class core_clr_activation_ctx
    {
    public:
        // assemblyPath - Assembly containing activation context manifest
        core_clr_activation_ctx(_In_z_ const WCHAR *assemblyPath)
            : _actCookie{}
            , _actCxt{ INVALID_HANDLE_VALUE }
        {
            ACTCTX cxt{};
            cxt.cbSize = sizeof(cxt);
            cxt.dwFlags = (ACTCTX_FLAG_APPLICATION_NAME_VALID | ACTCTX_FLAG_RESOURCE_NAME_VALID);
            cxt.lpSource = assemblyPath;
            cxt.lpResourceName = MAKEINTRESOURCEW(1); // The CreateProcess manifest which contains the context details

            _actCxt = ::CreateActCtxW(&cxt);
            if (_actCxt == INVALID_HANDLE_VALUE)
            {
                DWORD err = ::GetLastError();
                if (err == ERROR_RESOURCE_TYPE_NOT_FOUND)
                {
                    std::wcerr << L"Assembly does not contain a manifest for activation" << std::endl;
                }
                else
                {
                    std::wcerr << L"Activation Context creation failed. Error Code: " << err << std::endl;
                }
            }
            else
            {
                BOOL res = ::ActivateActCtx(_actCxt, &_actCookie);
                if (res == FALSE)
                    std::wcerr << L"Failed to activate Activation Context. Error Code: " << ::GetLastError() << std::endl;
            }
        }

        ~core_clr_activation_ctx()
        {
            if (_actCookie != ULONG_PTR{})
            {
                BOOL res = ::DeactivateActCtx(0, _actCookie);
                if (res == FALSE)
                    std::wcerr << L"Failed to de-activate Activation Context. Error Code: " << ::GetLastError() << std::endl;
            }

            if (_actCxt != INVALID_HANDLE_VALUE)
                ::ReleaseActCtx(_actCxt);
        }

    private:
        HANDLE _actCxt;
        ULONG_PTR _actCookie;
    };
#endif

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

        core_clr_instance_t* m_instance_ptr;
        core_clr_directory m_directory;
        ICLRRuntimeHost2* m_host;

    public:
        core_clr_instance() : m_instance_ptr(nullptr), m_host(nullptr)
        {

        }

        core_clr_instance(core_clr_instance_t* core_clr_instance_ptr, const core_clr_directory& directory) :
            m_instance_ptr(core_clr_instance_ptr), m_directory(std::move(directory))
        {

        }

        core_clr_instance(core_clr_instance_t* core_clr_instance_ptr, const wchar_t* core_clr_root_path, const wchar_t* core_clr_dll_path, const version::Semver200_version core_clr_version) :
            m_instance_ptr(core_clr_instance_ptr), m_directory(core_clr_directory(core_clr_root_path, core_clr_dll_path, core_clr_version))
        {

        }

#if PLATFORM_WINDOWS
        HMODULE to_native_ptr() const
        {
            return static_cast<HMODULE>(m_instance_ptr);
        }
#endif

        bool is_instance_loaded() const
        {
            return m_instance_ptr != nullptr;
        }

        bool is_host_created() const
        {
            return m_host != nullptr;
        }

        core_clr_directory get_directory() const
        {
            return m_directory;
        }

        ICLRRuntimeHost2* get_host() const
        {
            return m_host;
        }

        void set_host(ICLRRuntimeHost2* host)
        {
            m_host = host;
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

        static BOOL get_clr_runtime_host(core_clr_instance* core_clr_instance);

        static core_clr_instance* try_load_core_clr(const std::wstring & executable_path, const std::vector<std::wstring>& arguments,
            const version::Semver200_version & clr_minimum_version);

        static core_clr_instance* try_load_core_clr(const wchar_t* directory_path, const version::Semver200_version& core_clr_version);

        static STARTUP_FLAGS create_clr_startup_flags();

        static BOOL create_clr_appdomain(const std::wstring& executable_path, const wchar_t* executable_working_directory,
            const std::wstring& trusted_platform_assemblies,
            core_clr_instance* const core_clr_instance,
            unsigned long* app_domain_id_out
        );

        static std::vector<std::wstring> get_trusted_platform_assemblies(const wchar_t* trusted_platform_assemblies_path);

        static std::wstring build_trusted_platform_assemblies_str(const std::wstring& executable_path,
            core_clr_instance* const core_clr_instance);

        static wchar_t** to_clr_arguments(const std::vector<std::wstring>& arguments, int* argc);

    };
};
