#include "pal/pal.hpp"
#include "pal/pal_semaphore.hpp"

pal_semaphore_machine_wide::pal_semaphore_machine_wide(const std::string& name) :
    m_semaphore(nullptr),
#if defined(PAL_PLATFORM_WINDOWS) 
    m_semaphore_name("Global\\" + name)
#else
    m_semaphore_name("/" + name)
#endif
{
}

pal_semaphore_machine_wide::~pal_semaphore_machine_wide() {
    release();
}

bool pal_semaphore_machine_wide::try_create() {
#if defined(PAL_PLATFORM_WINDOWS)
    pal_utf16_string semaphore_name_utf16_string(m_semaphore_name);
    auto mutex = ::OpenMutex(SYNCHRONIZE, TRUE, semaphore_name_utf16_string.data());
    if(mutex != nullptr) {
        CloseHandle(mutex);
        return false;
    }

    // If name is not null and initiallyOwned is true, the calling thread owns the mutex only
    // if the named system mutex was created as a result of this call.
    mutex = ::CreateMutex(nullptr, TRUE, semaphore_name_utf16_string.data());    
    if (mutex == nullptr || GetLastError() == ERROR_ALREADY_EXISTS) {
        return false;
    }    

    m_semaphore = mutex;
    return true;
#elif defined(PAL_PLATFORM_LINUX)
    // http://man7.org/linux/man-pages/man7/sem_overview.7.html
    auto semaphore = sem_open(m_semaphore_name.c_str(), O_CREAT | O_EXCL, 0777, 0);
    if(SEM_FAILED == semaphore) {
        return false;
    }
    m_semaphore = semaphore;
    return true;
#else
    return false;
#endif
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
    sem_unlink(m_semaphore_name.c_str());
#endif
    m_semaphore = nullptr;
    return true;
}
