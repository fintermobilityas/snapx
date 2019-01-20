#include "corerun.hpp"
#include "stubexecutable.hpp"
#include "coreclr.hpp"
#include "vendor/cxxopts/cxxopts.hpp"

#include <vector>

int run_main(int argc, wchar_t **argw, const int n_cmd_show)
{
    const version::Semver200_version clr_minimum_version("2.2.0");
    const std::wstring executable_path = L"C:\\Users\\peters\\Documents\\GitHub\\snap\\src\\Snap.Update\\bin\\Debug\\netcoreapp2.1\\Snap.Update.dll";
    const std::vector<std::wstring> executable_arguments;

    //return snap::stubexecutable::run(executable_arguments, 0);
    return snap::coreclr::run(executable_path, executable_arguments, clr_minimum_version);

    wchar_t* app_name = nullptr;
    if (!pal_fs_get_own_executable_name(&app_name))
    {
        return -1;
    }

    cxxopts::Options options(pal_str_narrow(app_name), "Manages snap applications.");

    options
        .add_options("Generic")
        ("h,help", "Display this help message.", cxxopts::value<bool>()->implicit_value("false"));

    options
        .add_options("CoreClr")
        ("coreclr", "Run a coreclr executable.", cxxopts::value<bool>()->implicit_value("true"))
        ("file", "The coreclr executable (dll or exe) to run.", cxxopts::value<std::string>())
        ("argv", "Additional commandline arguments.", cxxopts::value<std::vector<std::string>>());

    try
    {
        auto argv = new char*[argc];
        for (auto i = 0; i < argc; ++i) {
            std::wstring widechar_string_in(argw[i]);
            argv[i] = pal_str_narrow(widechar_string_in.c_str());
        }

        auto options_result = options.parse(argc, argv);
        delete[] argv;

        if (options_result.count("h"))
        {
            std::cout << options.help({ "", "Generic", "CoreClr" }) << std::endl;
            return 0;
        }

        const auto is_core = options_result.count("Generic");
        const auto is_core_clr = options_result.count("CoreClr");
        const auto is_stub_executable = !is_core && !is_core_clr;

        if (is_stub_executable)
        {
            std::vector<wchar_t*> stubexecutable_arguments(argw, argw + argc);
            stubexecutable_arguments.erase(stubexecutable_arguments.begin()); // Skip own executable name.
            return snap::stubexecutable::run(stubexecutable_arguments, n_cmd_show);
        }
    }
    catch (const cxxopts::OptionException& e)
    {
        std::wcerr << L"Error parsing arguments: " << e.what() << std::endl;
    }

    return -1;
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

    auto main_return_value = 0;

    try
    {
        main_return_value = run_main(argc, argw, n_cmd_show);
    }
    catch (std::exception& ex)
    {
        std::wcerr << L"Unknown error: " << ex.what() << std::endl;
    }

    if (argc > 0)
    {
        LocalFree(argw);
    }

    return main_return_value;
}
#else
int main(const int argc, wchar_t **argv)
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
