#include "pal/pal.hpp"
#include "pal/pal_semaphore.hpp"

pal_semaphore_machine_wide::pal_semaphore_machine_wide(const std::string& name) :
    m_semaphore(nullptr),
    m_semaphore_name("Global\\" + name)
{
}

pal_semaphore_machine_wide::~pal_semaphore_machine_wide() {
    release();
}

bool pal_semaphore_machine_wide::try_create() {
#if defined(PAL_PLATFORM_WINDOWS) || defined(PAL_PLATFORM_MINGW)
    pal_utf16_string semaphore_name_utf16_string(m_semaphore_name);
    if(m_semaphore_name.size() > PAL_MAX_PATH) {
        return false;
    }
    // If name is not null and initiallyOwned is true, the calling thread owns the mutex only
    // if the named system mutex was created as a result of this call.
    auto semaphore = ::CreateMutex(nullptr, TRUE, semaphore_name_utf16_string.data());
    auto status = semaphore == nullptr ? 0 : GetLastError();
    if (semaphore == nullptr || status == ERROR_ALREADY_EXISTS) {
        return false;
    }
    m_semaphore = semaphore;
    return true;
#elif defined(PAL_PLATFORM_LINUX)
    if(m_semaphore_name.size() > PAL_MAX_PATH) {
        return false;
    }
    auto semaphore = sem_open(m_semaphore_name.c_str(), O_CREAT | O_EXCL);
    if(SEM_FAILED == semaphore) {
        return false;
    }
    m_semaphore = semaphore;
    return true;
#endif
    return false;
}

bool pal_semaphore_machine_wide::release() {
    if(m_semaphore == nullptr) 
    {
        return false;
    }
#if defined(PAL_PLATFORM_WINDOWS)
    if (0 == ReleaseMutex(m_semaphore)) {
        return false;
    }
    if (0 == CloseHandle(m_semaphore)) {
        return false;
    }
#elif defined(PAL_PLATFORM_LINUX)
    return 0 == sem_close(m_semaphore);
#endif
    m_semaphore = nullptr;
    return true;
}
