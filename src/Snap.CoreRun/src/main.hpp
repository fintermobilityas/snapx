#pragma once

#include "corerun.hpp"
#include "stubexecutable.hpp"

#if PAL_PLATFORM_LINUX
#include "unistd.h" // fork
#endif

#include <plog/Log.h>

#include <iostream>

inline void main_wait_for_pid(const pal_pid_t pid)
{
    pal_pid_t this_pid;
    if (!pal_process_get_pid(&this_pid) || this_pid == pid)
    {
        return;
    }

    while (TRUE == pal_process_is_running(pid))
    {
        pal_sleep_ms(250);
    }
}

inline void snapx_maybe_wait_for_debugger()
{
    if (!pal_env_get_bool("SNAPX_WAIT_DEBUGGER"))
    {
        return;
    }

    LOGD << "Waiting for debugger to attach...";
    pal_wait_for_debugger();
    LOGD << "Debugger attached.";
}

inline int corerun_main_impl(const int argc, char **argv, const int cmd_show_windows)
{
    LOGD << "Process started. "
         << "Startup arguments(" << std::to_string(argc) << "): "
        << this_exe::build_argv_str(argc, argv);

    if (pal_is_elevated())
    {
        LOGE << "Current user account is elevated to either root / Administrator, exiting..";
        return 1;
    }

    snapx_maybe_wait_for_debugger();

    const auto this_executable_full_path = std::string(argv[0]);

    std::vector<std::string> stubexecutable_arguments(argv, argv + argc);
    stubexecutable_arguments.erase(stubexecutable_arguments.begin()); // Remove "this" executable name.

    const auto command_wait_pid_str = std::string("--corerun-wait-for-process-id="); // Arguments are intentionally verbose

    auto argv_index = 0;
    for (const auto &value : stubexecutable_arguments)
    {
        const auto command_wait_pid_value = pal_str_startswith(value.c_str(), command_wait_pid_str.c_str());

        if (command_wait_pid_value)
        {
            const auto wait_pid_pos = value.find_last_of(command_wait_pid_str);
            const auto pid_fragment = value.substr(wait_pid_pos + 1);

            const pal_pid_t wait_for_this_pid = std::stoul(pid_fragment);

            LOGD << "Waiting for process with id to exit: " << wait_for_this_pid;

            if (wait_for_this_pid <= 0)
            {
                continue;
            }

            stubexecutable_arguments.erase(stubexecutable_arguments.begin() + argv_index); // Remove "this" argument

#if PAL_PLATFORM_LINUX
            // We have to resolve stub executable working directory before fork
            auto working_dir = std::make_unique<char*>(nullptr);
            if (!pal_path_get_directory_name_from_file_path(this_executable_full_path.c_str(), working_dir.get()))
            {
                LOGE << "Failed to get directory name from path: " << this_executable_full_path;
                exit(1);
            }

            // The reason why we have to fork is that we want to daemonize (background)
            // this process because when the parent process exits that process will be killed.

            LOGD << "Forking...";

            const auto child_pid = fork();
            if (child_pid == 0)
            {
                LOGD << "Inside child, wait for process to exit: " << std::to_string(wait_for_this_pid);

                // Child process has to wait for the executable that signaled the restart
                main_wait_for_pid(wait_for_this_pid);

                LOGD << "Process exited: " << std::to_string(wait_for_this_pid) << ". "
                     << "Startup arguments(" << std::to_string(stubexecutable_arguments.size()) << "): "
                     << this_exe::build_argv_str(stubexecutable_arguments);

                return snap::stubexecutable::run(stubexecutable_arguments, -1);
            }
            else
            {
                // Do not exit until process that requested restart has exited.
                main_wait_for_pid(wait_for_this_pid);

                LOGD << "Process exited: " << std::to_string(wait_for_this_pid) << ". "
                     << "Exiting this process, child is now responsible for starting process that requested restart.";

                exit(0);
            }
#else
            main_wait_for_pid(wait_for_this_pid);

            LOGD << "Process exited: " << std::to_string(wait_for_this_pid) << ". "
                 << "Startup arguments("<< std::to_string(stubexecutable_arguments.size()) << "): "
                 << this_exe::build_argv_str(stubexecutable_arguments);

            break;
#endif
        }

        ++argv_index;
    }

    return snap::stubexecutable::run(stubexecutable_arguments, cmd_show_windows);
}
