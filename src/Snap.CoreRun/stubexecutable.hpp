#pragma once

#include "corerun.hpp"

#include <vector>

namespace snap
{
    using std::wstring;

    class stubexecutable
    {
    public:
        static int snap::stubexecutable::run_current_snap_windows(std::vector<std::wstring> arguments, int cmdShow);
        static std::wstring find_root_app_dir();
        static std::wstring find_own_executable_name();
        static std::wstring find_latest_app_dir();
    };
}
