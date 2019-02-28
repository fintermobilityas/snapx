#include "gtest/gtest.h"
#include "main.hpp"
#include "crossguid/Guid.hpp"
#include "nlohmann/json.hpp"

#include <string>
#include <algorithm>
#include <exception>
#include <random>

using json = nlohmann::json;

const int demoapp_default_exit_code = 127;
const uint32_t default_permissions = 0777;
static auto rng = std::default_random_engine{};

inline std::string get_process_cwd() {
    char* working_dir = nullptr;
    if (!pal_process_get_cwd(&working_dir))
    {
        return nullptr;
    }

    std::string working_dir_str(working_dir);
    delete working_dir;

    return working_dir_str;
}

namespace {

    class corerun_app_details
    {
    public:
        std::string working_dir;
        std::string version_str;
        version::Semver200_version version;
        std::string exe_name_absolute_path;
        std::string exe_name_relative_path;

        corerun_app_details() = default;

        corerun_app_details(std::string working_dir, std::string exe_name_absolute_path,
            std::string exe_name_relative_path,
            const std::string& version, bool version_invalid) :
            working_dir(std::move(working_dir)),
            exe_name_absolute_path(std::move(exe_name_absolute_path)),
            exe_name_relative_path(std::move(exe_name_relative_path)),
            version_str(version),
            version(version::Semver200_version(version_invalid ? "0.0.0" : version))
        {

        }
    };

    class corerun_run_details
    {
    public:
        std::string install_dir;

        corerun_run_details() = delete;

        explicit corerun_run_details(const std::string& install_dir) : install_dir(install_dir)
        {

        }

        ~corerun_run_details() {
#if PAL_PLATFORM_WINDOWS || PAL_PLATFORM_MINGW
            pal_fs_rmdir(this->install_dir.c_str(), TRUE);
#else
            EXPECT_TRUE(pal_fs_rmdir(this->install_dir.c_str(), TRUE));
#endif
        }
    };

    class stubexecutable_run_details : corerun_run_details
    {
    public:
        std::vector<std::string> stub_arguments;
        int stub_exit_code{};
        corerun_app_details app_details;
        int app_exit_code{};
        std::vector<std::string> app_arguments;

        explicit stubexecutable_run_details(std::string install_dir) : corerun_run_details(std::move(install_dir))
        {

        }
    };

    class snapx
    {
    private:
        std::string m_unique_id;
        std::vector<corerun_app_details> m_apps;

    public:
        std::string app_name;
        std::string working_dir;
        std::string working_dir_demoapp_exe;
        std::string working_dir_corerun_exe;
        std::string install_dir;
        std::string install_dir_corerun_exe;
        std::string os_file_ext;

        snapx() = delete;

        snapx(const std::string& app_name, const std::string& working_dir) :
            snapx(app_name, working_dir, pal_is_windows() ? ".exe" : "")
        {

        }

        void install(const std::string& version, const std::string& app_dir_prefix = "app-", bool version_invalid = false)
        {
            const auto app_dir = path_combine(this->install_dir, app_dir_prefix + version);
            const auto app_dir_demoapp_exe = path_combine(app_dir, this->app_name + this->os_file_ext);

            ASSERT_TRUE(pal_fs_mkdir(app_dir.c_str(), default_permissions)) << "Failed to create app dir: " << app_dir;
            ASSERT_TRUE(file_copy(this->working_dir_demoapp_exe.c_str(), app_dir_demoapp_exe.c_str())) << "Failed copy demoapp" << this->working_dir_demoapp_exe;

            this->m_apps.emplace_back(corerun_app_details(app_dir, app_dir_demoapp_exe,
                this->app_name + this->os_file_ext, version, version_invalid));
        }

        static std::string path_combine(const std::string& path1, const std::string& path2)
        {
            char* path_combined = nullptr;
            if (!pal_fs_path_combine(path1.c_str(), path2.c_str(), &path_combined))
            {
                return std::string();
            }

            std::string path_combined_str(path_combined);
            delete path_combined;

            return path_combined_str;
        }

        static bool file_copy(const char* src_filename, const char* dest_filename)
        {
            if (src_filename == nullptr
                || dest_filename == nullptr)
            {
                return false;
            }

            char* bytes = nullptr;
            size_t bytes_len = 0;
            if (!pal_fs_read_binary_file(src_filename, &bytes, &bytes_len))
            {
                return false;
            }

            if (!pal_fs_write(dest_filename, "wb", bytes, bytes_len))
            {
                return false;
            }

            if (!pal_fs_chmod(dest_filename, default_permissions))
            {
                return false;
            }

            return true;
        }

        std::unique_ptr<stubexecutable_run_details> run_stubexecutable_with_args(const std::vector<std::string>& arguments)
        {
            const auto argc = static_cast<int>(arguments.size());
            const auto argv = new char*[argc] {};

            for (auto i = 0; i < argc; i++)
            {
                argv[i] = _strdup(arguments[i].c_str());
            }

            auto run_details = std::unique_ptr<stubexecutable_run_details>(new stubexecutable_run_details(this->install_dir));

            for (const auto &value : arguments)
            {
                run_details->stub_arguments.emplace_back(value);
            }

            if (!pal_fs_directory_exists(this->install_dir.c_str()))
            {
                throw std::runtime_error("Fatal error! Install directory does not exist: " + this->install_dir);
            }

            int stub_executable_exit_code = 0;
            if (!pal_process_exec(this->install_dir_corerun_exe.c_str(), this->install_dir.c_str(), argc, argv,
                &stub_executable_exit_code))
            {
                throw std::runtime_error("Failed to start stub executable: " + this->install_dir_corerun_exe + ". Install dir: " + this->install_dir);
            }

            run_details->stub_exit_code = stub_executable_exit_code;
            delete[] argv;

            // We are not synchronizing write of logoutput between parent and child process.
            // This means that the child process may still be writing to the file while we are reading.
            // Todo: Fix me? :)
            pal_sleep_ms(300);

            auto log_output = try_read_log_output();
            if (log_output.empty())
            {
                return run_details;
            }

            json json_log_output;

            try {
                json_log_output = json::parse(log_output);
            }
            catch (const json::exception& ex)
            {
                std::cout << "Failed to parse json output log. What: " << ex.what() << std::endl;
                std::cout << log_output << std::endl;
                return run_details;
            }

            run_details->app_arguments = json_log_output["arguments"].get<std::vector<std::string>>();
            run_details->app_exit_code = json_log_output["exit_code"].get<int>();

            const auto working_dir = json_log_output["working_dir"].get<std::string>();
            const auto command = json_log_output["command"].get<std::string>();

            for (const auto &app : this->m_apps)
            {
                if (app.working_dir == working_dir)
                {
                    run_details->app_details = app;
                    break;
                }
            }

            return run_details;
        }

    private:
        snapx(const std::string& app_name, const std::string& working_dir, const std::string& os_file_ext) :
            m_unique_id(xg::newGuid()),
            app_name(app_name),
            working_dir(working_dir),
            working_dir_corerun_exe(path_combine(working_dir, "corerun" + os_file_ext)),
            working_dir_demoapp_exe(path_combine(working_dir, "corerun_demoapp" + os_file_ext)),
            install_dir(path_combine(working_dir, m_unique_id)),
            install_dir_corerun_exe(path_combine(install_dir, app_name + os_file_ext)),
            os_file_ext(os_file_ext)
        {
            init();
        }

    private:

        void init()
        {
            ASSERT_TRUE(pal_fs_file_exists(this->working_dir_corerun_exe.c_str()));
            ASSERT_TRUE(pal_fs_file_exists(this->working_dir_demoapp_exe.c_str()));
            ASSERT_TRUE(pal_fs_mkdir(this->install_dir.c_str(), default_permissions));
            ASSERT_TRUE(file_copy(this->working_dir_corerun_exe.c_str(), this->install_dir_corerun_exe.c_str()));
        }

        corerun_app_details find_current_app_details()
        {
            corerun_app_details most_recent_app;

            for (const auto &app : this->m_apps)
            {
                if (app.version > most_recent_app.version)
                {
                    most_recent_app = app;
                }
            }

            return most_recent_app;
        }

        std::string try_read_log_output()
        {
            const auto most_recent_app = find_current_app_details();
            if (most_recent_app.working_dir.empty()
                || most_recent_app.exe_name_relative_path.empty())
            {
                return std::string();
            }

            const auto log_filename = most_recent_app.exe_name_relative_path + ".json";

            const auto log_filename_absolute_path = path_combine(most_recent_app.working_dir, log_filename);
            if (log_filename_absolute_path.empty())
            {
                return std::string();
            }

            char* log_output = nullptr;
            size_t log_output_len = 0;
            if (!pal_fs_read_binary_file(log_filename_absolute_path.c_str(), &log_output, &log_output_len) || log_output_len <= 0)
            {
                return std::string();
            }

            std::string log_output_str(log_output);
            delete log_output;

            return log_output_str;
        }

    };

    TEST(MAIN, corerun_StartsWhenThereAreZeroAppsInstalled)
    {
        auto working_dir = get_process_cwd();

        snapx snapx("demoapp", working_dir);

        const auto run_details = snapx.run_stubexecutable_with_args(std::vector<std::string>());

        ASSERT_EQ(run_details->stub_exit_code, 1);
        ASSERT_EQ(run_details->stub_arguments.size(), 0u);
        ASSERT_EQ(run_details->app_exit_code, 0);
        ASSERT_EQ(run_details->app_arguments.size(), 0);
        ASSERT_EQ(run_details->app_details.version_str, "");
    }

    TEST(MAIN, corerun_ExcludesAppDirectoriesWithInvalidPrefix)
    {
        auto working_dir = get_process_cwd();

        snapx snapx("demoapp", working_dir);
        snapx.install("1.0.0", "notanapp-");
        snapx.install("2.0.0");
        snapx.install("3.0.0", "notanapp-");
        snapx.install("4.0.0");

        const auto run_details = snapx.run_stubexecutable_with_args(std::vector<std::string> {
            "--expected-version=4.0.0"
        });

        ASSERT_EQ(run_details->stub_exit_code, 0);
        ASSERT_EQ(run_details->stub_arguments.size(), 1u);
        ASSERT_EQ(run_details->app_exit_code, demoapp_default_exit_code);
        ASSERT_EQ(run_details->app_arguments.size(), 2u);
        ASSERT_EQ(run_details->app_details.version_str, "4.0.0");

        const auto expected_arguments = std::vector<std::string>{
            run_details->app_details.exe_name_absolute_path,
            run_details->stub_arguments[0]
        };

        for (auto i = 0; i < expected_arguments.size(); i++)
        {
            ASSERT_EQ(expected_arguments[i], run_details->app_arguments[i]);
        }
    }

    TEST(MAIN, corerun_ExcludesAppDirectoriesWithInvalidSemver)
    {
        auto working_dir = get_process_cwd();

        snapx snapx("demoapp", working_dir);
        snapx.install("1.0.0");
        snapx.install("2..0.0", "app-", true);
        snapx.install("3.0...0", "app", true);
        snapx.install("4.0.0");

        const auto run_details = snapx.run_stubexecutable_with_args(std::vector<std::string> {
            "--expected-version=4.0.0"
        });

        ASSERT_EQ(run_details->stub_exit_code, 0);
        ASSERT_EQ(run_details->stub_arguments.size(), 1u);
        ASSERT_EQ(run_details->app_exit_code, demoapp_default_exit_code);
        ASSERT_EQ(run_details->app_arguments.size(), 2u);
        ASSERT_EQ(run_details->app_details.version_str, "4.0.0");

        const auto expected_arguments = std::vector<std::string>{
            run_details->app_details.exe_name_absolute_path,
            run_details->stub_arguments[0]
        };

        for (auto i = 0; i < expected_arguments.size(); i++)
        {
            ASSERT_EQ(expected_arguments[i], run_details->app_arguments[i]);
        }
    }

    TEST(MAIN, corerun_StartsInitialVersion)
    {
        auto working_dir = get_process_cwd();

        snapx snapx("demoapp", working_dir);
        snapx.install("1.0.0");

        const auto run_details = snapx.run_stubexecutable_with_args(std::vector<std::string> {
            "--expected-version=1.0.0"
        });

        ASSERT_EQ(run_details->stub_exit_code, 0);
        ASSERT_EQ(run_details->stub_arguments.size(), 1u);
        ASSERT_EQ(run_details->app_exit_code, demoapp_default_exit_code);
        ASSERT_EQ(run_details->app_arguments.size(), 2u);
        ASSERT_EQ(run_details->app_details.version_str, "1.0.0");

        const auto expected_arguments = std::vector<std::string>{
            run_details->app_details.exe_name_absolute_path,
            run_details->stub_arguments[0]
        };

        for (auto i = 0; i < expected_arguments.size(); i++)
        {
            ASSERT_EQ(expected_arguments[i], run_details->app_arguments[i]);
        }
    }

    TEST(MAIN, corerun_StartsMostRecentVersion)
    {
        auto working_dir = get_process_cwd();

        snapx snapx("demoapp", working_dir);
        snapx.install("1.0.0");
        snapx.install("2.0.0");

        const auto run_details = snapx.run_stubexecutable_with_args(std::vector<std::string> {
            "--expected-version=2.0.0"
        });

        ASSERT_EQ(run_details->stub_exit_code, 0);
        ASSERT_EQ(run_details->stub_arguments.size(), 1u);
        ASSERT_EQ(run_details->app_exit_code, demoapp_default_exit_code);
        ASSERT_EQ(run_details->app_arguments.size(), 2u);
        ASSERT_EQ(run_details->app_details.version_str, "2.0.0");

        const auto expected_arguments = std::vector<std::string>{
            run_details->app_details.exe_name_absolute_path,
            run_details->stub_arguments[0]
        };

        for (auto i = 0; i < expected_arguments.size(); i++)
        {
            ASSERT_EQ(expected_arguments[i], run_details->app_arguments[i]);
        }
    }

    TEST(MAIN, corerun_StartsMostRecentVersionWhenThereAreLotsOfVersionsInRandomOrderInstalled)
    {
        auto working_dir = get_process_cwd();

        snapx snapx("demoapp", working_dir);

        int app_count = 25;

        std::string expected_app_version = std::string();
        std::vector<std::string> app_versions;
        for (auto major_version = 0; major_version <= app_count; major_version++)
        {
            expected_app_version = std::to_string(major_version) + ".0.0";
            app_versions.emplace_back(expected_app_version);
        }

        std::shuffle(std::begin(app_versions), std::end(app_versions), rng);

        for (auto const &app_version : app_versions)
        {
            snapx.install(app_version);
        }

        ASSERT_EQ(expected_app_version, std::to_string(app_count) + ".0.0");

        const auto run_details = snapx.run_stubexecutable_with_args(std::vector<std::string> {
            "--expected-version=" + expected_app_version
        });

        ASSERT_EQ(run_details->stub_exit_code, 0);
        ASSERT_EQ(run_details->stub_arguments.size(), 1u);
        ASSERT_EQ(run_details->app_exit_code, demoapp_default_exit_code);
        ASSERT_EQ(run_details->app_arguments.size(), 2u);
        ASSERT_EQ(run_details->app_details.version_str, expected_app_version);

        const auto expected_arguments = std::vector<std::string>{
            run_details->app_details.exe_name_absolute_path,
            run_details->stub_arguments[0]
        };

        for (auto i = 0; i < expected_arguments.size(); i++)
        {
            ASSERT_EQ(expected_arguments[i], run_details->app_arguments[i]);
        }
    }

}
