#pragma once

#include <string>
#include <iostream>

// - String internal
#if defined(PLATFORM_WINDOWS) || defined(PLATFORM_MINGW)
wchar_t* pal_str_widen(const char* utf8_str);
char* pal_str_narrow(const wchar_t* utf16_str);
#ifndef TRUE
#define TRUE 1
#endif
#ifndef FALSE
#define FALSE 0
#endif
#if defined(PLATFORM_MINGW)
#include <string.h>
#ifndef WC_ERR_INVALID_CHARS
#define WC_ERR_INVALID_CHARS 0x0080
#endif
#endif
#elif defined(PLATFORM_LINUX)
#endif
#include <string.h>

template<class TStringClass, class TStorageClass, class TStdString>
class pal_unicode_string
{
    virtual TStorageClass data() = 0;
    virtual TStorageClass dup() = 0;
    virtual TStorageClass slice(size_t start_pos) = 0;
    virtual TStdString str() const = 0;
    virtual TStdString& append(const TStdString& string) = 0;
    virtual bool ends_with(const TStdString& string) = 0;
    virtual bool equals(const TStdString& string) = 0;
};

class pal_utf8_string : public pal_unicode_string<pal_utf8_string, char*, std::string>
{
    char* m_ptr;
    std::string m_value;

private:    
    pal_utf8_string(char* utf8_string, bool free) : 
        m_ptr(free ? utf8_string : nullptr),
        m_value(utf8_string == nullptr ? std::string() : utf8_string)
    {

    }

public:
    explicit pal_utf8_string() : pal_utf8_string(nullptr, false)
    {

    }

    pal_utf8_string(const size_t size) : pal_utf8_string(new char[size], true)
    {

    }

    pal_utf8_string(pal_utf8_string& utf8_string) : pal_utf8_string(utf8_string.dup(), true)
    {

    }

    pal_utf8_string(const std::string& utf8_string) : pal_utf8_string(strdup(utf8_string.data()), true)
    {

    }

#if PLATFORM_WINDOWS
    pal_utf8_string(wchar_t* utf16_string) : pal_utf8_string(pal_str_narrow(utf16_string), true)
    {

    }

    pal_utf8_string(const std::wstring& utf16_string) : pal_utf8_string(pal_str_narrow(utf16_string.data()), true)
    {

    }
#endif

    ~pal_utf8_string()
    {
        if(m_ptr != nullptr)
        {
            delete m_ptr;
            m_ptr = nullptr;
        }
    }

    friend std::ostream& operator << (std::ostream &out, const pal_utf8_string& utf8_string)
    {
        out << utf8_string.m_value.data();
        return out;
    }

    virtual char* dup() override
    {
        return strdup(m_value.c_str());
    }

    virtual char* slice(size_t start_pos) override
    {
        return strdup(str().substr(start_pos).c_str());
    }

    virtual char* data() override
    {
        return m_value.data();
    }

    virtual std::string str() const override
    {
        return m_value;
    }

    virtual std::string& append(const std::string& string) override
    {
        return m_value.append(string);
    }

    virtual bool ends_with(const std::string& string) override
    {
        if (string.size() > m_value.size()) return false;
        return std::equal(string.rbegin(), string.rend(), m_value.rbegin());
    }

    virtual bool equals(const std::string& string) override
    {
        return m_value.size() == string.size() && std::equal(m_value.begin(), m_value.end(), string.begin());
    }

};

#if PLATFORM_WINDOWS
class pal_utf16_string : public pal_unicode_string<pal_utf16_string, wchar_t*, std::wstring>
{
    wchar_t* m_ptr;
    std::wstring m_value;

public:
    explicit pal_utf16_string() : 
        m_ptr(nullptr), 
        m_value(std::wstring())
    {

    }

    pal_utf16_string(wchar_t* utf16_string, bool free) :
        m_ptr(free ? utf16_string : nullptr),
        m_value(utf16_string == nullptr ? std::wstring() : utf16_string)
    {

    }

    pal_utf16_string(pal_utf16_string& utf8_string) : pal_utf16_string(utf8_string.dup(), true)
    {

    }

    pal_utf16_string(const size_t size) : pal_utf16_string(new wchar_t[size], true)
    {

    }

    pal_utf16_string(const std::string& utf8_string) : pal_utf16_string(pal_str_widen(utf8_string.data()), true)
    {

    }

    ~pal_utf16_string()
    {
        if(m_ptr != nullptr)
        {
            delete m_ptr;
            m_ptr = nullptr;
        }
    }

    friend std::ostream& operator << (std::ostream &out, const pal_utf16_string &utf16_string)
    {
        out << utf16_string.m_value.data();
        return out;
    }

    virtual wchar_t* dup() override
    {
        return wcsdup(m_value.c_str());
    }

    virtual wchar_t* data() override
    {
        return m_value.data();
    }

    virtual wchar_t* slice(size_t start_pos) override
    {
        return wcsdup(str().substr(start_pos).c_str());
    }

    virtual std::wstring str() const override
    {
        return m_value;
    }

    virtual bool ends_with(const std::wstring& string) override
    {
        if (string.size() > m_value.size()) return false;
        return std::equal(string.rbegin(), string.rend(), m_value.rbegin());
    }

    virtual bool equals(const std::wstring& string) override
    {
        return m_value.size() == string.size() && std::equal(m_value.begin(), m_value.end(), string.begin());
    }

    virtual std::wstring& append(const std::wstring& string) override
    {
        return m_value.append(string);
    }

    static wchar_t* from_utf8(char* string)
    {
        return pal_utf16_string(string).dup();
    }
};
#endif
