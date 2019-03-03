#include "pal/pal_string.hpp"

#if defined(PAL_PLATFORM_WINDOWS)
#include <shlwapi.h> // MultiByteToWideChar
#include <strsafe.h> // StringCchLengthA
#include <stdexcept>

// https://stackoverflow.com/a/10766913
wchar_t * pal_str_widen(const char * utf8_str)
{
    if (utf8_str == nullptr || *utf8_str == '\0')
    {
        return nullptr;
    }

    // Consider CHAR's count corresponding to total input string length,
    // including end-of-string (\0) character
    const size_t cch_utf8_max = INT_MAX - 1;
    size_t cch_utf8;
    const auto hr = ::StringCchLengthA(utf8_str, cch_utf8_max, &cch_utf8);
    if (FAILED(hr))
    {
        throw std::runtime_error("Unable to determine string character count.");
    }

    // Consider also terminating \0
    ++cch_utf8;

    // Convert to 'int' for use with MultiByteToWideChar API
    const auto cb_utf8 = static_cast<int>(cch_utf8);

    // Get size of destination UTF-16 buffer, in WCHAR's
    const auto cch_utf16 = ::MultiByteToWideChar(
        CP_UTF8,                // convert from UTF-8
        MB_ERR_INVALID_CHARS,   // error on invalid chars
        utf8_str,               // source UTF-8 string
        cb_utf8,                // total length of source UTF-8 string,
                                // in CHAR's (= bytes), including end-of-string \0
        nullptr,                // unused - no conversion done in this step
        0                       // request size of destination buffer, in WCHAR's
    );

    if (cch_utf16 == 0)
    {
        throw std::runtime_error("Unable to determine UTF-16 buffer size");
    }

    const auto utf16_str = new wchar_t[cch_utf16];

    // Do the conversion from UTF-8 to UTF-16
    const auto utf16_str_len = ::MultiByteToWideChar(
        CP_UTF8,                 // convert from UTF-8
        MB_ERR_INVALID_CHARS,    // error on invalid chars
        utf8_str,                // source UTF-8 string
        cb_utf8,                 // total length of source UTF-8 string,
                                 // in CHAR's (= bytes), including end-of-string \0
        utf16_str,               // destination buffer
        cch_utf16                // size of destination buffer, in WCHAR's
    );

    if (utf16_str_len == 0)
    {
        throw std::runtime_error("Unable to convert from UTF-8 to UTF-16");
    }

    return utf16_str;
}

char * pal_str_narrow(const wchar_t * utf16_str)
{
    if (utf16_str == nullptr || *utf16_str == '\0')
    {
        return nullptr;
    }

    // Consider WCHAR's count corresponding to total input string length,
    // including end-of-string (L'\0') character.
    const size_t cch_utf16_max = INT_MAX - 1;
    size_t cch_utf16;
    const auto hr = ::StringCchLengthW(utf16_str, cch_utf16_max, &cch_utf16);
    if (FAILED(hr))
    {
        throw std::runtime_error("Unable to determine string character count.");
    }

    // Consider also terminating \0
    ++cch_utf16;

    // WC_ERR_INVALID_CHARS flag is set to fail if invalid input character is encountered
    const DWORD dw_conversion_flags = WC_ERR_INVALID_CHARS;

    //
    // Get size of destination UTF-8 buffer, in CHAR's (= bytes)
    //
    const auto utf8_buffer_len = ::WideCharToMultiByte(
        CP_UTF8,                        // convert to UTF-8
        dw_conversion_flags,              // specify conversion behavior
        utf16_str,                      // source UTF-16 string
        static_cast<int>(cch_utf16),     // total source string length, in WCHAR's,
                                        // including end-of-string \0
        nullptr,                        // unused - no conversion required in this step
        0,                              // request buffer size
        nullptr,
        nullptr          // unused
    );

    if (utf8_buffer_len == 0)
    {
        throw std::runtime_error("Unable to determine UTF-8 buffer length");
    }

    // Allocate destination buffer for UTF-8 string
    const auto utf8_buffer = new char[utf8_buffer_len];

    // Do the conversion from UTF-16 to UTF-8
    const auto utf8_str_len = ::WideCharToMultiByte(
        CP_UTF8,                        // convert to UTF-8
        dw_conversion_flags,              // specify conversion behavior
        utf16_str,                      // source UTF-16 string
        static_cast<int>(cch_utf16),     // total source string length, in WCHAR's,
                                        // including end-of-string \0
        utf8_buffer,                    // destination buffer
        utf8_buffer_len,                // destination buffer size, in bytes
        nullptr,
        nullptr          // unused
    );

    if (utf8_str_len == 0)
    {
        throw std::runtime_error("Unable to convert from UTF-16 to UTF-8.");
    }

    return utf8_buffer;
}
#endif
