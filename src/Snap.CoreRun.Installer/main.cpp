#include "main.hpp"
#include "pal.hpp"
#include "extractor.hpp"
#include "easylogging++.h"
#include "guid.hpp"

#include <vector>

uint8_t _installer_nupkg_start;
uint8_t _installer_nupkg_size;
uint8_t _installer_nupkg_end;

INITIALIZE_EASYLOGGINGPP

bool init_easylogging()
{
    char* app_name = nullptr;
    if (!pal_fs_get_own_executable_name(&app_name))
    {
        LOG(ERROR) << "Unable to get own executable name." << std::endl;
        return false;
    }

    const std::string app_name_s(app_name);

    el::Configurations easylogging_default_conf;
    easylogging_default_conf.setToDefault();
    easylogging_default_conf.setGlobally(el::ConfigurationType::Filename, app_name_s + ".log");
    el::Loggers::reconfigureLogger("default", easylogging_default_conf);

    return true;
}

std::string build_install_dir()
{
    char* working_dir = nullptr;
    if (!pal_fs_get_cwd(&working_dir))
    {
        return std::string();
    }

    auto random_guid = snap::Guid::newGuid();

    char* install_dir = nullptr;
    if (!pal_fs_path_combine(working_dir, std::string(random_guid).c_str(), &install_dir))
    {
        return std::string();
    }

    return std::string(install_dir);
}

int main_impl(int argc, char **argv, const int cmd_show_windows)
{
    START_EASYLOGGINGPP(argc, argv);

    if(!init_easylogging())
    {
        return -1;
    }

    size_t nupkg_size = 0;
    uint8_t* nupkg_start = nullptr;
    uint8_t* nupkg_end = nullptr;

    std::string nupkg_filename;
    std::string install_dir(build_install_dir());

#if !defined(NDEBUG) && defined(PLATFORM_WINDOWS)
    nupkg_filename = R"(C:\Users\peters\Documents\GitHub\snap\src\Snap.DemoApp\snapx\packages\demoapp_full_1.0.0_win7-x64_test.nupkg)";
    install_dir = R"(C:\Users\peters\Documents\GitHub\snap\src\Snap.DemoApp\snapx\packages\test)";
#elif !defined(NDEBUG) && defined(PLATFORM_LINUX)
    nupkg_filename = R"(/home/peters/Documents/GitHub/snap/src/Snap.DemoApp/snapx/packages/demoapp_full_1.0.0_linux-x64_test.nupkg)";
#else
    if (argc == 2) {
        nupkg_filename = argv[1];
    }
#endif

    char* nupkg_data = nullptr;
    if (!nupkg_filename.empty())
    {
        int nupkg_size_tmp = 0;
        if (!pal_str_endswith(nupkg_filename.c_str(), ".nupkg")
            || !pal_fs_read_file(nupkg_filename.c_str(), "rb", &nupkg_data, &nupkg_size_tmp))
        {
            LOG(ERROR) << "Failed to read nupkg from: " << nupkg_filename;
            return -1;
        }

        nupkg_size = nupkg_size_tmp;
        nupkg_start = reinterpret_cast<uint8_t*>(&nupkg_data[0]);
        nupkg_end = reinterpret_cast<uint8_t*>(&nupkg_data[0]) + nupkg_size;
    }
    else
    {
        nupkg_size = reinterpret_cast<size_t>(reinterpret_cast<void*>(&_installer_nupkg_size));
        nupkg_start = &_installer_nupkg_start;
        nupkg_end = &_installer_nupkg_end;
    }

    if (!snap::extractor::is_valid_payload(nupkg_size, nupkg_start, nupkg_end))
    {
        LOG(ERROR) << "Invalid nupkg payload! Nupkg size: " << nupkg_size;
        return -1;
    }

    LOG(INFO) << "Nupkg payload successfully validated.";

    if (snap::extractor::extract(install_dir, nupkg_size, nupkg_start, nupkg_end))
    {
        return -1;
    }

    return 0;
}

#if PLATFORM_WINDOWS

#include <shellapi.h>

// ReSharper disable all
int APIENTRY wWinMain(
    _In_ HINSTANCE h_instance,
    _In_opt_ HINSTANCE h_prev_instance,
    _In_ LPWSTR    lp_cmd_line,
    _In_ const int  n_cmd_show)
    // ReSharper enable all
{
    auto argc = 0;
    const auto argw = CommandLineToArgvW(GetCommandLineW(), &argc);

    auto argv = new char*[argc];
    for (auto i = 0; i < argc; i++)
    {
        argv[i] = pal_utf8_string(argw[i]).dup();
    }

    LocalFree(argw);

    try
    {
        return main_impl(argc, argv, n_cmd_show);
    }
    catch (std::exception& ex)
    {
        LOG(ERROR) << "Unknown error: " << ex.what() << std::endl;
    }

    return -1;
}
#else
int main(const int argc, char *argv[])
{
    try
    {
        return main_impl(argc, argv, -1);
    }
    catch (std::exception& ex)
    {
        LOG(ERROR) << "Unknown error: " << ex.what() << std::endl;
    }
    return -1;
}
#endif
