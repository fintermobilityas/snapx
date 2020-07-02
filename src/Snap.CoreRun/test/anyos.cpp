#include "gtest/gtest.h"
#include "main.hpp"
#include "crossguid/Guid.hpp"
#include "nlohmann/json.hpp"
#include "vendor/semver/semver200.h"
#include "tests/support/utils.hpp"

#include <string>
#include <algorithm>
#include <random>
#include <utility>

using json = nlohmann::json;
using testutils = corerun::support::util::test_utils;

const int demoapp_default_exit_code = 0;

static std::random_device dev;
static auto rng = std::mt19937_64 { dev() };

namespace {

    class corerun_app_details
    {
    public:
        std::string working_dir;
        std::string version_str;
        version::Semver200_version version;
        std::string exe_name_absolute_path;
        std::string exe_name_relative_path;

        corerun_app_details() : 
            working_dir(std::string()),
            version_str(std::string()),
            version(version::Semver200_version()),
            exe_name_absolute_path(std::string()),
            exe_name_relative_path(std::string())
        {
            
        }

        corerun_app_details(std::string working_dir, std::string exe_name_absolute_path,
            std::string exe_name_relative_path,
            const std::string& version, const bool version_invalid) : 
            working_dir(std::move(working_dir)),
            version_str(version),
            version(version::Semver200_version(version_invalid ? "0.0.0" : version)),
            exe_name_absolute_path(std::move(exe_name_absolute_path)),
            exe_name_relative_path(std::move(exe_name_relative_path))
        {

        }
    };

    class corerun_run_details
    {
    public:
        std::string install_dir;

        corerun_run_details() = delete;

        explicit corerun_run_details(std::string install_dir) : install_dir(std::move(install_dir))
        {

        }

        ~corerun_run_details() {
            pal_fs_rmdir(this->install_dir.c_str(), TRUE);
        }
    };

    class stubexecutable_run_details : corerun_run_details
    {
    public:
        std::vector<std::string> stub_arguments;
        pal_exit_code_t stub_exit_code{};
        corerun_app_details app_details;
        pal_exit_code_t app_exit_code{};
        std::vector<std::string> app_arguments;
        std::string run_working_dir;
        std::string run_command;

        explicit stubexecutable_run_details(const std::string& install_dir) :
            corerun_run_details(install_dir),
            stub_arguments(std::vector<std::string>()),
            stub_exit_code(pal_exit_code_t(-1)),
            app_details(corerun_app_details()),
            app_exit_code(pal_exit_code_t(-1)),
            app_arguments(std::vector<std::string>()),
            run_working_dir(std::string()),
            run_command(std::string())
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

    private:
        snapx(const std::string& app_name, const std::string& working_dir, const std::string& os_file_ext) :
            m_unique_id(xg::newGuid()),
            m_apps(std::vector<corerun_app_details>()),
            app_name(app_name),
            working_dir(working_dir),
            working_dir_demoapp_exe(testutils::path_combine(working_dir, "corerun_demoapp" + os_file_ext)),
            working_dir_corerun_exe(testutils::path_combine(working_dir, "corerun" + os_file_ext)),
            install_dir(testutils::path_combine(working_dir, m_unique_id)),
            install_dir_corerun_exe(testutils::path_combine(install_dir, app_name + os_file_ext)),
            os_file_ext(os_file_ext)
        {
            init();
        }

    public:
        snapx() = delete;

        snapx(const std::string& app_name, const std::string& working_dir) :
            snapx(app_name, working_dir, pal_is_windows() ? ".exe" : "")
        {

        }

        void install(const std::string& version, const std::string& app_dir_prefix = "app-", bool version_invalid = false)
        {
            const auto app_dir = testutils::path_combine(this->install_dir, app_dir_prefix + version);
            const auto app_dir_demoapp_exe = testutils::path_combine(app_dir, this->app_name + this->os_file_ext);

            ASSERT_TRUE(pal_fs_mkdirp(app_dir.c_str(), this_exe::default_permissions)) << "Failed to create app dir: " << app_dir;
            ASSERT_TRUE(file_copy(this->working_dir_demoapp_exe.c_str(), app_dir_demoapp_exe.c_str())) << "Failed copy demoapp" << this->working_dir_demoapp_exe;

            this->m_apps.emplace_back(corerun_app_details(app_dir, app_dir_demoapp_exe,
                this->app_name + this->os_file_ext, version, version_invalid));
        }

        static bool file_copy(const char* src_filename, const char* dest_filename)
        {
            if (src_filename == nullptr
                || dest_filename == nullptr)
            {
                return false;
            }
            return testutils::file_copy(std::string(src_filename), std::string(dest_filename));
        }

        std::unique_ptr<stubexecutable_run_details> run_stubexecutable_with_args(const std::vector<std::string>& arguments)
        {
            const auto argc = arguments.size();
            auto* const argv = new char*[argc] {};

            for (auto i = 0u; i < argc; i++)
            {
                argv[i] = _strdup(arguments[i].c_str());
            }

            auto run_details = std::make_unique<stubexecutable_run_details>(this->install_dir);

            for (const auto &value : arguments)
            {
                run_details->stub_arguments.emplace_back(value);
            }

            if (!pal_fs_directory_exists(this->install_dir.c_str()))
            {
                throw std::runtime_error("Fatal error! Install directory does not exist: " + this->install_dir);
            }

            pal_exit_code_t stub_executable_exit_code = 0;
            if (!pal_process_exec(this->install_dir_corerun_exe.c_str(), this->install_dir.c_str(), static_cast<int>(argc), argv,
                &stub_executable_exit_code))
            {
                throw std::runtime_error("Failed to start stub executable: " + this->install_dir_corerun_exe + ". Install dir: " + this->install_dir);
            }

            run_details->stub_exit_code = stub_executable_exit_code;
            delete[] argv;

            auto attempts = 5;
            std::string log_output;
            while(attempts-- > 0)
            {
                // We are not synchronizing write of logoutput between parent and child process.
                // This means that the child process may still be writing to the file while we are reading.

                log_output = try_read_log_output();
                if (!log_output.empty())
                {
                    break;
                }

                pal_sleep_ms(300);
            }

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
                LOGE << "Failed to parse json output log. What: " << ex.what() << ". Output: " << log_output;
                return run_details;
            }

            run_details->app_arguments = json_log_output["arguments"].get<std::vector<std::string>>();
            run_details->app_exit_code = json_log_output["exit_code"].get<pal_exit_code_t>();
            run_details->run_working_dir = json_log_output["working_dir"].get<std::string>();
            run_details->run_command = json_log_output["command"].get<std::string>(); 
            
            auto expected_command = std::string();
            if(!arguments.empty())
            {
                expected_command = arguments[0];
            }

            for (const auto &app : this->m_apps)
            {
                if (expected_command == run_details->run_command 
                    && app.working_dir == run_details->run_working_dir)
                {
                    run_details->app_details = app;
                    break;
                }
            }

            return run_details;
        }

    private:

        void init() const
        {
            ASSERT_TRUE(pal_fs_file_exists(this->working_dir_corerun_exe.c_str()));
            ASSERT_TRUE(pal_fs_file_exists(this->working_dir_demoapp_exe.c_str()));
            ASSERT_TRUE(pal_fs_mkdirp(this->install_dir.c_str(), this_exe::default_permissions));
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

            const auto log_filename_absolute_path = testutils::path_combine(most_recent_app.working_dir, log_filename);
            if (log_filename_absolute_path.empty())
            {
                LOGE << "Log file not found: " << log_filename_absolute_path;
                return std::string();
            }

            const auto log_output = std::make_unique<char*>(new char);
            size_t log_output_len = 0;
            if (!pal_fs_read_file(log_filename_absolute_path.c_str(), log_output.get(), &log_output_len) || log_output_len <= 0)
            {
                LOGE << "Failed to read log file: " << log_filename_absolute_path << ". Size: " << log_output_len;
                return std::string();
            }

            return std::string(*log_output);
        }

    };

    TEST(MAIN, TestsCannotRunInElevatedContext)
    {
        ASSERT_NO_THROW(pal_is_elevated());
    }
    
    TEST(MAIN, corerun_StartsWhenThereAreZeroAppsInstalled)
    {
        const auto working_dir = testutils::get_process_cwd();

        snapx snapx("demoapp", working_dir);

        const auto run_details = snapx.run_stubexecutable_with_args(std::vector<std::string>());

        ASSERT_EQ(run_details->stub_exit_code, 1);
        ASSERT_EQ(run_details->stub_arguments.size(), 0u);
        ASSERT_EQ(run_details->app_exit_code, -1);
        ASSERT_EQ(run_details->app_arguments.size(), 0);
        ASSERT_EQ(run_details->app_details.version_str, "");
        ASSERT_STREQ(run_details->run_working_dir.c_str(), "");
        ASSERT_STREQ(run_details->run_command.c_str(), "");
    }

    TEST(MAIN, corerun_ExcludesAppDirectoriesWithInvalidPrefix)
    {
        const auto working_dir = testutils::get_process_cwd();

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
        ASSERT_STREQ(run_details->run_working_dir.c_str(), run_details->app_details.working_dir.c_str());
        ASSERT_STREQ(run_details->run_command.c_str(), run_details->stub_arguments[0].c_str());

        const auto expected_arguments = std::vector<std::string>{
            run_details->app_details.exe_name_absolute_path,
            run_details->stub_arguments[0]
        };

        for (auto i = 0u; i < expected_arguments.size(); i++)
        {
            ASSERT_EQ(expected_arguments[i], run_details->app_arguments[i]);
        }
    }

    TEST(MAIN, corerun_ExcludesAppDirectoriesWithInvalidSemver)
    {
        const auto working_dir = testutils::get_process_cwd();

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
        ASSERT_STREQ(run_details->run_working_dir.c_str(), run_details->app_details.working_dir.c_str());
        ASSERT_STREQ(run_details->run_command.c_str(), run_details->stub_arguments[0].c_str());

        const auto expected_arguments = std::vector<std::string>{
            run_details->app_details.exe_name_absolute_path,
            run_details->stub_arguments[0]
        };

        for (auto i = 0u; i < expected_arguments.size(); i++)
        {
            ASSERT_EQ(expected_arguments[i], run_details->app_arguments[i]);
        }
    }

    TEST(MAIN, corerun_StartsInitialVersion)
    {
        const auto working_dir = testutils::get_process_cwd();

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
        ASSERT_STREQ(run_details->run_working_dir.c_str(), run_details->app_details.working_dir.c_str());
        ASSERT_STREQ(run_details->run_command.c_str(), run_details->stub_arguments[0].c_str());

        const auto expected_arguments = std::vector<std::string>{
            run_details->app_details.exe_name_absolute_path,
            run_details->stub_arguments[0]
        };

        for (auto i = 0u; i < expected_arguments.size(); i++)
        {
            ASSERT_EQ(expected_arguments[i], run_details->app_arguments[i]);
        }
    }

    TEST(MAIN, corerun_StartsMostRecentVersion)
    {
        const auto working_dir = testutils::get_process_cwd();

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
        ASSERT_STREQ(run_details->run_working_dir.c_str(), run_details->app_details.working_dir.c_str());
        ASSERT_STREQ(run_details->run_command.c_str(), run_details->stub_arguments[0].c_str());

        const auto expected_arguments = std::vector<std::string>{
            run_details->app_details.exe_name_absolute_path,
            run_details->stub_arguments[0]
        };

        for (auto i = 0u; i < expected_arguments.size(); i++)
        {
            ASSERT_EQ(expected_arguments[i], run_details->app_arguments[i]);
        }
    }

    TEST(MAIN, corerun_StartsMostRecentVersionWhenThereAreLotsOfVersionsInRandomOrderInstalled)
    {
        const auto working_dir = testutils::get_process_cwd();

        snapx snapx("demoapp", working_dir);

        const auto app_count = 25;

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
        ASSERT_STREQ(run_details->run_working_dir.c_str(), run_details->app_details.working_dir.c_str());
        ASSERT_STREQ(run_details->run_command.c_str(), run_details->stub_arguments[0].c_str());

        const auto expected_arguments = std::vector<std::string>{
            run_details->app_details.exe_name_absolute_path,
            run_details->stub_arguments[0]
        };

        for (auto i = 0u; i < expected_arguments.size(); i++)
        {
            ASSERT_EQ(expected_arguments[i], run_details->app_arguments[i]);
        }
    }

}
