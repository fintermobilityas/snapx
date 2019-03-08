#pragma once

#include <string>

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

    bool is_loaded() const;
    const std::string& get_filename() const;
    template<typename T>
    T bind(const std::string& fn)
    {
        return reinterpret_cast<T>(_bind(fn));
    }
private:
    void* _bind(const std::string& fn);
};
