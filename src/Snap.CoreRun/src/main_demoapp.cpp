#include <iostream>
#include <vector>
#include <sstream>

#include "corerun.hpp"
#include "pal/pal.hpp"
#include "nlohmann/json.hpp"

using json = nlohmann::json;

const pal_exit_code_t unit_test_success_exit_code = 0;
const pal_exit_code_t unit_test_error_exit_code = 1;

int corerun_demoapp_main_impl(const int argc, char **argv)
{
    this_exe::plog_init();

    LOGD << "Process started. Arguments: " << this_exe::build_argv_str(argc, argv);

    pal_mitigate_dll_hijacking();

    char* app_name = nullptr;
    if (!pal_process_get_name(&app_name))
    {
        LOGE << "Failed to get process name.";
        return unit_test_error_exit_code;
    }

    char* this_working_dir = nullptr;
    if(!pal_process_get_cwd(&this_working_dir))
    {
        LOGE << "Failed to get current working dir.";
        return unit_test_error_exit_code;
    }

    std::vector<std::string> arguments(argv, argv + argc);
    const auto log_filename_str = std::string(app_name) + ".json";
    const auto command_expected_exit_code_str = std::string("--expected-version=");

    json output;
    output["arguments"] = arguments;
    output["working_dir"] = this_working_dir;
    output["exit_code"] = unit_test_success_exit_code;
    output["command"] = std::string();
    
    for (const auto &value : arguments)
    {
        const auto command_expected_exit_code_value = pal_str_startswith(value.c_str(), command_expected_exit_code_str.c_str());
        if (command_expected_exit_code_value)
        {
            const auto version_start_pos = value.find_last_of(command_expected_exit_code_str);
            const auto version_str = value.substr(version_start_pos + 1);

            output["command"] = value;
            output["version"] = version_str;
            break;
        }
    }

    LOGV << "Writing json: " << log_filename_str;

    std::stringstream ss;
    ss << output.dump() << std::endl;

    const auto json_str = ss.str();
    const auto data_len = json_str.size() + 1;
    auto* data = new char[data_len];
#if defined(PAL_PLATFORM_WINDOWS)
    strcpy_s(data, data_len, json_str.c_str());
#else
    strcpy(data, json_str.c_str());
#endif
    data[data_len] = '\0';

    pal_fs_write(log_filename_str.c_str(), "wb", data, data_len);

    const auto exit_code = output["exit_code"].get<pal_exit_code_t>();

    LOGV << "Demoapp process exited. Exit code: " << exit_code;
     
    return exit_code;
}

#if defined(WIN32)
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
        LOGE << "Unknown error: " << e.what();
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
        LOGE << "Unknown error: " << e.what();
    }
    return 1;
}
#endif
