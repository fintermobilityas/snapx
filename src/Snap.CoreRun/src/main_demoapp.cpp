const int unit_default_exit_code = 127;
const int unit_default_error_exit_code = 128;

#include <iostream>
#include <vector>

#include "pal/pal.hpp"
#include "nlohmann/json.hpp"

using json = nlohmann::json;

int corerun_demoapp_main_impl(int argc, char **argv)
{
    char* this_exe_name = nullptr;
    if (!pal_fs_get_own_executable_name(&this_exe_name))
    {
        return unit_default_error_exit_code;
    }

    char* this_working_dir = nullptr;
    if(!pal_fs_get_cwd(&this_working_dir))
    {
        return unit_default_error_exit_code;
    }

    std::vector<std::string> arguments(argv, argv + argc);
    const auto log_filename_str = std::string(this_exe_name) + ".json";
    const auto command_expected_exit_code_str = std::string("--expected-version=");

    json output;
    output["arguments"] = arguments;
    output["working_dir"] = this_working_dir;
    output["exit_code"] = unit_default_exit_code;
    output["command"] = std::string();
    
    for (const auto &value : arguments)
    {
        const auto command_expected_exit_code_value = pal_str_startswith(value.c_str(), command_expected_exit_code_str.c_str());
        if (command_expected_exit_code_value)
        {
            const auto version_start_pos = value.find_last_of(command_expected_exit_code_str);
            const auto version_str = value.substr(version_start_pos + 1);

            output["command"] = command_expected_exit_code_str;
            output["version"] = version_str;
            break;
        }
    }

    auto output_str = output.dump();

    pal_fs_write(log_filename_str.c_str(), "w", output_str.c_str(), output_str.size());

    return output["exit_code"].get<int>();
}

#if WIN32
#include <windows.h>
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
        return corerun_demoapp_main_impl(argc, argv);
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
        return corerun_demoapp_main_impl(argc, argv);
    }
    catch (const std::exception& e)
    {
        std::cerr << "Unknown error: " << e.what() << std::endl;
    }
    return 1;
}
#endif
