#include "gtest/gtest.h"
#include "pal/pal.hpp"
#include "tests/support/utils.hpp"

using testutils = corerun::support::util::test_utils;

namespace
{
    TEST(PAL_GENERIC, pal_is_windows)
    {
        EXPECT_TRUE(pal_is_windows());
        EXPECT_FALSE(pal_is_linux());
    }

    TEST(PAL_GENERIC, pal_process_exec)
    {
        char* working_dir = nullptr;
        EXPECT_TRUE(pal_process_get_cwd(&working_dir));

        pal_exit_code_t exit_code = 1;
        EXPECT_TRUE(pal_process_exec("whoami", working_dir, -1, nullptr, &exit_code));
        EXPECT_EQ(exit_code, 0);
    }

    TEST(PAL_FS_WINDOWS, pal_fs_file_exists_ReturnsFalseIfDirectory)
    {
        char* working_dir = nullptr;
        EXPECT_TRUE(pal_fs_get_cwd(&working_dir));
        EXPECT_NE(working_dir, nullptr);
        ASSERT_FALSE(pal_fs_file_exists(working_dir));
    }

    TEST(PAL_FS_WINDOWS, pal_fs_file_exists_ReturnsTrueWhenAbsolutePath)
    {
        char* exe_abs_path = nullptr;
        EXPECT_TRUE(pal_process_get_real_path(&exe_abs_path));
        EXPECT_NE(exe_abs_path, nullptr);
        EXPECT_TRUE(pal_fs_file_exists(exe_abs_path));
    }

    TEST(PAL_FS_WINDOWS, pal_fs_get_cwd_ReturnsCurrentWorkingDirectoryForThisProcess)
    {
        const auto process_working_dir = testutils::get_process_cwd();
#if defined(PAL_PLATFORM_WINDOWS) && !defined(PAL_PLATFORM_MINGW)
        pal_utf16_string process_working_dir_utf16_str(process_working_dir);
        EXPECT_STRNE(process_working_dir_utf16_str.data(), nullptr);
        EXPECT_GT(SetCurrentDirectory(process_working_dir_utf16_str.data()), 0);
#endif
        char* working_dir = nullptr;
        EXPECT_TRUE(pal_fs_get_cwd(&working_dir));
        EXPECT_TRUE(pal_fs_directory_exists(working_dir));
        delete working_dir;
    }

    TEST(PAL_FS_WINDOWS, pal_fs_mkdir_LongPath)
    {
        if(!testutils::is_windows10_or_greater())
        {
            GTEST_SKIP();
            return;
        }

        const auto working_dir = testutils::get_process_cwd();

        const auto long_path_base_dir = working_dir + PAL_DIRECTORY_SEPARATOR_STR + testutils::build_random_str();
        ASSERT_TRUE(pal_fs_mkdir(long_path_base_dir.c_str(), 777));

        const auto expected_directories_created = 10;
        auto directories_remaining = expected_directories_created;
        auto long_path_current_dir = long_path_base_dir;
        while(directories_remaining > 0)
        {
            long_path_current_dir += PAL_DIRECTORY_SEPARATOR_STR + testutils::build_random_str();
            if(!pal_fs_mkdir(long_path_current_dir.c_str(), 777))
            {
                break;
            }

            --directories_remaining;
        }

        ASSERT_EQ(directories_remaining, 0) 
            << "Should have created " << expected_directories_created 
            << " directories but only " << (expected_directories_created - directories_remaining) 
            << " was created.";

        ASSERT_TRUE(pal_fs_directory_exists(long_path_current_dir.c_str()));
    }

    TEST(PAL_FS_WINDOWS, pal_process_get_name_ReturnsThisProcessExeName)
    {
        char* exe_name = nullptr;
        EXPECT_TRUE(pal_process_get_name(&exe_name));
        ASSERT_STREQ(exe_name, "corerun_tests.exe");
    }

    TEST(PAL_ENV_WINDOWS, pal_env_get_Reads_PATH_Variable)
    {
        char* environment_variable = nullptr;
        EXPECT_TRUE(pal_env_get("PATH", &environment_variable));
        EXPECT_NE(environment_variable, nullptr);
    }

    TEST(PAL_STR_WINDOWS, pal_str_widen_DoesNotSegfault)
    {
        EXPECT_EQ(pal_str_widen(nullptr), nullptr);
    }

    TEST(PAL_STR_WINDOWS, pal_str_narrow_DoesNotSegfault)
    {
        EXPECT_EQ(pal_str_widen(nullptr), nullptr);
    }

}
