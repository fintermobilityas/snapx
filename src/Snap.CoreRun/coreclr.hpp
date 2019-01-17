#pragma once

#include "corerun.hpp"
#include "vendor/semver/semver200.h"
#include "vendor/coreclr/mscoree.h"	// Generated from mscoree.idl

#include <vector>

namespace snap {

    using std::wstring;

    struct core_clr_directory
    {
        std::wstring path;
        version::Semver200_version version;
    };

    struct core_clr_instance
    {
        void* instance;
        core_clr_directory directory;

        static core_clr_instance build(void* instance, const core_clr_directory& directory)
        {
            core_clr_instance core_clr_instance;
            core_clr_instance.instance = instance;
            core_clr_instance.directory = std::move(directory);
            return core_clr_instance;
        }
    };

    class coreclr
    {
        // The path to this module
        std::wstring m_hostPath;

        // The path to the directory containing this module
        std::wstring m_hostDirectoryPath;

        // The name of this module, without the path
        const wchar_t *m_hostExeName;

        // The list of paths to the assemblies that will be trusted by CoreCLR
        std::wstring m_tpaList;

        ICLRRuntimeHost2* m_CLRRuntimeHost;

        HMODULE m_coreCLRModule;

    public:
        static int run(const std::wstring& executable_path, const std::vector<std::wstring>& arguments,
            const version::Semver200_version& clr_minimum_version);

        static std::vector<core_clr_directory> get_core_directories_from_path(const wchar_t* core_clr_root_path,
            const version::Semver200_version& clr_minimum_version);
    private:

        static void* try_load_core_clr(const std::wstring & executable_path, const std::vector<std::wstring>& arguments,
            const version::Semver200_version & clr_minimum_version);

        static void* try_load_core_clr(const wchar_t* directory_path);

        static STARTUP_FLAGS create_clr_startup_flags();
    };
};
