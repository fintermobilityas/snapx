#pragma once

#include "pal/pal.hpp"
#include "pal/pal_module.hpp"

#include <string>

pal_module::pal_module(const std::string& filename) : 
    m_module(nullptr),
    m_filename(filename)
{
    pal_load_library(filename.c_str(), FALSE, &m_module);
}

pal_module::~pal_module()
{
    const auto ptr = m_module;
    if(ptr != nullptr)
    {
        pal_free_library(ptr);
        m_module = nullptr;        
    }
}

bool pal_module::is_loaded()
{
    return m_module != nullptr;
}

const std::string& pal_module::get_filename() const
{
    return m_filename;
}
