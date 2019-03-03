#include "main.hpp"

#if defined(PAL_PLATFORM_WINDOWS)
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
        return corerun_main_impl(argc, argv, n_cmd_show);
    }
    catch (const std::exception& e)
    {
        std::cerr << "Unknown error: " << e.what() << std::endl;
    }

    return 1;
}
#else
int main(const int argc, char *argv[])
{
    try
    {
        return corerun_main_impl(argc, argv, -1);
    }
    catch (const std::exception& e)
    {
        std::cerr << "Unknown error: " << e.what() << std::endl;
    }
    return 1;
}
#endif
