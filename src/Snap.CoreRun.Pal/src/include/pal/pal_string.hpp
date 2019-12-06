#pragma once

#if defined(PAL_PLATFORM_LINUX)
#include <string.h>
#define _strdup strdup
#define _wcsdup wcsdup
#elif defined(PAL_PLATFORM_WINDOWS) || defined(PAL_PLATFORM_MINGW)
#include <string>
#include <iostream>

// - String internal
wchar_t* pal_str_widen(const char* utf8_str);
char* pal_str_narrow(const wchar_t* utf16_str);

#if defined(PAL_PLATFORM_MINGW)
#include <string.h>
#ifndef WC_ERR_INVALID_CHARS
#define WC_ERR_INVALID_CHARS 0x0080
#endif
#define _strdup strdup
#define _wcsdup wcsdup
#endif

template<class TStorageClass, class TStdString>
class pal_string
{
    TStorageClass* m_ptr;
protected:
    std::basic_string<TStorageClass> m_value;

public:
    virtual ~pal_string()
    {
        if (m_ptr != nullptr)
        {
            delete m_ptr;
            m_ptr = nullptr;
        }

        m_value = TStdString();
    }

    pal_string(TStorageClass* str_ptr, const bool free) :
        m_ptr(free ? str_ptr : nullptr),
        m_value(str_ptr == nullptr ? TStdString() : str_ptr)
    {
        if (str_ptr == nullptr && !free)
        {
            throw std::runtime_error("str_ptr cannot be nullptr when free is false");
        }
    }

    // Copy
    pal_string(const pal_string&) noexcept = delete;
    pal_string& operator=(const pal_string&) noexcept = delete;

    // Move
    pal_string(pal_string&&) noexcept = delete;
    pal_string& operator=(pal_string&&) noexcept = delete;

    friend std::basic_ostream<TStorageClass>& operator<<(std::basic_ostream<TStorageClass>& out, const pal_string& pal_string)
    {
        out << pal_string.m_value;
        return out;
    }

    virtual TStorageClass* dup() = 0;

    TStorageClass* data()
    {
        return m_value.data();
    }

    void prepend_if(const bool yes, const TStdString& string)
    {
        if(!yes)
        {
            return;
        }
        prepend(string);
    }

    void prepend(const TStdString& string)
    {
        m_value = m_value.insert(0, string);        
    }

    void append_if(const bool yes, const TStdString& string)
    {
        if(!yes)
        {
            return;
        }
        append(string);
    }

    void append(const TStdString& string)
    {
        m_value = m_value.append(string);
    }

    void append_if_not_ends_width(const TStdString& string)
    {
        append_if(!ends_with(string), string);
    }

    void remove_if_ends_width(const TStdString& string)
    {
        if(!ends_with(string))
        {
            return;
        }

        const auto pos = m_value.find_last_of(string);
        if(pos == std::string::npos)
        {
            return;
        }

        m_value = m_value.substr(0, pos);
    }

    TStdString str()
    {
        return m_value;
    }

    bool ends_with(const TStdString& string)
    {
        return m_value.size() >= string.size() 
            && 0 == m_value.compare(m_value.size() - string.size(), string.size(), string);
    }

    bool starts_with(const TStdString& string)
    {
        return m_value.size() >= string.size() 
            && 0 == m_value.compare(0, string.size(), string);
    }

    bool empty()
    {
        return m_value.empty();
    }

    bool empty_or_whitespace()
    {
        return empty() || m_value.find_first_not_of(' ') == m_value.npos;
    }

    size_t size()
    {
        return m_value.size();
    }

    [[nodiscard]] const TStorageClass* c_str() const noexcept
    {
        return m_value.c_str();
    }


};

class pal_utf8_string final : public pal_string<char, std::string>
{
    pal_utf8_string(char* utf8_string, const bool free) : pal_string<char, std::basic_string<char>>(utf8_string, free)
    {
    }

public:
    pal_utf8_string() : pal_utf8_string(nullptr, false)
    {

    }

    explicit pal_utf8_string(const size_t size) : pal_utf8_string(new char[size], true)
    {

    }

    explicit pal_utf8_string(wchar_t* utf16_string) : pal_utf8_string(pal_str_narrow(utf16_string), true)
    {

    }

    explicit pal_utf8_string(const std::wstring& utf16_string) : pal_utf8_string(pal_str_narrow(utf16_string.data()), true)
    {

    }

    char* dup() override
    {
        return _strdup(m_value.c_str());
    }

};

class pal_utf16_string final : public pal_string<wchar_t, std::wstring>
{
    pal_utf16_string(wchar_t* utf16_string, const bool free) : pal_string<wchar_t, std::basic_string<wchar_t>>(utf16_string, free)
    {
    }

public:
    pal_utf16_string() : pal_utf16_string(nullptr, false)
    {

    }

    explicit pal_utf16_string(const size_t size) : pal_utf16_string(new wchar_t[size], true)
    {

    }

    explicit pal_utf16_string(const std::wstring& utf16_string) : pal_utf16_string(_wcsdup(utf16_string.data()), true)
    {

    }

    explicit pal_utf16_string(char* utf8_string) : pal_utf16_string(pal_str_widen(utf8_string), true)
    {

    }

    explicit pal_utf16_string(const std::string& utf8_string) : pal_utf16_string(pal_str_widen(utf8_string.data()), true)
    {

    }

    wchar_t* dup() override
    {
        return _wcsdup(m_value.c_str());
    }
};
#endif
