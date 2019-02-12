#pragma once

#include <string>
#include <vector>
#include "miniz.h"

namespace snap
{
    class netcoreapp_runtime_dependency
    {
    public:
        std::string filename;
    };

    class extractor
    {
    public:
        static bool extract(const std::string install_dir, const size_t nupkg_size, uint8_t* nupkg_start_ptr, uint8_t* nupkg_end_ptr);
        static bool is_valid_payload(const size_t nupkg_size, uint8_t* nupkg_start_ptr, uint8_t* nupkg_end_ptr);
    private:
        static bool write_nupkg_to_disk(const std::string install_dir, const size_t nupkg_size, uint8_t* nupkg_start_ptr, uint8_t* nupkg_end_ptr);
        static std::vector<netcoreapp_runtime_dependency> build_extraction_list(mz_zip_archive& zip_archive, const size_t file_count);
    };
}
