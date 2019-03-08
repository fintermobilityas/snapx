#include "gtest/gtest.h"
#include "pal/pal.hpp"
#include "tests/support/utils.hpp"
#include <vector>

using testutils = corerun::support::util::test_utils;

namespace
{

    struct path_combine_test_case
    {
    public:
        const char *path1;
        const char *path2;
        const char *value;

        path_combine_test_case() = delete;

        path_combine_test_case(const char* path1, const char* path2, const char* value) :
            path1(path1), path2(path2), value(value)
        {

        }
    };

    struct path_normalize_test_case
    {
    public:
        const char *input;
        const char *expected_value;

        path_normalize_test_case() = delete;

        path_normalize_test_case(const char* input, const char* expected_value) :
                input(input), expected_value(expected_value)
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

    // realpath -m --canonicalize-missing -P <expression>
    std::vector<path_normalize_test_case> path_normalize_test_cases = {
        path_normalize_test_case("", nullptr),
        path_normalize_test_case(nullptr, nullptr),
        path_normalize_test_case("/", "/"),
        path_normalize_test_case("//", "/"),
        path_normalize_test_case("///", "/"),
        path_normalize_test_case("////", "/"),
        path_normalize_test_case("/////", "/"),
        path_normalize_test_case("/abc", "/abc"),
        path_normalize_test_case("//abc", "/abc"),
        path_normalize_test_case("///abc", "/abc"),
        path_normalize_test_case("abc", "abc"),
        path_normalize_test_case("abc/", "abc"),
        path_normalize_test_case("abc//", "abc"),
        path_normalize_test_case("abc/123", "abc/123"),
        path_normalize_test_case("abc//123", "abc/123"),
        path_normalize_test_case("abc///123", "abc/123"),
        path_normalize_test_case("abc/./123", "abc/123"),
        path_normalize_test_case("abc/x/../123", "abc/123"),
        path_normalize_test_case("..", ".."),
        path_normalize_test_case("/..", "/"),
        path_normalize_test_case("../123", "../123"),
        path_normalize_test_case("/../123", "/123"),
        path_normalize_test_case("//../123", "/123"),
        path_normalize_test_case("./../123", "123"),
        path_normalize_test_case("./", "."),
        path_normalize_test_case(".//", "."),
        path_normalize_test_case(".///", "."),
        path_normalize_test_case("./abc", "./abc"),
        path_normalize_test_case("././abc", "./abc"),
        path_normalize_test_case("./../abc", "abc"),
        path_normalize_test_case("abc/.", "abc"),
        path_normalize_test_case("abc/./.", "abc"),
        path_normalize_test_case("/abc/.", "/abc"),
        path_normalize_test_case("/abc/./.", "/abc"),
        path_normalize_test_case("/./abc/.", "/abc"),
        path_normalize_test_case("/abc/././123", "/abc/123"),
        path_normalize_test_case("abc/../123", "123"),
        path_normalize_test_case("/abc/../123", "/123"),
        path_normalize_test_case("abc/./../123", "123"),
        path_normalize_test_case("/abc/./../123", "/123"),
        path_normalize_test_case("abc/def/../123", "abc/123"),
        path_normalize_test_case("/abc/def/../123", "/abc/123"),
        path_normalize_test_case("abc/def/../../123", "123"),
        path_normalize_test_case("/abc/def/../../123", "/123"),
        path_normalize_test_case("/abc/..", "/"),
        path_normalize_test_case("abc/..", nullptr),
        path_normalize_test_case("abc/123/..", "abc"),
        path_normalize_test_case("/abc/123/..", "/abc"),
        path_normalize_test_case("abc/123/../..", nullptr),
        path_normalize_test_case("/abc/123/../..", "/"),
        path_normalize_test_case("abc/123/../../.", "."),
        path_normalize_test_case("/abc/123/../../.", "/"),
        path_normalize_test_case("abc/123/.././..", nullptr),
        path_normalize_test_case("/abc/123/.././..", "/"),
        path_normalize_test_case("abc////..////z////", "/z"),
        path_normalize_test_case("/////abc////..////z////", "/z"),
        path_normalize_test_case("d/./e/.././o/f/g/./h/../../.././n/././e/./i/..", "d/o/n/e")
    };

    TEST(PAL_GENERIC_UNIX, pal_is_linux)
    {
        ASSERT_TRUE(pal_is_linux()); 
    }

    TEST(PAL_GENERIC_UNIX, pal_is_windows)
    {
        ASSERT_FALSE(pal_is_windows());
    }

    TEST(PAL_GENERIC_UNIX, pal_is_windows_8_or_greater)
    {
        ASSERT_FALSE(pal_is_windows_8_or_greater());
    }

    TEST(PAL_GENERIC_UNIX, pal_is_windows_7_or_greater)
    {
        ASSERT_FALSE(pal_is_windows_7_or_greater());
    }

    TEST(PAL_GENERIC_UNIX, pal_process_exec)
    {
        auto exit_code = 1; 
        const auto working_dir = testutils::get_process_cwd();
        ASSERT_TRUE(pal_process_exec("ls", working_dir.c_str(), -1, nullptr, &exit_code));
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

    TEST(Disable_PAL_FS_UNIX, pal_fs_get_cwd_ReturnsCurrentWorkingDirectoryForThisProcess)
    {
        char* working_dir = nullptr;
        ASSERT_TRUE(pal_fs_get_cwd(&working_dir));
        ASSERT_TRUE(pal_fs_directory_exists(working_dir));
    }

    TEST(PAL_PATH_UNIX, pal_path_combine)
    {
        ASSERT_GT(path_combine_test_cases.size(), 0u);

        for (const auto &test_case : path_combine_test_cases)
        {
            auto path_combined = std::make_unique<char*>(nullptr);
            const auto ASSERT_success = test_case.value == nullptr ? FALSE : TRUE;
            ASSERT_EQ(pal_path_combine(test_case.path1, test_case.path2, path_combined.get()), ASSERT_success);
            ASSERT_STREQ(*path_combined, test_case.value);
        }
    }

    TEST(PAL_PATH_UNIX, pal_path_normalize)
    {
        ASSERT_GT(path_normalize_test_cases.size(), 0u);

        auto test_case_index = 0;
        for (const auto &test_case : path_normalize_test_cases)
        {
            auto path_normalized = std::make_unique<char*>(nullptr);
            const auto ASSERT_success = test_case.expected_value == nullptr ? FALSE : TRUE;

            EXPECT_EQ(pal_path_normalize(test_case.input, path_normalized.get()), ASSERT_success)
                << " @Input:" << test_case.input
                << " @Expected:" << test_case.expected_value
                << " @Normalized:" << *path_normalized
                << " @Test case index:" << std::to_string(test_case_index);

            EXPECT_STREQ(*path_normalized, test_case.expected_value)
                     << " @Input:" << test_case.input
                     << " @Expected:" << test_case.expected_value
                     << " @Normalized:" << *path_normalized
                     << " @Test case index:" << std::to_string(test_case_index);

            std::cout << "Completed: " << test_case_index;
            test_case_index++;
        }
    }

    /*TEST(PAL_PATH_UNIX, pal_path_normalize_debug)
    {
        const auto test_case = path_normalize_test_cases[24];

        auto path_normalized = std::make_unique<char*>(nullptr);
        const auto ASSERT_success = test_case.expected_value == nullptr ? FALSE : TRUE;

        ASSERT_EQ(pal_path_normalize(test_case.input, path_normalized.get()), ASSERT_success)
                                    << " @Input:" << test_case.input
                                    << " @Expected:" << test_case.expected_value
                                    << " @Normalized:" << *path_normalized;

        ASSERT_STREQ(*path_normalized, test_case.expected_value)
                                    << " @Input:" << test_case.input
                                    << " @Expected:" << test_case.expected_value
                                    << " @Normalized:" << *path_normalized;
    }*/

}
