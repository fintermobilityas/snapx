#pragma once

#include <string>
#include <random>
#include <chrono>

namespace unique_id {
    namespace detail {
        // URL-safe characters similar to nanoid
        constexpr const char *alphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz_-";
        constexpr size_t alphabet_size = 64;
        constexpr size_t default_size = 21;

        inline std::mt19937_64 &get_rng() {
            static thread_local std::mt19937_64 rng(
                static_cast<std::mt19937_64::result_type>(
                    std::chrono::high_resolution_clock::now().time_since_epoch().count()
                )
            );
            return rng;
        }
    }

    inline std::string generate(size_t size = detail::default_size) {
        std::uniform_int_distribution<size_t> dist(0, detail::alphabet_size - 1);
        auto &rng = detail::get_rng();

        std::string result;
        result.reserve(size);

        for (size_t i = 0; i < size; ++i) {
            result += detail::alphabet[dist(rng)];
        }

        return result;
    }
}

// Compatibility namespace for existing nanoid usage
namespace nanoid {
    inline std::string generate(size_t size = unique_id::detail::default_size) {
        return unique_id::generate(size);
    }
}