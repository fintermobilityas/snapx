#include "corerun.hpp"
#include "stubexecutable.hpp"
#include "coreclr.hpp"
#include "vendor/cxxopts/cxxopts.hpp"

#include <vector>

int run_main(int argc, wchar_t *argw[], const int cmd_show_windows)
{
    wchar_t* app_name = nullptr;
    if (!pal_fs_get_own_executable_name(&app_name))
    {
        return -1;
    }

    const auto run_stubexecutable = [argw, argc, cmd_show_windows]()
    {
        std::vector<std::wstring> stubexecutable_arguments(argw, argw + argc);
        stubexecutable_arguments.erase(stubexecutable_arguments.begin()); // Remove "this" executable name.
        return snap::stubexecutable::run(stubexecutable_arguments, cmd_show_windows);
    };

    cxxopts::Options options(pal_str_narrow(app_name));

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
        auto argv = new char*[argc];
        for (auto i = 0; i < argc; ++i) {
            std::wstring widechar_string_in(argw[i]);
            argv[i] = pal_str_narrow(widechar_string_in.c_str());
        }

        auto options_result = options.parse(argc, argv);
        delete[] argv;

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
            std::wstring coreclr_exe_w;
            std::vector<std::wstring> coreclr_arguments_w;

            if (options_result.count("coreclr-min-version"))
            {
                const auto clr_min_required_version_ = options_result["coreclr-min-version"].as<std::string>();
                clr_min_required_version = version::Semver200_version(clr_min_required_version_);
            }

            if (options_result.count("coreclr-exe"))
            {
                auto coreclr_exe = options_result["coreclr-exe"].as<std::string>();
                coreclr_exe_w = std::wstring(coreclr_exe.begin(), coreclr_exe.end());
            }

            if (options_result.count("coreclr-args"))
            {
                options.parse_positional({ "coreclr-args" });

                auto coreclr_arguments = options_result["coreclr-args"].as<std::vector<std::string>>();
                for (const auto& argument : coreclr_arguments)
                {
                    coreclr_arguments_w.emplace_back(std::wstring(argument.begin(), argument.end()));
                }
            }

            // Snap.CoreRun.exe --coreclr-min-version 2.2.0 --coreclr-exe test.dll --coreclr-args test1234 test12345
            return snap::coreclr::run(coreclr_exe_w, coreclr_arguments_w, clr_min_required_version);
        }

        if (is_help)
        {
            std::cout << options.help({ "", "Generic", "CoreClr" }) << std::endl;
            return 0;
        }

    }
    catch (const version::Parse_error & e)
    {
        std::wcerr << L"Invalid semver version: " << e.what() << std::endl;
        return -1;
    }
    catch (const cxxopts::OptionException& e)
    {
        std::wcerr << L"Error: Unable to parse command line argument options. " << e.what() << std::endl;
        std::cout << options.help({ "", "Generic", "CoreClr" }) << std::endl;
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

    try
    {
        return run_main(argc, argw, n_cmd_show);
    }
    catch (std::exception& ex)
    {
        std::wcerr << L"Unknown error: " << ex.what() << std::endl;
    }
    return -1;
}
#else
int PALAPI wmain(const int argc, wchar_t *argv[])
{
    try
    {
        return run_main(argc, argv, -1);
    }
    catch (std::exception& ex)
    {
        std::wcerr << L"Unknown error: " << ex.what() << std::endl;
    }
    return -1;
}
#endif
