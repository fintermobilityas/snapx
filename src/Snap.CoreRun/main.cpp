#include "stubexecutable.hpp"
#include "coreclr.hpp"
#include "vendor/cxxopts/cxxopts.hpp"

#include <vector>

static int n_cmd_show_ = 0;
LPWSTR *wargv = nullptr;

int run_main(int argc, char **argv)
{
    version::Semver200_version clr_minimum_version("2.2.0");
    std::wstring executable_path = L"C:\\Users\\peters\\Documents\\GitHub\\snap\\src\\Snap.Update\\bin\\Debug\\netcoreapp2.1\\Snap.Update.dll";
    std::vector<std::wstring> executable_arguments;

    return snap::coreclr::run(executable_path, executable_arguments, clr_minimum_version);
    //const auto app_name = pal_convert_from_utf16_to_utf8(snap::stubexecutable::find_own_executable_name().c_str());

    std::string app_name;
    std::vector<std::wstring> argv_w;
    //for (auto i = 0; i < argc; i++)
    //{
    //    argv_w.emplace_back(convert_from_utf8_to_utf16(argv[i]));
    //}

    cxxopts::Options options(app_name, "Manages snap applications.");

    options
        .add_options("Core")
        ("h,help", "Display this help message.", cxxopts::value<bool>()->implicit_value("false"));

    options
        .add_options("CoreClr")
        ("coreclr", "Run a coreclr executable.", cxxopts::value<bool>()->implicit_value("true"))
        ("file", "The coreclr executable (dll or exe) to run.", cxxopts::value<std::string>())
        ("argv", "Additional commandline arguments.", cxxopts::value<std::vector<std::string>>());

    try
    {
        auto options_result = options.parse(argc, argv);
        if (options_result.count("h"))
        {
            std::cout << options.help({ "", "Core", "CoreClr" }) << std::endl;
            return 0;
        }

        const auto is_core = options_result.count("Core");
        const auto is_core_clr = options_result.count("CoreClr");
        const auto is_stub_executable = !is_core && !is_core_clr;

        if (is_stub_executable)
        {
            return snap::stubexecutable::run_current_snap_windows(argv_w, n_cmd_show_);
        }
    }
    catch (const cxxopts::OptionException& e)
    {
        std::cerr << "Error parsing arguments: " << e.what() << std::endl;
    }

    return -1;
}

int main(const int argc, char **argv)
{
    try
    {
        return run_main(argc, argv);
    }
    catch (std::exception& ex)
    {
        std::cerr << "Unknown error: " << ex.what() << std::endl;
    }
    return -1;
}

#if PLATFORM_WINDOWS

#include <shellapi.h>

int APIENTRY WinMain(
    HINSTANCE h_instance,
    HINSTANCE h_prev_instance,
    LPSTR lp_cmd_line,
    const int n_cmd_show
)
{
    n_cmd_show_ = n_cmd_show;

    auto argc = 0;
    wargv = CommandLineToArgvW(GetCommandLineW(), &argc);
    if (nullptr == wargv)
    {
        return snap::stubexecutable::run_current_snap_windows(std::vector<std::wstring>(), n_cmd_show);
    }

    const auto argv = new char *[argc];

    for (auto i = 0; i < argc; ++i) {
        std::wstring widechar_string_in(wargv[i]);
        const auto widechar_string_in_len = static_cast<int>(widechar_string_in.size());

        char* multibyte_string_out = nullptr;
        pal_str_convert_from_utf16_to_utf8(widechar_string_in.c_str(), widechar_string_in_len, &multibyte_string_out);

        argv[i] = multibyte_string_out;
    }

    LocalFree(wargv);

    const auto return_value = main(argc, argv);
    delete[] argv;

    return return_value;
}
#endif
