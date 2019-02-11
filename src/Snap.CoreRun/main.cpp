#include "corerun.hpp"
#include "stubexecutable.hpp"

int main_impl(int argc, char **argv, const int cmd_show_windows)
{
    if(pal_is_elevated()) {
        return -1;
    }

    char* app_name = nullptr;
    if (!pal_fs_get_own_executable_name(&app_name))
    {
        return -1;
    }

    const auto run_stubexecutable = [argv, argc, cmd_show_windows]() -> int
    {
        std::vector<std::string> stubexecutable_arguments(argv, argv + argc);
        stubexecutable_arguments.erase(stubexecutable_arguments.begin()); // Remove "this" executable name.
        return snap::stubexecutable::run(stubexecutable_arguments, cmd_show_windows);
    };

    return run_stubexecutable();
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
    catch (std::exception)
    {
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
    catch (std::exception)
    {
    }
    return -1;
}
#endif
