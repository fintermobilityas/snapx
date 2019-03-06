#pragma once

#include "corerun.hpp"

#include <vector>
#include <string>

namespace snap
{
    class stubexecutable
    {
    public:
        static int run(std::vector<std::string> arguments, int cmd_show);
    private:
        static std::string find_current_app_dir();
    };
}
