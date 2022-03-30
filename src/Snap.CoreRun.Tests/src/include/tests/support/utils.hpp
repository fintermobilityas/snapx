#pragma once

#include "pal/pal.hpp"
#include "nanoid/nanoid.h"
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
                    const auto bytes = std::make_unique<char*>(nullptr);
                    size_t bytes_len = 0;
                    if (!pal_fs_read_file(src_filename.c_str(), bytes.get(), &bytes_len))
                    {
                        return false;
                    }

                    if (!pal_fs_write(dest_filename.c_str(), *bytes, bytes_len))
                    {
                        return false;
                    }

                    return pal_fs_chmod(dest_filename.c_str(), 0777) == TRUE;
                }

                static std::string path_combine(const std::string &path1, const std::string &path2)
                {
                    const auto path_combined = std::make_unique<char*>(nullptr);
                    if (!pal_path_combine(path1.c_str(), path2.c_str(), path_combined.get()))
                    {
                        return std::string();
                    }
                    return std::string(*path_combined);
                }

                static std::string get_process_cwd()
                {
                    const auto working_dir = std::make_unique<char*>(nullptr);
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
                    const auto exe_filename = std::make_unique<char*>(new char);
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

                    const auto random_dir = std::make_unique<char*>(nullptr);
                    if(!pal_path_combine(working_dir.c_str(), nanoid::generate().c_str(), random_dir.get()))
                    {
                        return std::string();
                    }

                    if (!pal_fs_mkdir(*random_dir, mode))
                    {
                        return std::string();
                    }

                    return std::string(*random_dir);
                }

                static std::string mkdir(const std::string& working_dir, const char* directory_name, const pal_mode_t mode = 0777)
                {
                    if (directory_name == nullptr)
                    {
                        return std::string();
                    }

                    const auto dst_directory = std::make_unique<char*>(nullptr);
                    if(!pal_path_combine(working_dir.c_str(), nanoid::generate().c_str(), dst_directory.get()))
                    {
                        return std::string();
                    }

                    if (!pal_fs_mkdir(*dst_directory, mode))
                    {
                        return std::string();
                    }

                    return std::string(*dst_directory);
                }

                static std::string mkfile(const std::string& dst_directory, const char* filename)
                {
                    if (!pal_fs_directory_exists(dst_directory.c_str())
                        || filename == nullptr)
                    {
                        return std::string();
                    }

                    const auto dst_filename = std::make_unique<char*>(nullptr);
                    if(!pal_path_combine(dst_directory.c_str(), filename, dst_filename.get()))
                    {
                        return std::string();
                    }

                    const auto* const text = "Hello World";
                    if (!pal_fs_write(*dst_filename, text, strlen(text)))
                    {
                        return std::string();
                    }

                    return std::string(*dst_filename);
                }

                static std::string build_random_str()
                {
                    return nanoid::generate();
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
