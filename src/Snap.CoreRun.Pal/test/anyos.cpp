#include "gtest/gtest.h"
#include "pal/pal.hpp"

namespace
{

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

        pal_wait_for_debugger();
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

    TEST(PAL_GENERIC, pal_usleep_DoesNotSegFault)
    {
        pal_usleep(0);
        pal_usleep(1);
    }

    TEST(PAL_ENV, pal_env_get_variable_DoesNotSegFault)
    {
        char** value = nullptr;
        EXPECT_FALSE(pal_env_get_variable(nullptr, value));
        EXPECT_EQ(value, nullptr);
    }

    TEST(PAL_ENV, pal_env_get_variable_bool_DoesNotSegFault)
    {
        EXPECT_FALSE(pal_env_get_variable_bool(nullptr));
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
        EXPECT_TRUE(pal_fs_get_cwd(&working_dir));
        EXPECT_NE(working_dir, nullptr);
        ASSERT_FALSE(pal_fs_file_exists(working_dir));
    }

    TEST(PAL_FS, pal_fs_file_exists_ReturnsTrueWhenAbsolutePath)
    {
        char* exe_abs_path = nullptr;
        EXPECT_TRUE(pal_fs_get_process_real_path(&exe_abs_path));
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
        char* working_dir = nullptr;
        EXPECT_TRUE(pal_fs_get_cwd(&working_dir));
        EXPECT_NE(working_dir, nullptr);

        char** directories_array = nullptr;
        size_t directories_len = 0u;
        EXPECT_TRUE(pal_fs_list_directories(working_dir, nullptr, nullptr, &directories_array, &directories_len));
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
        EXPECT_TRUE(pal_fs_get_cwd(&working_dir));
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

    TEST(PAL_FS, pal_fs_get_cwd_ReturnsCurrentWorkingDirectoryForThisProcess)
    {
        char* working_dir = nullptr;
        EXPECT_TRUE(pal_fs_get_cwd(&working_dir));
        EXPECT_TRUE(pal_fs_directory_exists(working_dir));
    }

    TEST(PAL_FS, pal_fs_get_process_real_path)
    {
        char* this_process_real_path = nullptr;
        EXPECT_TRUE(pal_fs_get_process_real_path(&this_process_real_path));
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
        EXPECT_TRUE(pal_fs_get_cwd(&working_dir));
        EXPECT_TRUE(pal_fs_directory_exists(working_dir));
    }

    TEST(PAL_FS, pal_fs_get_file_size_ReturnsAValueGreaterThanZero)
    {
        char* exe_abs_path = nullptr;
        EXPECT_TRUE(pal_fs_get_process_real_path(&exe_abs_path));
        EXPECT_NE(exe_abs_path, nullptr);

        size_t file_size = 0;
        EXPECT_TRUE(pal_fs_get_file_size(exe_abs_path, &file_size));
        EXPECT_GT(file_size, 0u);
    }

    TEST(PAL_FS, pal_fs_read_file_ReadsCurrentProcessBinaryData)
    {
        char* exe_abs_path = nullptr;
        EXPECT_TRUE(pal_fs_get_process_real_path(&exe_abs_path));
        EXPECT_NE(exe_abs_path, nullptr);

        size_t expected_file_size = 0;
        EXPECT_TRUE(pal_fs_get_file_size(exe_abs_path, &expected_file_size));
        EXPECT_GT(expected_file_size, 0u);

        char* bytes = nullptr;
        int bytes_len = 0;
        EXPECT_TRUE(pal_fs_read_file(exe_abs_path, "rb", &bytes, &bytes_len));
        EXPECT_NE(bytes, nullptr);
        EXPECT_GT(bytes_len, 0);

        for (auto i = 0; i < bytes_len; i++)
        {
            EXPECT_NE(&bytes[i], nullptr);
        }
    }

    TEST(PAL_FS, pal_fs_mkdir_DoesNotSegfault)
    {
        EXPECT_FALSE(pal_fs_mkdir(nullptr, 0));
    }

    TEST(PAL_FS, pal_fs_fopen_DoesNotSegfault)
    {
        pal_file_handle_t** file_handle = nullptr;
        EXPECT_FALSE(pal_fs_fopen(nullptr, nullptr, file_handle));
        EXPECT_EQ(file_handle, nullptr);
    }

    TEST(PAL_FS, pal_fs_fopen_OpenAndClosesAFile)
    {
        pal_file_handle_t* file_handle = nullptr;
        EXPECT_TRUE(pal_fs_fopen("test.txt", "wb", &file_handle));
        EXPECT_NE(file_handle, nullptr);
        EXPECT_TRUE(pal_fs_fclose(file_handle));
        EXPECT_EQ(file_handle, nullptr);
    }

    TEST(PAL_FS, pal_fs_fwrite_DoesNotSegfault)
    {
        EXPECT_FALSE(pal_fs_fwrite(nullptr, nullptr, 0));
    }

    TEST(PAL_FS, pal_fs_get_own_executable_name_ReturnsThisProcessExeName)
    {
        char* exe_name = nullptr;
        EXPECT_TRUE(pal_fs_get_own_executable_name(&exe_name));
        EXPECT_NE(exe_name, nullptr);
        EXPECT_TRUE(pal_str_startswith(exe_name, "Snap.Tests"));
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
