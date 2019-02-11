#include "installer.hpp"
#include "easylogging++.h"
#include "vendor/miniz.h"

int snap::installer::run(std::vector<std::string> arguments, size_t nupkg_size, uint8_t *nupkg_start_ptr, uint8_t *nupkg_end_ptr)
{
    if(!is_valid_payload(nupkg_size, nupkg_start_ptr, nupkg_end_ptr)) {
        return -1;
    }
    return 0;
}

bool snap::installer::is_valid_payload(const size_t nupkg_size, uint8_t *nupkg_start_ptr, uint8_t *nupkg_end_ptr) {
    if(nupkg_start_ptr == nullptr
        || nupkg_end_ptr == nullptr)
    {
        return false;
    }
    auto payload_size = 0;
    for (uint8_t *byte=nupkg_start_ptr; byte<nupkg_end_ptr; ++byte) {
        payload_size += 1;
    }
    return payload_size == nupkg_size;
}
