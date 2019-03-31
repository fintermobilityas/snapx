#pragma once

#include "corerun.hpp"
#include "stubexecutable.hpp"
#include "cxxopts/include/cxxopts.hpp"

#if PAL_PLATFORM_LINUX

#include "unistd.h" // fork

#endif

#include <plog/Log.h>

#include <iostream>

inline int corerun_command_supervise(const std::basic_string<char> &executable_full_path,
                              std::vector<std::string> &arguments,
                              int process_id,
                              int cmd_show_windows);
inline void main_wait_for_pid(pal_pid_t pid);
inline void snapx_maybe_wait_for_debugger();

inline int corerun_main_impl(int argc, char **argv, const int cmd_show_windows) {
    LOGD << "Process started. "
         << "Startup arguments(" << std::to_string(argc) << "): "
         << this_exe::build_argv_str(argc, argv);

    if (pal_is_elevated()) {
        LOGE << "Current user account is elevated to either root / Administrator, exiting..";
        return 1;
    }

    snapx_maybe_wait_for_debugger();

    const auto stub_executable_full_path = std::string(argv[0]);
    std::vector<std::string> stub_executable_arguments(argv, argv + argc);
    stub_executable_arguments.erase(stub_executable_arguments.begin()); // Remove "this" executable name.

    cxxopts::Options options(argv[0], "");

    auto supervise_process_id = 0;

    options
            .add_options()
                    ("corerun-supervise-pid",
                     "Supervision of target process. Wait for process pid to exit and then restart it.",
                     cxxopts::value<int>(supervise_process_id));

    try {
        options.parse(argc, argv);
    } catch (const cxxopts::OptionException &e) {
        LOGE << "Error parsing startup argument: " << e.what();
    }

    if (supervise_process_id > 0) {
        return corerun_command_supervise(stub_executable_full_path, stub_executable_arguments,
                supervise_process_id, cmd_show_windows);
    }

    return snap::stubexecutable::run(stub_executable_arguments, cmd_show_windows);
}

inline int corerun_command_supervise(const std::basic_string<char> &executable_full_path,
        std::vector<std::string> &arguments, const int process_id, const int cmd_show_windows)
{
    const auto corerun_dash_dash = "--corerun-";

    auto index = 0;
    for (const auto &value : arguments) {
        if (pal_str_startswith(value.c_str(), corerun_dash_dash)) {
            arguments.erase(arguments.begin() + index);
        }
        ++index;
    }

    LOGD << "Supervisor is waiting for target process to exit: " << std::to_string(process_id);

    main_wait_for_pid(process_id);

    LOGD << "Process exited: " << std::to_string(process_id) << ". "
         << "Startup arguments("<< std::to_string(arguments.size()) << "): "
         << this_exe::build_argv_str(arguments);

#if defined(PAL_PLATFORM_LINUX)
    PAL_UNUSED(cmd_show_windows);
    const auto child_pid = fork();
    if (child_pid == 0)
    {
        return snap::stubexecutable::run(arguments, -1);
    }
    return 0;
#else
    return snap::stubexecutable::run(arguments, cmd_show_windows);
#endif
}

inline void main_wait_for_pid(const pal_pid_t pid) {
    pal_pid_t this_pid;
    if (!pal_process_get_pid(&this_pid) || this_pid == pid) {
        return;
    }

    while (TRUE == pal_process_is_running(pid)) {
        pal_sleep_ms(250);
    }
}

inline void snapx_maybe_wait_for_debugger() {
    if (!pal_env_get_bool("SNAPX_WAIT_DEBUGGER")) {
        return;
    }

    LOGD << "Waiting for debugger to attach...";
    pal_wait_for_debugger();
    LOGD << "Debugger attached.";
}
