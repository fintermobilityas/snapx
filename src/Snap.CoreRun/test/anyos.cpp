#include "gtest/gtest.h"
#include "main.hpp"
#include "crossguid/Guid.hpp"
#include "nlohmann/json.hpp"

#include <string>
#include <algorithm>

using json = nlohmann::json;

const int demoapp_default_exit_code = 127;

namespace {

    class snapx_app_details
    {
    public:
        std::string working_dir;
        std::string version_str;
        version::Semver200_version version;
        std::string exe_name_absolute_path;
        std::string exe_name_relative_path;

        snapx_app_details()
        {

        }

        snapx_app_details(std::string working_dir, std::string exe_name_absolute_path, std::string exe_name_relative_path,
            std::string version, bool version_invalid) :
            working_dir(working_dir),
            exe_name_absolute_path(exe_name_absolute_path),
            exe_name_relative_path(exe_name_relative_path),
            version_str(version),
            version(version::Semver200_version(version_invalid ? "0.0.0" : version))
        {

        }
    };

    class stubexecutable_run_details
    {
    public:
        std::vector<std::string> stub_arguments;
        int stub_exit_code;
        snapx_app_details app_details;
        int app_exit_code;
        std::vector<std::string> app_arguments;

        stubexecutable_run_details() :
            stub_exit_code(0),
            app_exit_code(0)
        {

        }
    };

    class snapx 
    {
    private:
        xg::Guid m_unique_id;
        std::vector<snapx_app_details> m_apps;

    public:
        std::string app_name;
        std::string working_dir;
        std::string working_dir_demoapp_exe;
        std::string working_dir_corerun_exe;
        std::string install_dir;
        std::string install_dir_corerun_exe;
        std::string os_file_ext;

        snapx() = delete;

        snapx(std::string app_name, std::string working_dir) :
            snapx(app_name, working_dir, pal_is_windows() ? ".exe" : "")
        {

        }

        void install(std::string version, std::string app_dir_prefix = "app-", bool version_invalid = false)
        {
            auto app_dir = std::string(install_dir + PAL_DIRECTORY_SEPARATOR_STR + (app_dir_prefix + version));
            auto app_dir_demoapp_exe = std::string(app_dir + PAL_DIRECTORY_SEPARATOR_STR + app_name + os_file_ext);

            ASSERT_TRUE(pal_fs_mkdir(app_dir.c_str(), 0777));
            file_copy(working_dir_demoapp_exe.c_str(), app_dir_demoapp_exe.c_str());

            m_apps.emplace_back(snapx_app_details(app_dir, app_dir_demoapp_exe, app_name + os_file_ext, version, version_invalid));
        }

        stubexecutable_run_details run_stubexecutable_with_args(std::vector<std::string> arguments)
        {
            const auto argc = static_cast<int>(arguments.size());
            const auto argv = new char*[argc];

            for (auto i = 0; i < argc; i++)
            {
                argv[i] = _strdup(arguments[i].c_str());
            }

            int stub_executable_exit_code = 0;
            if (!pal_process_exec(install_dir_corerun_exe.c_str(), install_dir.c_str(), argc, argv, &stub_executable_exit_code))
            {
                stub_executable_exit_code = 1;
            }

            delete[] argv;

            auto log_output = try_read_log_output();
            auto remaining_attempts = 5;
            while (--remaining_attempts > 0 && (log_output = try_read_log_output()).empty())
            {
                pal_usleep(100);
            }

            stubexecutable_run_details run_details;
            run_details.stub_exit_code = stub_executable_exit_code;
            for (const auto &value : arguments)
            {
                run_details.stub_arguments.emplace_back(value);
            }

            if (run_details.stub_exit_code != 0 || log_output.empty())
            {
                return run_details;
            }

            auto json_log_output = json::parse(log_output);
            run_details.app_arguments = json_log_output["arguments"].get<std::vector<std::string>>();
            run_details.app_exit_code = json_log_output["exit_code"].get<int>();

            const auto working_dir = json_log_output["working_dir"].get<std::string>();
            const auto command = json_log_output["command"].get<std::string>();

            if (command == "--expected-version=")
            {
                for (auto const& app : m_apps)
                {
                    if (app.working_dir == working_dir)
                    {
                        run_details.app_details = app;
                        break;
                    }
                }
            }

            return run_details;
        }

        ~snapx()
        {
            EXPECT_TRUE(pal_fs_rmdir(install_dir.c_str(), TRUE));
        }

    private:
        snapx(std::string app_name, std::string working_dir, std::string os_file_ext) :
            m_unique_id(xg::newGuid()),
            app_name(app_name),
            working_dir(working_dir),
            working_dir_corerun_exe(working_dir + PAL_DIRECTORY_SEPARATOR_STR + "corerun" + os_file_ext),
            working_dir_demoapp_exe(working_dir + PAL_DIRECTORY_SEPARATOR_STR + "corerun_demoapp" + os_file_ext),
            install_dir(working_dir + PAL_DIRECTORY_SEPARATOR_STR + m_unique_id.str()),
            install_dir_corerun_exe(install_dir + PAL_DIRECTORY_SEPARATOR_STR + app_name + os_file_ext),
            os_file_ext(os_file_ext)
        {
            init();
        }

    private:

        void file_copy(const char* src_filename, const char* dest_filename)
        {
            ASSERT_NE(src_filename, nullptr);
            ASSERT_NE(dest_filename, nullptr);

            ASSERT_TRUE(pal_fs_file_exists(src_filename));

            char* bytes = nullptr;
            int bytes_len = 0;
            ASSERT_TRUE(pal_fs_read_file(src_filename, "rb", &bytes, &bytes_len));
            ASSERT_GT(bytes_len, 0);

            pal_file_handle_t* dst_file_handle;
            ASSERT_TRUE(pal_fs_fopen(dest_filename, "wb", &dst_file_handle));
            ASSERT_TRUE(pal_fs_fwrite(dst_file_handle, bytes, bytes_len));
            ASSERT_TRUE(pal_fs_fclose(dst_file_handle));

            size_t dst_filesize = 0u;
            ASSERT_TRUE(pal_fs_get_file_size(dest_filename, &dst_filesize));
            ASSERT_EQ(bytes_len, static_cast<int>(dst_filesize)); // TODO: FIX ME -> size_t

            ASSERT_TRUE(pal_fs_chmod(dest_filename, 0777));
        }

        void init()
        {
            ASSERT_TRUE(pal_fs_file_exists(working_dir_corerun_exe.c_str()));
            ASSERT_TRUE(pal_fs_file_exists(working_dir_demoapp_exe.c_str()));
            ASSERT_TRUE(pal_fs_mkdir(install_dir.c_str(), 0777));
            file_copy(working_dir_corerun_exe.c_str(), install_dir_corerun_exe.c_str());
        }

        snapx_app_details find_current_app_details()
        {
            snapx_app_details most_recent_app;

            for (const auto &app : m_apps)
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
            const auto log_filename = most_recent_app.exe_name_relative_path + ".json";

            char* log_filename_absolute_path = nullptr;
            EXPECT_TRUE(pal_fs_path_combine(most_recent_app.working_dir.c_str(), log_filename.c_str(), &log_filename_absolute_path));

            char* log_output = nullptr;
            int log_output_len = 0;
            if (!pal_fs_read_file(log_filename_absolute_path, "r", &log_output, &log_output_len))
            {
                return std::string();
            }

            return std::string(log_output);
        }

    };

    TEST(MAIN, corerun_StartsWhenThereAreZeroAppsInstalled)
    {
        char* working_dir = nullptr;
        ASSERT_TRUE(pal_fs_get_cwd(&working_dir));

        snapx snapx("demoapp", working_dir);

        const auto run_details = snapx.run_stubexecutable_with_args(std::vector<std::string>());

        ASSERT_EQ(run_details.stub_exit_code, 1);
        ASSERT_EQ(run_details.stub_arguments.size(), 0u);
        ASSERT_EQ(run_details.app_exit_code, 0);
        ASSERT_EQ(run_details.app_arguments.size(), 0);
        ASSERT_EQ(run_details.app_details.version_str, "");
    }
    
    TEST(MAIN, corerun_ExcludesAppDirectoriesWithInvalidPrefix)
    {
        char* working_dir = nullptr;
        ASSERT_TRUE(pal_fs_get_cwd(&working_dir));

        snapx snapx("demoapp", working_dir);
        snapx.install("1.0.0", "notanapp-");
        snapx.install("2.0.0");
        snapx.install("3.0.0", "notanapp-");
        snapx.install("4.0.0");

        const auto run_details = snapx.run_stubexecutable_with_args(std::vector<std::string> {
            "--expected-version=4.0.0"
        });

        ASSERT_EQ(run_details.stub_exit_code, 0);
        ASSERT_EQ(run_details.stub_arguments.size(), 1u);
        ASSERT_EQ(run_details.app_exit_code, demoapp_default_exit_code);
        ASSERT_EQ(run_details.app_arguments.size(), 2u);
        ASSERT_EQ(run_details.app_details.version_str, "4.0.0");

        const auto expected_arguments = std::vector<std::string>{
            run_details.app_details.exe_name_absolute_path,
            run_details.stub_arguments[0]
        };

        for (auto i = 0; i < expected_arguments.size(); i++)
        {
            ASSERT_EQ(expected_arguments[i], run_details.app_arguments[i]);
        }
    }

    TEST(MAIN, corerun_ExcludesAppDirectoriesWithInvalidSemver)
    {
        char* working_dir = nullptr;
        ASSERT_TRUE(pal_fs_get_cwd(&working_dir));

        snapx snapx("demoapp", working_dir);
        snapx.install("1.0.0");
        snapx.install("2..0.0", "app-", true);
        snapx.install("3.0...0", "app", true);
        snapx.install("4.0.0");

        const auto run_details = snapx.run_stubexecutable_with_args(std::vector<std::string> {
            "--expected-version=4.0.0"
        });

        ASSERT_EQ(run_details.stub_exit_code, 0);
        ASSERT_EQ(run_details.stub_arguments.size(), 1u);
        ASSERT_EQ(run_details.app_exit_code, demoapp_default_exit_code);
        ASSERT_EQ(run_details.app_arguments.size(), 2u);
        ASSERT_EQ(run_details.app_details.version_str, "4.0.0");

        const auto expected_arguments = std::vector<std::string>{
            run_details.app_details.exe_name_absolute_path,
            run_details.stub_arguments[0]
        };

        for (auto i = 0; i < expected_arguments.size(); i++)
        {
            ASSERT_EQ(expected_arguments[i], run_details.app_arguments[i]);
        }
    }

    TEST(MAIN, corerun_StartsInitialVersion)
    {
        char* working_dir = nullptr;
        ASSERT_TRUE(pal_fs_get_cwd(&working_dir));

        snapx snapx("demoapp", working_dir);
        snapx.install("10.0.0");

        const auto run_details = snapx.run_stubexecutable_with_args(std::vector<std::string> {
            "--expected-version=10.0.0"
        });

        ASSERT_EQ(run_details.stub_exit_code, 0);
        ASSERT_EQ(run_details.stub_arguments.size(), 1u);
        ASSERT_EQ(run_details.app_exit_code, demoapp_default_exit_code);
        ASSERT_EQ(run_details.app_arguments.size(), 2u);
        ASSERT_EQ(run_details.app_details.version_str, "10.0.0");

        const auto expected_arguments = std::vector<std::string>{
            run_details.app_details.exe_name_absolute_path,
            run_details.stub_arguments[0]
        };

        for (auto i = 0; i < expected_arguments.size(); i++)
        {
            ASSERT_EQ(expected_arguments[i], run_details.app_arguments[i]);
        }
    }

    TEST(MAIN, corerun_StartsMostRecentVersion)
    {
        char* working_dir = nullptr;
        ASSERT_TRUE(pal_fs_get_cwd(&working_dir));

        snapx snapx("demoapp", working_dir);
        snapx.install("100.0.0");
        snapx.install("10000.0.0");
        snapx.install("10.0.0");
        snapx.install("1000.0.0");

        const auto run_details = snapx.run_stubexecutable_with_args(std::vector<std::string> {
            "--expected-version=10000.0.0"
        });

        ASSERT_EQ(run_details.stub_exit_code, 0);
        ASSERT_EQ(run_details.stub_arguments.size(), 1u);
        ASSERT_EQ(run_details.app_exit_code, demoapp_default_exit_code);
        ASSERT_EQ(run_details.app_arguments.size(), 2u);
        ASSERT_EQ(run_details.app_details.version_str, "10000.0.0");

        const auto expected_arguments = std::vector<std::string>{
            run_details.app_details.exe_name_absolute_path,
            run_details.stub_arguments[0]
        };

        for (auto i = 0; i < expected_arguments.size(); i++)
        {
            ASSERT_EQ(expected_arguments[i], run_details.app_arguments[i]);
        }
    }

}
