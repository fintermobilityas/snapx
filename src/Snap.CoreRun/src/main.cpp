#include "main.hpp"

#ifdef PAL_LOGGING_ENABLED
#include <plog/Appenders/ColorConsoleAppender.h>
#include <plog/Appenders/RollingFileAppender.h>
#include <plog/Appenders/DebugOutputAppender.h>
#endif

inline void maybe_enable_plog()
{
#ifdef PAL_LOGGING_ENABLED
    static plog::RollingFileAppender<plog::TxtFormatter> fileAppender("corerun.log", 8000, 3);
    static plog::ColorConsoleAppender<plog::TxtFormatter> consoleAppender;
    static plog::DebugOutputAppender<plog::TxtFormatter> debugOutputAppender;
    plog::init(plog::Severity::verbose, &fileAppender)
    .addAppender(&consoleAppender)
    .addAppender(&debugOutputAppender);
#endif
}

// MSVS, MINGW ENTRYPOINT
// -->
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
    maybe_enable_plog();
    pal_mitigate_dll_hijacking();

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
        maybe_enable_plog();
        return corerun_main_impl(argc, argv, -1);
    }
    catch (const std::exception& e)
    {
        std::cerr << "Unknown error: " << e.what() << std::endl;
    }
    return 1;
}
#endif
