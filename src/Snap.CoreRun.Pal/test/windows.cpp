#include "gtest/gtest.h"
#include "pal/pal.hpp"

namespace
{
    TEST(PAL_GENERIC, pal_is_windows)
    {
        EXPECT_TRUE(pal_is_windows());
        EXPECT_FALSE(pal_is_linux());
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
        EXPECT_TRUE(pal_fs_get_process_real_path(&exe_abs_path));
        EXPECT_NE(exe_abs_path, nullptr);
        EXPECT_TRUE(pal_fs_file_exists(exe_abs_path));
    }    

    TEST(PAL_FS_WINDOWS, pal_fs_get_own_executable_name_ReturnsThisProcessExeName)
    {
        char* exe_name = nullptr;
        EXPECT_TRUE(pal_fs_get_own_executable_name(&exe_name));
        ASSERT_STREQ(exe_name, "Snap.Tests.exe");
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
