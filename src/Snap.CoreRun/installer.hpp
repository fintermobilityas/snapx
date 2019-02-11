#pragma once

#include <string>
#include <vector>

namespace snap {
    class installer {
    public:
        static int run(std::vector<std::string> arguments, const size_t nupkg_size, uint8_t* nupkg_start_ptr, uint8_t* nupkg_end_ptr);
        static bool is_valid_payload(const size_t nupkg_size, uint8_t* nupkg_start_ptr, uint8_t* nupkg_end_ptr);
    };
}