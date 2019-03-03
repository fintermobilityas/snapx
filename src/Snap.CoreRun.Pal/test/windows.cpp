#include "gtest/gtest.h"
#include "pal/pal.hpp"

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

        int exit_code = -1;
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
        auto process_working_dir = get_process_cwd();
#if defined(PAL_PLATFORM_WINDOWS) && !defined(PAL_PLATFORM_MINGW)
        EXPECT_STRNE(process_working_dir.c_str(), nullptr);
        EXPECT_GT(SetCurrentDirectory(process_working_dir.c_str()), 0);
#endif
        char* working_dir = nullptr;
        EXPECT_TRUE(pal_fs_get_cwd(&working_dir));
        EXPECT_TRUE(pal_fs_directory_exists(working_dir));
    }

    TEST(PAL_FS_WINDOWS, pal_process_get_name_ReturnsThisProcessExeName)
    {
        char* exe_name = nullptr;
        EXPECT_TRUE(pal_process_get_name(&exe_name));
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
