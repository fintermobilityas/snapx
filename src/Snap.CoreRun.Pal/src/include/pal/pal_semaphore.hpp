#pragma once

#if defined(PAL_PLATFORM_WINDOWS) || defined(PAL_PLATFORM_MINGW)
#include <synchapi.h>
#elif defined(PAL_PLATFORM_LINUX)
#include <fcntl.h>           /* For O_* constants */
#include <sys/stat.h>        /* For mode constants */
#include <semaphore.h>
#endif

#include <string>

class pal_semaphore_machine_wide final {
private:
#if defined(PAL_PLATFORM_WINDOWS) || defined(PAL_PLATFORM_MINGW)
    HANDLE m_semaphore;
#elif defined(PAL_PLATFORM_LINUX)
    sem_t* m_semaphore;
#endif
    std::string m_semaphore_name;

public:
    explicit pal_semaphore_machine_wide(const std::string& name);
    bool try_create();
    bool release();
    ~pal_semaphore_machine_wide();
};
