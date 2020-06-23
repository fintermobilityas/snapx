#include "gtest/gtest.h"
#include "pal/pal.hpp"
#include "tests/support/utils.hpp"

using testutils = corerun::support::util::test_utils;

namespace
{
    struct path_combine_test_case
    {
    public:
        const char *path1;
        const char *path2;
        const char *combined;
        path_combine_test_case() = delete;

        path_combine_test_case(const char* path1, const char* path2, const char* combined) :
            path1(path1), path2(path2), combined(combined)
        {

        }
    };

    // https://github.com/wine-mirror/wine/blob/6a04cf4a69205ddf6827fb2a4b97862fd1947c62/dlls/shlwapi/tests/path.c

    std::vector<path_combine_test_case> path_combine_test_cases = {
         /* normal paths */
        path_combine_test_case("C:\\",  "a",     "C:\\a"),
        path_combine_test_case("C:\\b", "..\\a", "C:\\a"),
        path_combine_test_case("C:",    "a",     "C:\\a"),
        path_combine_test_case("C:\\",  ".",     "C:\\"),
        path_combine_test_case("C:\\",  "..",    "C:\\"),
        path_combine_test_case("\\a",   "b",     "\\a\\b"),

        /* normal UNC paths */
        path_combine_test_case("\\\\192.168.1.1\\test", "a",  "\\\\192.168.1.1\\test\\a"),
        path_combine_test_case("\\\\192.168.1.1\\test", "..", "\\\\192.168.1.1"),

        /* NT paths */
        path_combine_test_case("\\\\?\\C:\\", "a",  "C:\\a"),
        path_combine_test_case("\\\\?\\C:\\", "..", "C:\\"),

        /* NT UNC path */
        path_combine_test_case("\\\\?\\UNC\\192.168.1.1\\test", "a",  "\\\\192.168.1.1\\test\\a"),
        path_combine_test_case("\\\\?\\UNC\\192.168.1.1\\test", "..", "\\\\192.168.1.1")
    };

    TEST(PAL_GENERIC_WINDOWS, pal_set_icon)
    {
        const auto working_dir = testutils::get_process_cwd();

        const auto src_filename = testutils::path_combine(
            working_dir, "corerun_demoapp.exe");
        const auto dst_filename = testutils::path_combine(
            working_dir, testutils::build_random_filename(".exe"));
        const auto icon_filename = testutils::path_combine(
            working_dir, "test.ico");

        ASSERT_TRUE(testutils::file_copy(src_filename, dst_filename));
        ASSERT_FALSE(pal_has_icon(dst_filename.c_str()));
        ASSERT_TRUE(pal_set_icon(dst_filename.c_str(), icon_filename.c_str()));
        ASSERT_TRUE(pal_has_icon(dst_filename.c_str()));
        ASSERT_TRUE(pal_fs_rmfile(dst_filename.c_str()));
    }

    TEST(PAL_GENERIC_WINDOWS, pal_is_windows)
    {
        EXPECT_TRUE(pal_is_windows());
        EXPECT_FALSE(pal_is_linux());
    }

    TEST(PAL_GENERIC_WINDOWS, pal_process_exec)
    {
        auto working_dir = std::make_unique<char*>(new char);
        EXPECT_TRUE(pal_process_get_cwd(working_dir.get()));

        pal_exit_code_t exit_code = 1;
        EXPECT_TRUE(pal_process_exec("whoami", *working_dir, -1, nullptr, &exit_code));
        EXPECT_EQ(exit_code, 0);
    }

    TEST(PAL_FS_WINDOWS, pal_fs_file_exists_ReturnsFalseIfDirectory)
    {
        auto working_dir = std::make_unique<char*>(new char);
        EXPECT_TRUE(pal_fs_get_cwd(working_dir.get()));
        ASSERT_FALSE(pal_fs_file_exists(*working_dir));
    }

    TEST(PAL_FS_WINDOWS, pal_fs_file_exists_ReturnsTrueWhenAbsolutePath)
    {
        auto exe_abs_path = std::make_unique<char*>(new char);
        EXPECT_TRUE(pal_process_get_real_path(exe_abs_path.get()));
        EXPECT_TRUE(pal_fs_file_exists(*exe_abs_path));
    }

    TEST(PAL_FS_WINDOWS, pal_fs_get_cwd_ReturnsCurrentWorkingDirectoryForThisProcess)
    {
        const auto process_working_dir = testutils::get_process_cwd();
#if defined(PAL_PLATFORM_WINDOWS)
        pal_utf16_string process_working_dir_utf16_str(process_working_dir);
        EXPECT_STRNE(process_working_dir_utf16_str.data(), nullptr);
        EXPECT_GT(SetCurrentDirectory(process_working_dir_utf16_str.data()), 0);
#endif
        char* working_dir = nullptr;
        EXPECT_TRUE(pal_fs_get_cwd(&working_dir));
        EXPECT_TRUE(pal_fs_directory_exists(working_dir));
        delete working_dir;
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

    TEST(PAL_PATH_WINDOWS, pal_path_normalize)
    {        
        const auto expected_str = std::string("C:/test/123");

        char* path_normalized = nullptr;
        ASSERT_TRUE(pal_path_normalize(expected_str.c_str(), &path_normalized));
        ASSERT_STREQ(path_normalized, expected_str.c_str());
    }

    TEST(PAL_PATH_WINDOWS, pal_path_combine)
    {
        ASSERT_GT(path_combine_test_cases.size(), 0u);

        for (const auto &test_case : path_combine_test_cases)
        {
            auto path_combined = std::make_unique<char*>(new char);
            EXPECT_TRUE(pal_path_combine(test_case.path1, test_case.path2, path_combined.get()));
            EXPECT_STREQ(*path_combined, test_case.combined);
        }
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
