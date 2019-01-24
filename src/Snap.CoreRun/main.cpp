#include "corerun.hpp"
#include "stubexecutable.hpp"
#include "coreclr.hpp"
#include "vendor/cxxopts/cxxopts.hpp"

#include <vector>

INITIALIZE_EASYLOGGINGPP

int run_main(int argc, char *argv[], const int cmd_show_windows)
{
    START_EASYLOGGINGPP(argc, argv);

    char* app_name = nullptr;
    if (!pal_fs_get_own_executable_name(&app_name))
    {
        LOG(ERROR) << "Unable to get own executable name." << std::endl;
        return -1;
    }

#if _DEBUG && PLATFORM_WINDOWS
    std::vector<std::string> args;
    args.emplace_back(strdup(argv[0]));
    args.emplace_back("--coreclr-min-version=2.2.0");
    args.emplace_back("--coreclr-exe=C:\\Users\\peters\\Documents\\GitHub\\snap\\src\\Snap.Update\\bin\\Debug\\netcoreapp2.1\\Snap.Update.dll");

    argc = args.size();
    argv = new char*[argc];

    for(auto i = 0; i < argc; i++)
    {
        argv[i] = strdup(args[i].c_str());
    }
#endif

    char* this_executable_abs_path = nullptr;
    if(!pal_fs_get_absolute_path(argv[0], &this_executable_abs_path))
    {
        return -1;
    }

    const std::string app_name_s(app_name);

    el::Configurations easylogging_default_conf;
    easylogging_default_conf.setToDefault();
    easylogging_default_conf.setGlobally(el::ConfigurationType::Filename, app_name_s + ".log");
    el::Loggers::reconfigureLogger("default", easylogging_default_conf);

    const auto run_stubexecutable = [argv, argc, cmd_show_windows]() -> int
    {
        std::vector<std::string> stubexecutable_arguments(argv, argv + argc);
        stubexecutable_arguments.erase(stubexecutable_arguments.begin()); // Remove "this" executable name.
        return snap::stubexecutable::run(stubexecutable_arguments, cmd_show_windows);
    };

    cxxopts::Options options(app_name_s);

    options
        .allow_unrecognised_options()
        .add_options("Generic")
        ("h,help", "Display this help message.", cxxopts::value<bool>()->implicit_value("false"));

    options
        .add_options("CoreClr")
        ("coreclr-min-version", "coreclr minimum version that will be loaded.", cxxopts::value<std::string>())
        ("coreclr-exe", "coreclr executable (dll or exe) to run.", cxxopts::value<std::string>())
        // NB! Positional arguments cannot contain dashes (--)
        ("coreclr-args", "coreclr executable arguments.", cxxopts::value<std::vector<std::string>>());

    try
    {
        auto options_result = options.parse(argc, argv);

        const auto is_help = options_result.count("help") > 0;
        const auto is_core_clr = options_result.count("coreclr-min-version") > 0
            || options_result.count("coreclr-exe") > 0
            || options_result.count("coreclr-args") > 0;
        const auto is_stub_executable = !is_help && !is_core_clr;

        if (is_stub_executable)
        {
            return run_stubexecutable();
        }

        if (is_core_clr)
        {
            version::Semver200_version clr_min_required_version;
            std::string coreclr_exe;
            std::vector<std::string> coreclr_arguments;

            if (options_result.count("coreclr-min-version"))
            {
                const auto clr_min_required_version_ = options_result["coreclr-min-version"].as<std::string>();
                clr_min_required_version = version::Semver200_version(clr_min_required_version_);
            }

            if (options_result.count("coreclr-exe"))
            {
                coreclr_exe = options_result["coreclr-exe"].as<std::string>();
            }

            if (options_result.count("coreclr-args"))
            {
                options.parse_positional({ "coreclr-args" });

                for (const auto& argument : options_result["coreclr-args"].as<std::vector<std::string>>())
                {
                    coreclr_arguments.emplace_back(argument);
                }
            }

            // Snap.CoreRun.exe --coreclr-min-version 2.2.0 --coreclr-exe test.dll --coreclr-args test1234 test12345
            return snap::coreclr::run(this_executable_abs_path, coreclr_exe, coreclr_arguments, clr_min_required_version);
        }

        if (is_help)
        {
            LOG(INFO) << options.help({ "", "Generic", "CoreClr" }) << std::endl;
            return 0;
        }

    }
    catch (const version::Parse_error & e)
    {
        LOG(ERROR) << "Invalid semver version: " << e.what() << std::endl;
        return -1;
    }
    catch (const cxxopts::OptionException& e)
    {
        LOG(ERROR) << "Unable to parse command line argument options. " << e.what() << std::endl;
        LOG(INFO) << options.help({ "", "Generic", "CoreClr" }) << std::endl;
        return -1;
    }

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
    for(auto i = 0; i < argc; i++)
    {
        argv[i] = pal_utf8_string(argw[i]).dup();
    }

    LocalFree(argw);

    try
    {
        return run_main(argc, argv, n_cmd_show);
    }
    catch (std::exception& ex)
    {
        LOG(ERROR) << "Unknown error: " << ex.what() << std::endl;
    }

    return -1;
}
#else
int main(const int argc, char *argv[])
{
    try
    {
        return run_main(argc, argv, -1);
    }
    catch (std::exception& ex)
    {
        LOG(ERROR) << "Unknown error: " << ex.what() << std::endl;
    }
    return -1;
}
#endif
