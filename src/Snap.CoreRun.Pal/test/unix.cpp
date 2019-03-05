#include "gtest/gtest.h"
#include "pal/pal.hpp"

#include <vector>

inline std::string get_process_cwd() {
    char* working_dir = nullptr;
    if(!pal_process_get_cwd(&working_dir))
    {
        return nullptr;
    }

    std::string working_dir_str(working_dir);
    delete working_dir;

    return working_dir_str;
}

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

    std::vector<path_combine_test_case> path_combine_test_cases = {
        path_combine_test_case("/a/b/c", "/c/d/e", "/c/d/e"),
        path_combine_test_case("/a/b/c", "d", "/a/b/c/d"),
        path_combine_test_case("/foo/bar", "./baz", "/foo/bar/baz"),
        path_combine_test_case("/foo/bar", "./baz/", "/foo/bar/baz"),
        path_combine_test_case("a", ".", "a"),
        path_combine_test_case("a.", ".", "a."),
        path_combine_test_case("a./b.", ".", "a./b."),
        path_combine_test_case("a/b", "..", "a"),
        path_combine_test_case("a", "..a", "a/..a"),
        path_combine_test_case("a", "../a", nullptr),
        path_combine_test_case("a", "c../a", "a/c../a"),
        path_combine_test_case("a/b", "../", "a"),
        path_combine_test_case("a/b", ".././c/d/../../.", "a"),
        path_combine_test_case("", "", nullptr),
        path_combine_test_case(" ", " ", nullptr),
        path_combine_test_case(nullptr, nullptr, nullptr)
    };

    TEST(PAL_GENERIC_UNIX, pal_is_linux)
    {
        ASSERT_FALSE(pal_is_windows());
        ASSERT_TRUE(pal_is_linux()); 
    }

    TEST(PAL_GENERIC_UNIX, pal_process_exec)
    {
        char* working_dir = nullptr;
        ASSERT_TRUE(pal_process_get_cwd(&working_dir));
        auto exit_code = 1;
        ASSERT_TRUE(pal_process_exec("ls", working_dir, -1, nullptr, &exit_code));
        ASSERT_EQ(exit_code, 0);
    }

    TEST(PAL_ENV_UNIX, pal_env_get_variable_Reads_PWD_Variable)
    {
        char *environment_variable = nullptr;
        ASSERT_TRUE(pal_env_get("PWD", &environment_variable));
        ASSERT_NE(environment_variable, nullptr);
    }

    TEST(PAL_FS_UNIX, pal_process_get_name_ReturnsThisProcessExeName)
    {
        char *exe_name = nullptr;
        ASSERT_TRUE(pal_process_get_name(&exe_name));
        ASSERT_NE(exe_name, nullptr);
        ASSERT_STREQ(exe_name, "corerun_tests");
    }

    TEST(PAL_FS_UNIX, pal_fs_path_combine)
    {
        ASSERT_GT(path_combine_test_cases.size(), 0u);

        for (const auto &test_case : path_combine_test_cases)
        {
            char* path_combined = nullptr;
            const auto ASSERT_success = test_case.combined == nullptr ? FALSE : TRUE;
            ASSERT_EQ(pal_fs_path_combine(test_case.path1, test_case.path2, &path_combined), ASSERT_success);
            ASSERT_STREQ(path_combined, test_case.combined);
        }
    }

    TEST(Disable_PAL_FS_UNIX, pal_fs_get_cwd_ReturnsCurrentWorkingDirectoryForThisProcess)
    {
        char* working_dir = nullptr;
        ASSERT_TRUE(pal_fs_get_cwd(&working_dir));
        ASSERT_TRUE(pal_fs_directory_exists(working_dir));
    }

}
