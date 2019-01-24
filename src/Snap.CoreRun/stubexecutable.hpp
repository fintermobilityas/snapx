#pragma once

#include "corerun.hpp"

#include <vector>
#include <string>

namespace snap
{
    class stubexecutable
    {
    public:
        static int run(std::vector<std::string> arguments, const int cmd_show);
    private:

        static std::string find_root_app_dir();
        static std::string find_own_executable_name();
        static std::string find_latest_app_dir();
    };
}
