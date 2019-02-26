#include "gtest/gtest.h"
#include "pal/pal.hpp"

#include <vector>

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

    TEST(PAL_GENERIC, pal_is_linux)
    {
        EXPECT_FALSE(pal_is_windows());
        EXPECT_TRUE(pal_is_linux()); 
    }

    TEST(PAL_ENV_UNIX, pal_env_get_variable_Reads_PWD_Variable)
    {
        char *environment_variable = nullptr;
        EXPECT_TRUE(pal_env_get("PWD", &environment_variable));
        EXPECT_NE(environment_variable, nullptr);
    }

    TEST(PAL_FS_UNIX, pal_fs_get_own_executable_name_ReturnsThisProcessExeName)
    {
        char *exe_name = nullptr;
        EXPECT_TRUE(pal_fs_get_own_executable_name(&exe_name));
        EXPECT_NE(exe_name, nullptr);
        ASSERT_STREQ(exe_name, "Snap.Tests");
    }

    TEST(PAL_FS_UNIX, pal_fs_path_combine)
    {
        EXPECT_GT(path_combine_test_cases.size(), 0u);

        for (const auto &test_case : path_combine_test_cases)
        {
            char* path_combined = nullptr;
            const auto expect_success = test_case.combined == nullptr ? FALSE : TRUE;
            EXPECT_EQ(pal_fs_path_combine(test_case.path1, test_case.path2, &path_combined), expect_success);
            ASSERT_STREQ(path_combined, test_case.combined);
        }
    }

}
