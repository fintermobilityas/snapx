#pragma once

#include "corerun.hpp"
#include "vendor/semver/semver200.h"
#include "vendor/coreclr/mscoree.h"	// Generated from mscoree.idl

#include <vector>

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

        core_clr_instance_t* m_instance_ptr;
        core_clr_directory m_directory;

    public:
        core_clr_instance() : m_instance_ptr(nullptr)
        {

        }

#if PLATFORM_WINDOWS
        HMODULE to_native_ptr() const
        {
            return static_cast<HMODULE>(m_instance_ptr);
        }
#endif

        bool is_valid() const
        {
            return m_instance_ptr != nullptr;
        }

        core_clr_directory get_directory() const
        {
            return m_directory;
        }

        core_clr_instance(core_clr_instance_t* core_clr_instance_ptr, const core_clr_directory& directory) :
            m_instance_ptr(core_clr_instance_ptr), m_directory(std::move(directory))
        {

        }

        core_clr_instance(core_clr_instance_t* core_clr_instance_ptr, const wchar_t* core_clr_root_path, const wchar_t* core_clr_dll_path, const version::Semver200_version core_clr_version) :
            m_instance_ptr(core_clr_instance_ptr), m_directory(core_clr_directory(core_clr_root_path, core_clr_dll_path, core_clr_version))
        {

        }

    };

    class coreclr
    {
    public:
        static int run(const std::wstring& executable_path, const std::vector<std::wstring>& arguments,
            const version::Semver200_version& clr_minimum_version);

        static std::vector<core_clr_directory> get_core_directories_from_path(const wchar_t* core_clr_root_path,
            const version::Semver200_version& clr_minimum_version);
    private:

        static ICLRRuntimeHost2* get_clr_runtime_host(core_clr_instance* core_clr_instance);

        static core_clr_instance* try_load_core_clr(const std::wstring & executable_path, const std::vector<std::wstring>& arguments,
            const version::Semver200_version & clr_minimum_version);

        static core_clr_instance* try_load_core_clr(const wchar_t* directory_path);

        static STARTUP_FLAGS create_clr_startup_flags();

        static std::vector<std::wstring> get_trusted_platform_assemblies(const wchar_t* trusted_platform_assemblies_path);
    };
};
