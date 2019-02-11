#pragma once

#include <string>
#include <vector>

namespace snap {
    class extractor {
    public:
        static bool extract(const std::string install_dir, const size_t nupkg_size, uint8_t* nupkg_start_ptr, uint8_t* nupkg_end_ptr);
        static bool is_valid_payload(const size_t nupkg_size, uint8_t* nupkg_start_ptr, uint8_t* nupkg_end_ptr);
    private:
        static bool write_nupkg_to_disk(const std::string install_dir, const size_t nupkg_size, uint8_t* nupkg_start_ptr, uint8_t* nupkg_end_ptr);
    };
}
