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

                static bool file_copy(const std::string& src_filename, const std::string& dest_filename)
                {
                    auto bytes = std::make_unique<char*>(new char);
                    size_t bytes_len = 0;
                    if (!pal_fs_read_binary_file(src_filename.c_str(), bytes.get(), &bytes_len))
                    {
                        return false;
                    }

                    if (!pal_fs_write(dest_filename.c_str(), "wb", *bytes, bytes_len))
                    {
                        return false;
                    }

                    if (!pal_fs_chmod(dest_filename.c_str(), 0777))
                    {
                        return false;
                    }

                    return true;
                }

                static std::string path_combine(std::string path1, std::string path2)
                {
                    auto path_combined = std::make_unique<char*>(new char);
                    if (!pal_path_combine(path1.c_str(), path2.c_str(), path_combined.get()))
                    {
                        return std::string();
                    }
                    return std::string(*path_combined);
                }

                static std::string get_process_cwd()
                {
                    auto working_dir = std::make_unique<char*>(new char);
                    if (!pal_process_get_cwd(working_dir.get()))
                    {
                        return std::string();
                    }
                    return std::string(*working_dir);
                }

                static std::string get_directory_name(const std::string& full_path)
                {
                    const auto directory_name_start_pos = full_path.find_last_of(PAL_DIRECTORY_SEPARATOR_C);
                    if (directory_name_start_pos == std::string::npos)
                    {
                        return std::string();
                    }
                    return full_path.substr(directory_name_start_pos + 1);
                }

                static std::string get_process_real_path()
                {
                    auto exe_filename = std::make_unique<char*>(new char);
                    if (!pal_process_get_real_path(exe_filename.get()))
                    {
                        return std::string();
                    }
                    return std::string(*exe_filename);
                }

                static std::string mkdir_random(const std::string& working_dir, const pal_mode_t mode = 0777)
                {
                    if (mode <= 0)
                    {
                        return std::string();
                    }

                    char* random_dir = nullptr;
                    pal_path_combine(working_dir.c_str(), xg::newGuid().str().c_str(), &random_dir);
                    if (!pal_fs_mkdir(random_dir, mode))
                    {
                        return std::string();
                    }

                    auto random_dir_str = std::string(random_dir);
                    delete random_dir;

                    return random_dir_str;
                }

                static std::string mkdir(const std::string& working_dir, const char* directory_name, const pal_mode_t mode = 0777)
                {
                    if (directory_name == nullptr)
                    {
                        return std::string();
                    }

                    char* dst_directory = nullptr;
                    pal_path_combine(working_dir.c_str(), directory_name, &dst_directory);
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
                    if (!pal_path_combine(working_dir.c_str(), filename, &dst_filename))
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
