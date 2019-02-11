#include "main.hpp"
#include "pal.hpp"
#include "installer.hpp"
#include "easylogging++.h"

#include <vector>
#include <climits>

uint8_t _installer_nupkg_start;
uint8_t _installer_nupkg_size;
uint8_t _installer_nupkg_end;

INITIALIZE_EASYLOGGINGPP

int main_impl(int argc, char **argv, const int cmd_show_windows)
{
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
        const auto nupkg_size = reinterpret_cast<size_t>(reinterpret_cast<void*>(&_installer_nupkg_size));
        const auto nupkg_start = &_installer_nupkg_start;
        const auto nupkg_end = &_installer_nupkg_end;
        if(snap::installer::is_valid_payload(nupkg_size, nupkg_start, nupkg_end))
        {
            LOG(INFO) << "Valid nupkg payload detected, proceeding with installation...";
            std::vector<std::string> arguments(argv, argv + argc);
            return snap::installer::run(arguments, nupkg_size, nupkg_start, nupkg_end);
        }
        return main_impl(argc, argv, -1);
    }
    catch (std::exception& ex)
    {
        LOG(ERROR) << "Unknown error: " << ex.what() << std::endl;
    }
    return -1;
}
#endif
