#pragma once

#include "pal.hpp"

#ifdef PAL_LOGGING_ENABLED
#include <plog/Log.h>
#endif

class pal_module final
{
    void* m_module;
    std::string m_filename;

public:
    explicit pal_module(const std::string& filename);
    pal_module(const pal_module&) noexcept = delete;
    pal_module& operator=(const pal_module&) noexcept = delete;
    pal_module(pal_module&&) noexcept = delete;
    pal_module& operator=(pal_module&&) noexcept = delete;
    ~pal_module();

    bool is_loaded();
    const std::string& get_filename() const;
    template<typename T>
    T bind_fn(const std::string& fn);
};

template<class T>
T pal_module::bind_fn(const std::string& fn)
{
    if(!this->is_loaded())
    {
#ifdef PAL_LOGGING_ENABLED
        LOGE << "Failed to load method because module is not loaded. Method: " << fn << ". Module: " << get_filename();
#endif    
        return false;
    }

    void* ptr_fn = nullptr;
    if(!pal_getprocaddress(m_module, fn.c_str(), &ptr_fn))
    {
#ifdef PAL_LOGGING_ENABLED
        LOGE << "Failed to load method: " << fn << ". Module: " << get_filename();
#endif
        return nullptr;
    }
    return reinterpret_cast<T>(ptr_fn);
}
