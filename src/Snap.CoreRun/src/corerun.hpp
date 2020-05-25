#pragma once

#include "pal/pal.hpp"

#include <plog/Appenders/ColorConsoleAppender.h>
#include <plog/Appenders/RollingFileAppender.h>

#if defined(PAL_PLATFORM_WINDOWS)
#include <plog/Appenders/DebugOutputAppender.h>
#endif

#include <string>
#include <vector>

class this_exe
{
public:
    static inline pal_mode_t default_permissions = 0777;

    static void plog_init()
    {
        const auto filename = get_logger_relative_filename();

        static plog::RollingFileAppender<plog::TxtFormatter> file_appender(filename.c_str(), 1000000, 1);
        static plog::ColorConsoleAppender<plog::TxtFormatter> console_appender;
#if defined(PAL_PLATFORM_WINDOWS)
        static plog::DebugOutputAppender<plog::TxtFormatter> debug_output_appender;
#endif

        plog::init(plog::Severity::verbose, &file_appender)
            .addAppender(&console_appender)
#if defined(PAL_PLATFORM_WINDOWS)
            .addAppender(&debug_output_appender)
#endif
            ;
    }

    static std::string get_logger_relative_filename()
    {
        const auto process_name = get_process_name();
        if (process_name.empty())
        {
            return std::string("corerun.log");
        }
        return std::string(process_name + ".log");
    }

    static std::string get_process_name()
    {
        char* app_name = nullptr;
        if (!pal_process_get_name(&app_name))
        {
            return std::string();
        }

        auto app_name_str = std::string(app_name);
        delete app_name;

        return app_name_str;
    }

    static std::string build_argv_str(const std::vector<std::string>& strings, const std::string& delimiter = " ")
    {
        auto ss = std::string();
        for (auto const& string : strings)
        {
            ss += string + delimiter;
        }
        return ss;
    }

    static std::string build_argv_str(const uint32_t argc, char** argv)
    {
        if (argv == nullptr)
        {
            return std::string();
        }

        const std::vector<std::string> arguments(argv, argv + argc);
        return build_argv_str(arguments);
    }

};
