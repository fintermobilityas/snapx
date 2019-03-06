#pragma once

#include "pal/pal.hpp"
#include "crossguid/Guid.hpp"
#include <string>

namespace corerun
{
    namespace support
    {
        namespace util
        {
            class test_utils
            {
            public:

                static bool is_windows10_or_greater()
                {
                    return pal_is_windows_10_or_greater();
                }

                static std::string get_process_cwd()
                {
                    char* working_dir = nullptr;
                    if (!pal_process_get_cwd(&working_dir))
                    {
                        return std::string();
                    }

                    std::string working_dir_str(working_dir);

#if defined(PAL_PLATFORM_WINDOWS)
                    // https://docs.microsoft.com/en-us/windows/desktop/fileio/naming-a-file#maximum-path-length-limitation
                    working_dir_str = R"(\\?\)" + working_dir_str;
#endif

                    delete working_dir;

                    return working_dir_str;
                }


                static std::string mkdir_random(const std::string& working_dir, const uint32_t mode = 0777u)
                {
                    if (mode <= 0)
                    {
                        return std::string();
                    }

                    char* random_dir = nullptr;
                    pal_fs_path_combine(working_dir.c_str(), xg::newGuid().str().c_str(), &random_dir);
                    if (!pal_fs_mkdir(random_dir, mode))
                    {
                        return std::string();
                    }

                    auto random_dir_str = std::string(random_dir);
                    delete random_dir;

                    return random_dir_str;
                }

                static std::string mkdir(const std::string& working_dir, const char* directory_name, const uint32_t mode = 0777u)
                {
                    if (directory_name == nullptr)
                    {
                        return std::string();
                    }

                    char* dst_directory = nullptr;
                    pal_fs_path_combine(working_dir.c_str(), directory_name, &dst_directory);
                    if (!pal_fs_mkdir(dst_directory, mode))
                    {
                        return std::string();
                    }

                    const auto dst_directory_str = std::string(dst_directory);
                    delete dst_directory;

                    return dst_directory_str;
                }

                static std::string mkfile_random(const std::string& working_dir, const char* filename)
                {
                    if (!pal_fs_directory_exists(working_dir.c_str())
                        || filename == nullptr)
                    {
                        return std::string();
                    }

                    char* dst_filename = nullptr;
                    if (!pal_fs_path_combine(working_dir.c_str(), filename, &dst_filename))
                    {
                        return std::string();
                    }

                    const auto text = "Hello World";
                    if (!pal_fs_write(dst_filename, "wb", text, strlen(text)))
                    {
                        delete dst_filename;
                        return std::string();
                    }

                    auto dst_filename_str = std::string(dst_filename);
                    delete dst_filename;

                    return dst_filename_str;
                }

                static std::string build_random_str()
                {
                    return xg::newGuid().str();
                }

                static std::string build_random_filename(const std::string& ext = ".txt")
                {
                    return build_random_str() + ext;
                }

                static std::string build_random_dirname()
                {
                    return build_random_str();
                }
            };
        }
    }
}
