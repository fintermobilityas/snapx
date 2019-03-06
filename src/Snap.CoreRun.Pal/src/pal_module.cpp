#include "pal/pal.hpp"
#include "pal/pal_module.hpp"

pal_module::pal_module(const std::string& filename) :
    m_module(nullptr),
    m_filename(filename)
{
    pal_load_library(filename.c_str(), FALSE, &m_module);
}

pal_module::~pal_module()
{
    const auto ptr = m_module;
    if (ptr != nullptr)
    {
        pal_free_library(ptr);
        m_module = nullptr;
    }
}

bool pal_module::is_loaded() const
{
    return m_module != nullptr;
}

const std::string& pal_module::get_filename() const
{
    return m_filename;
}

void* pal_module::bind(const std::string& fn)
{
    if (!this->is_loaded())
    {
        LOGE << "Failed to load method because module is not loaded. Method: " << fn << ". Module: " << get_filename();
        return nullptr;
    }

    void* ptr_fn = nullptr;
    if (!pal_getprocaddress(m_module, fn.c_str(), &ptr_fn))
    {
        LOGE << "Failed to load method: " << fn << ". Module: " << get_filename();
        return nullptr;
    }
    return ptr_fn;
}
