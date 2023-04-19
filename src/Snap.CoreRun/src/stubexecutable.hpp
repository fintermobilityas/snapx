#pragma once

#include "corerun.hpp"

#include <map>
#include <string>
#include <vector>

namespace snap
{
    class stubexecutable
    {
    public:
        static int run(std::vector<std::string> arguments, const std::map<std::string, std::string>& environment_variables, int cmd_show);
    private:
        static std::string find_current_app_dir();
    };
}
