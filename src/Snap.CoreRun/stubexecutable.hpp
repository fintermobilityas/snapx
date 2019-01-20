#pragma once

#include "corerun.hpp"

#include <vector>
#include <string>

namespace snap
{
    class stubexecutable
    {
    public:
#if PLATFORM_WINDOWS
        static int run(std::vector<std::wstring> arguments, const int cmd_show);
#else
        static int run(std::vector<std::wstring> arguments);
#endif
    private:

        static std::wstring find_root_app_dir();
        static std::wstring find_own_executable_name();
        static std::wstring find_latest_app_dir();
    };
}
