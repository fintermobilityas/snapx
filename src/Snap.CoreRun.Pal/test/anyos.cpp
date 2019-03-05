#include "gtest/gtest.h"
#include "pal/pal.hpp"
#include "nlohmann/json.hpp"
#include "tests/support/utils.hpp"

using json = nlohmann::json;
using testutils = corerun::support::util::test_utils;

namespace
{
    TEST(PAL_GENERIC, pal_set_icon_DoesNotSegfault)
    {
        pal_set_icon(nullptr, nullptr);
    }

    TEST(PAL_GENERIC, pal_has_icon_DoesNotSegfault)
    {
        pal_has_icon(nullptr);
    }

    TEST(PAL_GENERIC, pal_process_get_name_ReturnsThisProcessExeName)
    {
        char* exe_name = nullptr;
        EXPECT_TRUE(pal_process_get_name(&exe_name));
        EXPECT_NE(exe_name, nullptr);
        EXPECT_TRUE(pal_str_startswith(exe_name, "corerun_tests"));
    }

    TEST(PAL_GENERIC, pal_process_is_running)
    {
        pal_pid_t pid;
        EXPECT_TRUE(pal_process_get_pid(&pid));
        EXPECT_TRUE(pal_process_is_running(pid));

        pal_pid_t next_pid = 1000;
        auto process_is_running = TRUE;

        while(process_is_running)
        {
            if(next_pid > 25000)
            {
                break;
            }

            process_is_running = pal_process_is_running(next_pid);
            ++next_pid;
        }

        ASSERT_EQ(process_is_running, FALSE);
    }

    TEST(PAL_GENERIC, pal_isdebuggerpresent_DoesNotSegfault)
    {
        pal_isdebuggerpresent();
    }

    TEST(PAL_GENERIC, pal_wait_for_debugger_DoesNotSegfault)
    {
        if (!pal_isdebuggerpresent())
        {
            return;
        }
        EXPECT_TRUE(pal_wait_for_debugger());
    }

    TEST(PAL_GENERIC, pal_load_library_DoesNotSegFault)
    {
        void** instance = nullptr;
        EXPECT_FALSE(pal_load_library(nullptr, FALSE, instance));
        EXPECT_EQ(instance, nullptr);
    }

    TEST(PAL_GENERIC, pal_free_library_DoesNotSegfault)
    {
        EXPECT_FALSE(pal_free_library(nullptr));
    }

    TEST(PAL_GENERIC, pal_getprocaddress_DoesNotSegfault)
    {
        void** fn_ptr = nullptr;
        EXPECT_FALSE(pal_getprocaddress(nullptr, nullptr, fn_ptr));
        EXPECT_EQ(fn_ptr, nullptr);
    }

    TEST(PAL_GENERIC, pal_set_icon_DoesNotSegFault)
    {
        EXPECT_FALSE(pal_set_icon(nullptr, nullptr));
    }

    TEST(PAL_GENERIC, pal_process_get_pid_ReturnsValueGreaterThanZero)
    {
        pal_pid_t pid;
        EXPECT_EQ(TRUE, pal_process_get_pid(&pid));
        EXPECT_GT(pid, 0u);
    }

    TEST(PAL_GENERIC, pal_is_elevated_DoesNotSegfault)
    {
        pal_is_elevated();
    }

    TEST(PAL_GENERIC, pal_process_is_running_ReturnsTrueForThisProcess)
    {
        pal_pid_t pid;
        pal_process_get_pid(&pid);
        EXPECT_GT(pid, 0u);
        EXPECT_TRUE(pal_process_is_running(pid));
    }

    TEST(PAL_GENERIC, pal_sleep_ms_DoesNotSegFault)
    {
        pal_sleep_ms(0);
        pal_sleep_ms(1);
    }

    TEST(PAL_GENERIC, pal_is_unknown_os)
    {
        EXPECT_FALSE(pal_is_unknown_os());
    }

    TEST(PAL_ENV, pal_env_set_DoesNotSegfault)
    {
        EXPECT_FALSE(pal_env_set(nullptr, nullptr));
    }

    TEST(PAL_ENV, pal_env_set_NullptrDeletesVariable)
    {
        const auto random_variable = testutils::build_random_str();
        EXPECT_TRUE(pal_env_set(random_variable.c_str(), nullptr));
        char* value = nullptr;
        EXPECT_FALSE(pal_env_get(random_variable.c_str(), &value));
    }

    TEST(PAL_ENV, pal_env_set_Overwrite)
    {
        const auto random_variable = testutils::build_random_str();
        EXPECT_TRUE(pal_env_set(random_variable.c_str(), "TEST"));
        EXPECT_TRUE(pal_env_set(random_variable.c_str(), "TEST2"));
        char* value = nullptr;
        EXPECT_TRUE(pal_env_get(random_variable.c_str(), &value));
        EXPECT_STREQ(value, "TEST2");
    }

    TEST(PAL_ENV, pal_env_set)
    {
        const auto random_variable = testutils::build_random_str();
        const auto random_text = testutils::build_random_str();
        EXPECT_TRUE(pal_env_set(random_variable.c_str(), random_text.c_str()));
        char* value = nullptr;
        EXPECT_TRUE(pal_env_get(random_variable.c_str(), &value));
        EXPECT_STREQ(value, random_text.c_str());
    }

    TEST(PAL_ENV, pal_env_get_variable_DoesNotSegFault)
    {
        char** value = nullptr;
        EXPECT_FALSE(pal_env_get(nullptr, value));
        EXPECT_EQ(value, nullptr);
    }

    TEST(PAL_ENV, pal_env_get_bool_DoesNotSegFault)
    {
        EXPECT_FALSE(pal_env_get_bool(nullptr));
    }

    TEST(PAL_ENV, pal_env_expand_str_DoesNotSegfault)
    {
        char** value = nullptr;
        EXPECT_FALSE(pal_env_expand_str(nullptr, value));
        EXPECT_EQ(value, nullptr);
    }

    TEST(PAL_FS, pal_fs_chmod_DoesNotSegfault)
    {
        EXPECT_FALSE(pal_fs_chmod(nullptr, 0));
    }

    TEST(PAL_FS, pal_fs_get_directory_name_absolute_path_DoesNotSegfault)
    {
        char** path = nullptr;
        EXPECT_FALSE(pal_fs_get_directory_name_absolute_path(nullptr, path));
        EXPECT_EQ(path, nullptr);
    }

    TEST(PAL_FS, pal_fs_get_directory_name_DoesNotSegfault)
    {
        char** directory_name = nullptr;
        EXPECT_FALSE(pal_fs_get_directory_name(nullptr, directory_name));
        EXPECT_EQ(directory_name, nullptr);
    }

    TEST(PAL_FS, pal_fs_path_combine_DoesNotSegFault)
    {
        char** path = nullptr;
        EXPECT_FALSE(pal_fs_path_combine(nullptr, nullptr, path));
        EXPECT_EQ(path, nullptr);
    }

    TEST(PAL_FS, pal_fs_file_exists_ReturnsFalseIfDirectory)
    {
        char* working_dir = nullptr;
        EXPECT_TRUE(pal_process_get_cwd(&working_dir));
        EXPECT_NE(working_dir, nullptr);
        ASSERT_FALSE(pal_fs_file_exists(working_dir));
    }

    TEST(PAL_FS, pal_fs_file_exists_ReturnsTrueWhenAbsolutePath)
    {
        char* exe_abs_path = nullptr;
        EXPECT_TRUE(pal_process_get_real_path(&exe_abs_path));
        EXPECT_NE(exe_abs_path, nullptr);
        EXPECT_TRUE(pal_fs_file_exists(exe_abs_path));
    }

    TEST(PAL_FS, pal_fs_list_directories_DoesNotSegfault)
    {
        char** directories_array = nullptr;
        size_t directories_len = 0u;
        EXPECT_FALSE(pal_fs_list_directories(nullptr, nullptr, nullptr, &directories_array, &directories_len));
        EXPECT_EQ(directories_array, nullptr);
        EXPECT_EQ(directories_len, 0u);
    }

    TEST(PAL_FS, pal_fs_list_directories_ReturnsDirectoriesInCurrentWorkingDirectory)
    {
        const auto working_dir = testutils::get_process_cwd();
        const auto random_dir = testutils::mkdir_random(working_dir);
        ASSERT_TRUE(pal_fs_directory_exists(random_dir.c_str()));

        char** directories_array = nullptr;
        size_t directories_len = 0u;
        EXPECT_TRUE(pal_fs_list_directories(working_dir.c_str(), nullptr, nullptr, &directories_array, &directories_len));
        EXPECT_NE(directories_array, nullptr);
        EXPECT_GT(directories_len, 0u);

        std::vector<std::string> directories(directories_array, directories_array + directories_len);
        EXPECT_EQ(directories.size(), directories_len);

        for (const auto directory : directories)
        {
            EXPECT_FALSE(directory.empty());
            EXPECT_TRUE(pal_fs_directory_exists(directory.c_str()));
            EXPECT_FALSE(pal_fs_file_exists(directory.c_str()));
        }
    }

    TEST(PAL_FS, pal_fs_list_files_DoesNotSegfault)
    {
        char** files = nullptr;
        size_t files_len = 0u;
        EXPECT_FALSE(pal_fs_list_files(nullptr, nullptr, nullptr, &files, &files_len));
        EXPECT_EQ(files, nullptr);
        EXPECT_EQ(files_len, 0u);
    }

    TEST(PAL_FS, pal_fs_list_files_ReturnsAListOfFilesInCurrentWorkingDirectory)
    {
        char* working_dir = nullptr;
        EXPECT_TRUE(pal_process_get_cwd(&working_dir));
        EXPECT_NE(working_dir, nullptr);

        char** files_array = nullptr;
        size_t files_len = 0u;
        EXPECT_TRUE(pal_fs_list_files(working_dir, nullptr, nullptr, &files_array, &files_len));
        EXPECT_NE(files_array, nullptr);
        EXPECT_GT(files_len, 0u);

        std::vector<std::string> files(files_array, files_array + files_len);
        EXPECT_EQ(files.size(), files_len);

        for (const auto file : files)
        {
            EXPECT_FALSE(file.empty());
            EXPECT_TRUE(pal_fs_file_exists(file.c_str()));
            EXPECT_FALSE(pal_fs_directory_exists(file.c_str()));
        }
    }

    TEST(PAL_FS, pal_process_get_real_path)
    {
        char* this_process_real_path = nullptr;
        EXPECT_TRUE(pal_process_get_real_path(&this_process_real_path));
        EXPECT_NE(this_process_real_path, nullptr);
    }

    TEST(PAL_FS, pal_fs_get_absolute_path_DoesNotSegfault)
    {
        char* path = nullptr;
        EXPECT_FALSE(pal_fs_get_absolute_path(nullptr, &path));
        EXPECT_EQ(path, nullptr);
    }

    TEST(PAL_FS, pal_fs_directory_exists_ReturnsTrueThatThisWorkingDirectoryExists)
    {
        char* working_dir = nullptr;
        EXPECT_TRUE(pal_process_get_cwd(&working_dir));
        EXPECT_TRUE(pal_fs_directory_exists(working_dir));
    }

    TEST(PAL_FS, pal_fs_get_file_size_ReturnsFalseWhenFileDoesNotExist)
    {
        auto filename = testutils::build_random_filename();
        size_t file_size = 0;
        EXPECT_FALSE(pal_fs_get_file_size(filename.c_str(), &file_size));
        EXPECT_EQ(file_size, 0u);
    }

    TEST(PAL_FS, pal_fs_get_file_size_ReturnsFalseWhenDirectoryDoesNotExist)
    {
        auto dir_name = testutils::build_random_dirname();
        size_t file_size = 0;
        EXPECT_FALSE(pal_fs_get_file_size(dir_name.c_str(), &file_size));
        EXPECT_EQ(file_size, 0u);
    }

    TEST(PAL_FS, pal_fs_get_file_size_ReturnsAValueGreaterThanZero)
    {
        char* exe_abs_path = nullptr;
        EXPECT_TRUE(pal_process_get_real_path(&exe_abs_path));
        EXPECT_NE(exe_abs_path, nullptr);

        size_t file_size = 0;
        EXPECT_TRUE(pal_fs_get_file_size(exe_abs_path, &file_size));
        EXPECT_GT(file_size, 0u);
    }

    TEST(PAL_FS, pal_fs_read_file_ReadsCurrentProcessBinaryData)
    {
        char* exe_abs_path = nullptr;
        EXPECT_TRUE(pal_process_get_real_path(&exe_abs_path));
        EXPECT_NE(exe_abs_path, nullptr);

        size_t expected_file_size = 0;
        EXPECT_TRUE(pal_fs_get_file_size(exe_abs_path, &expected_file_size));
        EXPECT_GT(expected_file_size, 0u);

        char* bytes = nullptr;
        size_t bytes_len = 0;
        EXPECT_TRUE(pal_fs_read_binary_file(exe_abs_path, &bytes, &bytes_len));
        EXPECT_NE(bytes, nullptr);
        EXPECT_GT(bytes_len, 0);

        for (auto i = 0u; i < bytes_len; i++)
        {
            EXPECT_NE(&bytes[i], nullptr);
        }
    }

    TEST(PAL_FS, pal_fs_read_file_Json)
    {
        const auto random_filename = testutils::build_random_filename();
        const auto working_dir = testutils::get_process_cwd();
        const auto dst_json_filename = testutils::mkfile_random(working_dir.c_str(), random_filename.c_str());

        json doc = {
            {"pi", 3.141},
            {"happy", true},
            {"name", "Niels"},
            {"nothing", nullptr},
            {"answer", {
                {"everything", 42}
            }},
            {"list", {1, 0, 2}},
            {"object", {
               {"currency", "USD"},
               {"value", 42.99}
            }}
        };

        const auto json_str_before = doc.dump();

        EXPECT_TRUE(pal_fs_write(dst_json_filename.c_str(), "wb", json_str_before.c_str(), json_str_before.size()));

        char* json_after = nullptr;
        size_t json_after_len = 0u;
        ASSERT_TRUE(pal_fs_read_binary_file(dst_json_filename.c_str(), &json_after, &json_after_len));

        json_after[json_after_len] = '\0';
        EXPECT_STREQ(json_str_before.c_str(), json_after) << "Json documents are not equal: " << dst_json_filename.c_str();

        auto doc_after = json::parse(json_after);
        ASSERT_EQ(doc["pi"], doc_after["pi"]);

#if defined(PAL_PLATFORM_WINDOWS) && !defined(PAL_PLATFORM_LINUX) && defined(NDEBUG)
        // Todo: Investigate why assert fails only in Release mode when targeting MSVS.
        // Process explorer cannot find any processes with
        // open file descriptions on the file we are attempting to remove :(
        pal_fs_rmfile(dst_json_filename.c_str());
#else
        EXPECT_TRUE(pal_fs_rmfile(dst_json_filename.c_str()));
#endif
    }

    TEST(PAL_FS, pal_fs_mkdir_DoesNotSegfault)
    {
        EXPECT_FALSE(pal_fs_mkdir(nullptr, 0));
    }

    TEST(PAL_FS, pal_pal_fs_rmdir_DoesNotSegfault)
    {
        EXPECT_FALSE(pal_fs_rmdir(nullptr, FALSE));
        EXPECT_FALSE(pal_fs_rmdir(nullptr, TRUE));
    }

    TEST(PAL_FS, pal_pal_fs_rmdir_RemovesEmptyDirectory)
    {        
        const auto working_dir = testutils::get_process_cwd();
        const auto empty_dir = testutils::mkdir_random(working_dir);
        EXPECT_TRUE(pal_fs_directory_exists(empty_dir.c_str()));
        EXPECT_TRUE(pal_fs_rmdir(empty_dir.c_str(), FALSE));
        EXPECT_FALSE(pal_fs_directory_exists(empty_dir.c_str()));
    }

    TEST(PAL_FS, pal_pal_fs_rmdir_RemovesDirectoryWithASingleFile)
    {
        const auto working_dir = testutils::get_process_cwd();
        const auto directory = testutils::mkdir_random(working_dir);
        const auto filename = testutils::mkfile_random(directory, testutils::build_random_filename().c_str());

        EXPECT_TRUE(pal_fs_directory_exists(directory.c_str()));
        EXPECT_TRUE(pal_fs_file_exists(filename.c_str()));
        EXPECT_TRUE(pal_fs_rmdir(directory.c_str(), TRUE));
        EXPECT_FALSE(pal_fs_directory_exists(directory.c_str()));
    }

    TEST(PAL_FS, pal_pal_fs_rmdir_RemovesDirectoryWithMultipleFiles)
    {
        const auto working_dir = testutils::get_process_cwd();
        const auto directory = testutils::mkdir_random(working_dir);
        const auto filename1 = testutils::mkfile_random(directory, testutils::build_random_filename().c_str());
        const auto filename2 = testutils::mkfile_random(directory, testutils::build_random_filename().c_str());

        EXPECT_TRUE(pal_fs_directory_exists(directory.c_str()));
        EXPECT_TRUE(pal_fs_file_exists(filename1.c_str()));
        EXPECT_TRUE(pal_fs_file_exists(filename2.c_str()));
        EXPECT_TRUE(pal_fs_rmdir(directory.c_str(), TRUE));
        EXPECT_FALSE(pal_fs_directory_exists(directory.c_str()));
    }

    TEST(PAL_FS, pal_pal_fs_rmdir_RemovesDirectoryWithEmptySubDirectory)
    {
        const auto working_dir = testutils::get_process_cwd();
        const auto parent_dir = testutils::mkdir_random(working_dir);
        const auto sub_dir = testutils::mkdir(parent_dir, "subdirectory");

        EXPECT_TRUE(pal_fs_directory_exists(parent_dir.c_str()));
        EXPECT_TRUE(pal_fs_directory_exists(sub_dir.c_str()));
        EXPECT_TRUE(pal_fs_rmdir(parent_dir.c_str(), TRUE));
        EXPECT_FALSE(pal_fs_directory_exists(parent_dir.c_str()));
    }

    TEST(PAL_FS, pal_pal_fs_rmdir_RemovesDirectoryWithMultipleSubDirectories)
    {
        char* working_dir = nullptr;
        EXPECT_TRUE(pal_process_get_cwd(&working_dir));

        const auto parent_dir = testutils::mkdir_random(working_dir);
        const auto parent_dir_filename = testutils::mkfile_random(parent_dir, testutils::build_random_filename().c_str());

        const auto sub_dir_name = "subdirectory";
        const auto sub_dir1 = testutils::mkdir(parent_dir, sub_dir_name);
        const auto sub_dir1_filename = testutils::mkfile_random(sub_dir1, testutils::build_random_filename().c_str());

        const auto sub_dir2 = testutils::mkdir(sub_dir1, sub_dir_name);
        const auto sub_dir2_filename = testutils::mkfile_random(sub_dir2, testutils::build_random_filename().c_str());

        EXPECT_TRUE(pal_fs_directory_exists(parent_dir.c_str()));
        EXPECT_TRUE(pal_fs_file_exists(parent_dir_filename.c_str()));
        EXPECT_TRUE(pal_fs_file_exists(sub_dir1_filename.c_str()));
        EXPECT_TRUE(pal_fs_file_exists(sub_dir2_filename.c_str()));
        EXPECT_TRUE(pal_fs_rmdir(parent_dir.c_str(), TRUE));
        EXPECT_FALSE(pal_fs_directory_exists(parent_dir.c_str()));
    }

    TEST(PAL_FS, pal_fs_rmfile_DoesNotSegFault)
    {
        EXPECT_FALSE(pal_fs_rmfile(nullptr));
    }

    TEST(PAL_FS, pal_fs_rmfile_ThatDoesNotExist)
    {
        const auto random_filename = testutils::build_random_filename();
        EXPECT_FALSE(pal_fs_rmfile(random_filename.c_str()));
    }

    TEST(PAL_FS, pal_fs_rmfile)
    {
        const auto random_filename = testutils::build_random_filename();
        const auto working_dir = testutils::get_process_cwd();
        const auto dst_filename = testutils::mkfile_random(working_dir.c_str(), random_filename.c_str());
        EXPECT_TRUE(pal_fs_file_exists(dst_filename.c_str()));
        EXPECT_TRUE(pal_fs_rmfile(dst_filename.c_str()));
        EXPECT_FALSE(pal_fs_file_exists(dst_filename.c_str()));
    }

    TEST(PAL_FS, pal_fs_fopen_DoesNotSegfault)
    {
        pal_file_handle_t** file_handle = nullptr;
        EXPECT_FALSE(pal_fs_fopen(nullptr, nullptr, file_handle));
        EXPECT_EQ(file_handle, nullptr);
    }

    TEST(PAL_FS, pal_fs_fopen_OpenAndClosesAFile)
    {
        const auto random_filename = testutils::build_random_filename(".txt");
        const auto working_dir = testutils::get_process_cwd();
        const auto dst_filename = testutils::mkfile_random(working_dir.c_str(), random_filename.c_str());

        pal_file_handle_t* file_handle = nullptr;
        EXPECT_TRUE(pal_fs_fopen(dst_filename.c_str(), "wb", &file_handle));
        EXPECT_NE(file_handle, nullptr);
        EXPECT_TRUE(pal_fs_fclose(file_handle));
        EXPECT_EQ(file_handle, nullptr);
    }

    TEST(PAL_FS, pal_fs_fwrite_DoesNotSegfault)
    {
        EXPECT_FALSE(pal_fs_fwrite(nullptr, nullptr, 0));
    }

    // - String

    TEST(PAL_STRING, pal_str_endswith)
    {
        EXPECT_TRUE(pal_str_endswith("test.nupkg", ".nupkg"));
        EXPECT_FALSE(pal_str_endswith("test.nupkg", ".nupk"));
        EXPECT_FALSE(pal_str_endswith(nullptr, nullptr));
        EXPECT_FALSE(pal_str_endswith(nullptr, ".nupkg"));
        EXPECT_FALSE(pal_str_endswith(".nupkg", nullptr));
    }

    TEST(PAL_STRING, pal_str_startswith)
    {
        EXPECT_TRUE(pal_str_startswith("test.nupkg", "test"));
        EXPECT_FALSE(pal_str_startswith("test.nupkg", "yolo"));
        EXPECT_FALSE(pal_str_startswith(nullptr, ".nupkg"));
        EXPECT_FALSE(pal_str_startswith(".nupkg", nullptr));
        EXPECT_FALSE(pal_str_startswith(nullptr, nullptr));
    }

    TEST(PAL_STRING, pal_str_iequals)
    {
        EXPECT_TRUE(pal_str_iequals("test.nupkg", "TEST.NUPKG"));
        EXPECT_TRUE(pal_str_iequals("test.NUPKG", "TEST.nupkg"));
        EXPECT_FALSE(pal_str_iequals("test.nupkg", "TEST.nupk"));
        EXPECT_FALSE(pal_str_iequals(nullptr, ".nupkg"));
        EXPECT_FALSE(pal_str_iequals(".nupkg", nullptr));
        EXPECT_TRUE(pal_str_iequals(nullptr, nullptr));
    }

    TEST(PAL_STRING, pal_str_is_null_or_whitespace)
    {
        EXPECT_TRUE(pal_str_is_null_or_whitespace(nullptr));
        EXPECT_TRUE(pal_str_is_null_or_whitespace(""));
        EXPECT_TRUE(pal_str_is_null_or_whitespace("          "));
        EXPECT_FALSE(pal_str_is_null_or_whitespace("          s"));
    }
}
