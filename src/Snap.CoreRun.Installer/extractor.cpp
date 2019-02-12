#include "pal.hpp"
#include "extractor.hpp"
#include "easylogging++.h"

#include <regex>

const int posix_io_mode = 0777;
#if PLATFORM_WINDOWS
const char* posix_fopen_mode = "wb";
#else
const char* posix_fopen_mode = "wbx";
#endif

bool snap::extractor::extract(const std::string install_dir, const size_t nupkg_size, uint8_t* nupkg_start_ptr, uint8_t* nupkg_end_ptr)
{
    if (install_dir.empty()
        || nupkg_size <= 0
        || nupkg_start_ptr == nullptr
        || nupkg_end_ptr == nullptr)
    {
        return false;
    }

    if (!pal_fs_directory_exists(install_dir.c_str())
        && !pal_fs_mkdir(install_dir.c_str(), posix_io_mode))
    {
        LOG(ERROR) << "Failed to create install dir: " << install_dir.c_str();
        return false;
    }

    if (!write_nupkg_to_disk(install_dir.c_str(), nupkg_size, nupkg_start_ptr, nupkg_end_ptr))
    {
        LOG(ERROR) << "Failed to write nupkg to install dir: " << install_dir.c_str();
        return false;
    }

    mz_zip_archive zip_archive = { 0 };
    const auto status = mz_zip_reader_init_mem(&zip_archive, nupkg_start_ptr, nupkg_size, 0);
    if (!status)
    {
        LOG(ERROR) << "mz_zip_reader_init_mem failed";
        return false;
    }

    const auto file_count = mz_zip_reader_get_num_files(&zip_archive);
    if (file_count <= 0)
    {
        LOG(ERROR) << "Zip archive does not contain any files";
        mz_zip_reader_end(&zip_archive);
        return false;
    }

    mz_zip_archive_file_stat file_stat;
    if (!mz_zip_reader_file_stat(&zip_archive, 0, &file_stat))
    {
        LOG(ERROR) << "Failed to stat first file in zip archive";
        mz_zip_reader_end(&zip_archive);
        return false;
    }

    if (!pal_fs_directory_exists(install_dir.c_str())
        && !pal_fs_mkdir(install_dir.c_str(), posix_io_mode)) {
        LOG(ERROR) << "Failed to create install directory: " << install_dir;
        return false;
    }

    std::string archive_base_dir("lib/Any/");
    std::string snap_base_dir("lib/Any/a97d941bdd70471289d7330903d8b5b3"); // Guid can be found in Snap .NET project

    std::vector<std::string> snap_runtime_files;
    snap_runtime_files.emplace_back("Snap.dll");
    snap_runtime_files.emplace_back("Snap.App.dll");
    snap_runtime_files.emplace_back("Snap.Installer.exe");

    auto net_runtime_files = build_extraction_list(zip_archive, file_count);

    for (auto i = 0u; i < file_count; i++)
    {
        if (!mz_zip_reader_file_stat(&zip_archive, i, &file_stat))
        {
            LOG(ERROR) << "Failed to stat file at index: " << i;
            mz_zip_reader_end(&zip_archive);
            return false;
        }

        std::string filename_relative_path = std::string(file_stat.m_filename).substr(archive_base_dir.size());
        const auto is_snap_base_dir = pal_str_endswith(file_stat.m_filename, snap_base_dir.c_str());
        auto extract_current_file = false;
        if (mz_zip_reader_is_file_a_directory(&zip_archive, i))
        {
            if (is_snap_base_dir)
            {
                const auto snap_base_dir_last_slash = filename_relative_path.find_last_of("/");
                filename_relative_path = filename_relative_path.substr(snap_base_dir_last_slash + 1);

                for (auto runtime_file : snap_runtime_files)
                {
                    if (pal_str_iequals(runtime_file.c_str(), filename_relative_path.c_str()))
                    {
                        extract_current_file = true;
                        break;
                    }
                }
            }
            else {
                continue;
            }
        }

        if (!extract_current_file)
        {
            for (auto net_runtime_file : net_runtime_files)
            {
                if (pal_str_iequals(net_runtime_file.c_str(), filename_relative_path.c_str()))
                {
                    extract_current_file = true;
                    break;
                }
            }

            if (!extract_current_file && !pal_str_endswith(filename_relative_path.c_str(), ".json"))
            {
                continue;
            }
        }

        char* filename_absolute_path = nullptr;
        if (!pal_fs_path_combine(install_dir.c_str(), filename_relative_path.c_str(), &filename_absolute_path))
        {
            LOG(ERROR) << "Failed to combine path: " << file_stat.m_filename << ". Index: " << i;
            mz_zip_reader_end(&zip_archive);
            return false;
        }

        char* directory_absolute_path = nullptr;
        if (!pal_fs_get_directory_name_absolute_path(filename_absolute_path, &directory_absolute_path))
        {
            LOG(ERROR) << "Failed to build directory absolute path: " << filename_absolute_path << ". Index: " << i;
            mz_zip_reader_end(&zip_archive);
            return false;
        }

        bool is_directory = pal_str_iequals(directory_absolute_path, filename_absolute_path);

        if (is_directory
            && !pal_fs_directory_exists(directory_absolute_path)
            && !pal_fs_mkdir(directory_absolute_path, posix_io_mode))
        {
            LOG(ERROR) << "Failed to create directory: " << directory_absolute_path << ". Index: " << i;
            mz_zip_reader_end(&zip_archive);
            return false;
        }

        if (is_directory)
        {
            continue;
        }

        size_t uncompressed_size = file_stat.m_uncomp_size;
        void* file_ptr = mz_zip_reader_extract_file_to_heap(&zip_archive, file_stat.m_filename, &uncompressed_size, 0);
        if (!file_ptr)
        {
            LOG(ERROR) << "Failed to extract file to memory: " << filename_absolute_path << ". Index: " << i;
            mz_zip_reader_end(&zip_archive);
            return false;
        }

        if (!pal_fs_write(filename_absolute_path, posix_fopen_mode, file_ptr, uncompressed_size))
        {
            LOG(ERROR) << "Failed to write uncompressed file to disk: " << filename_absolute_path << ". Index: " << i;
            mz_zip_reader_end(&zip_archive);
            return false;
        }

#if PLATFORM_WINDOWS
        pal_utf16_string filename_absolute_path_utf16_string(filename_absolute_path);
        if (0 == MoveFileEx(filename_absolute_path_utf16_string.data(), nullptr, MOVEFILE_DELAY_UNTIL_REBOOT))
        {
            LOG(WARNING) << "Failed to delay deletion of file until reboot: " << filename_absolute_path_utf16_string << ". Index " << i;
        }
#endif

    }

    mz_zip_reader_end(&zip_archive);

    return true;
}

std::vector<std::string> snap::extractor::build_extraction_list(mz_zip_archive &zip_archive, const size_t file_count)
{
    std::vector<std::string> dependencies;
    mz_zip_archive_file_stat file_stat;
    char* deps_json_file_ptr = nullptr;
    size_t deps_json_file_len = 0;

    for (auto i = 0u; i < file_count; i++) {
        if (!mz_zip_reader_file_stat(&zip_archive, i, &file_stat)) {
            break;
        }

        if (!pal_str_endswith(file_stat.m_filename, ".deps.json"))
        {
            continue;
        }

        deps_json_file_len = file_stat.m_uncomp_size;
        deps_json_file_ptr = reinterpret_cast<char*>(mz_zip_reader_extract_file_to_heap(&zip_archive,
            file_stat.m_filename, &deps_json_file_len, 0));
        if (deps_json_file_ptr)
        {
            break;
        }
    }

    if (deps_json_file_ptr == nullptr)
    {
        return dependencies;
    }

    std::string json(deps_json_file_ptr, deps_json_file_ptr + deps_json_file_len);

    const std::regex json_regex(R"~("(.*\.(dll|json))")~", std::regex::icase);

    std::sregex_iterator json_regex_iter(json.begin(), json.end(), json_regex);
    std::sregex_iterator json_regex_iter_end;

    while (json_regex_iter != json_regex_iter_end)
    {
        if (json_regex_iter->size() <= 1)
        {
            continue;
        }

        const auto filename = (*json_regex_iter)[1].str();
        const auto filename_last_slash = filename.find_last_of("/");

        if (filename_last_slash == std::string::npos)
        {
            dependencies.push_back(filename);
        }
        else {
            dependencies.push_back(filename.substr(filename_last_slash + 1));
        }

        ++json_regex_iter;
    }

    return dependencies;
}

bool snap::extractor::is_valid_payload(const size_t nupkg_size, uint8_t *nupkg_start_ptr, uint8_t *nupkg_end_ptr)
{
    if (nupkg_size <= 0
        || nupkg_start_ptr == nullptr
        || nupkg_end_ptr == nullptr)
    {
        return false;
    }

    auto payload_size = 0;

    for (uint8_t *byte = nupkg_start_ptr; byte < nupkg_end_ptr; ++byte)
    {
        payload_size += 1;
    }

    return payload_size == nupkg_size;
}

bool snap::extractor::write_nupkg_to_disk(const std::string install_dir, const size_t nupkg_size,
    uint8_t * nupkg_start_ptr, uint8_t * nupkg_end_ptr)
{
    if (nupkg_size <= 0
        || nupkg_start_ptr == nullptr
        || nupkg_end_ptr == nullptr)
    {
        return false;
    }

    std::string nupkg_relative_filename("payload.nupkg");

    char* nupkg_filename_absolute_path = nullptr;
    if (!pal_fs_path_combine(install_dir.c_str(), nupkg_relative_filename.c_str(), &nupkg_filename_absolute_path))
    {
        return false;
    }

    if (!pal_fs_write(nupkg_filename_absolute_path, posix_fopen_mode, &nupkg_start_ptr[0], nupkg_size))
    {
        LOG(ERROR) << "Failed to write nupkg to disk: " << nupkg_filename_absolute_path;
        return FALSE;
    }

    return true;
}
