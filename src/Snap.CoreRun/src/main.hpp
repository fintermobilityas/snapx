#pragma once

#include "corerun.hpp"
#include "stubexecutable.hpp"
#include "cxxopts/include/cxxopts.hpp"
#include <plog/Log.h>

#if PAL_PLATFORM_LINUX
#include "unistd.h" // fork
#include <csignal>
#endif

#include <memory>

static std::unique_ptr<pal_semaphore_machine_wide> corerun_supervisor_semaphore;

inline int corerun_command_supervise(
    const std::string& stub_executable_full_path,
    std::vector<std::string>& arguments,
    std::map<std::string, std::string>& environment_variables,
    int process_id,
    const std::string& process_application_id,
    int cmd_show_windows);
inline void main_wait_for_pid(pal_pid_t pid);
inline void snapx_maybe_wait_for_debugger();

#if PAL_PLATFORM_LINUX
void corerun_main_signal_handler(int signum) {
    LOGD << "Interrupt signal: " << signum;

    if(corerun_supervisor_semaphore != nullptr) {
        LOGD << "Supervisor semaphore released: " << (corerun_supervisor_semaphore->release() ? "true" : "false");
    }

    LOGD << "Supervisor will now exit.";
    exit(signum);
}
#endif

inline int corerun_main_impl(int argc, char **argv, const int cmd_show_windows) {
#if PAL_PLATFORM_LINUX
    std::signal(SIGTERM, corerun_main_signal_handler);
#endif
    
    LOGD << "Process started. "
         << "Startup arguments(" << std::to_string(argc) << "): "
         << this_exe::build_argv_str(argc, argv);

    const auto snapx_corerun_allow_elevated_context = []
    {
        const auto value = std::make_unique<char*>(new char);
        pal_env_get("SNAPX_CORERUN_ALLOW_ELEVATED_CONTEXT", value.get());
        const auto allow = pal_str_iequals(*value, "1") || pal_str_iequals(*value, "true");
        if(allow)
        {
            LOGW << "Allowing corerun to run in an elevated context.";
        }
        return allow;
    };

    if (pal_is_elevated() 
        && !snapx_corerun_allow_elevated_context()) 
    {        
        LOGE << "Current user account is elevated to either root / Administrator, exiting..";
        return 1;
    }

    snapx_maybe_wait_for_debugger();

    const auto stub_executable_full_path = std::string(argv[0]);
    std::vector<std::string> stub_executable_arguments(argv, argv + argc);
    stub_executable_arguments.erase(stub_executable_arguments.begin()); // Remove "this" executable name.

    cxxopts::Options options(argv[0], "");

    auto supervise_process_id = 0;
    std::string supervise_id;
    std::vector<std::string> environment_variables_vec;
    options
            .add_options()
                    ("corerun-environment-var",
                      "A key value pair for setting one or multiple environment variables",
                      cxxopts::value<std::vector<std::string>>(environment_variables_vec)
                        )
                    ("corerun-supervise-pid",
                     "Supervision of target process. Wait for process pid to exit and then restart it.",
                     cxxopts::value<int>(supervise_process_id)
                        )
                    ("corerun-supervise-id",
                        "A unique id that identifies current application.",
                        cxxopts::value<std::string>(supervise_id)
                        );

    std::map<std::string, std::string> environment_variables;

    try {
        options.parse_positional("corerun-environment-var");
        const auto result = options.parse(argc, argv);
        if (result.count("corerun-environment-var")) {
          const auto pairs = result["corerun-environment-var"].as<std::vector<std::string>>();
          for (const auto& kv : pairs) {
            const auto pos = kv.find('=');
            if (pos == std::string::npos) {
              LOGE << "Invalid environment variable pair: " << kv;
              return 1;
            }
            const std::string key = kv.substr(0, pos);
            const std::string value = kv.substr(pos + 1);
            environment_variables.emplace(key, value);
          }
        }

    } catch (const cxxopts::OptionException &e) {
        LOGE << "Error parsing startup argument: " << e.what();
    }

    if (supervise_process_id > 0) {
        return corerun_command_supervise(stub_executable_full_path, stub_executable_arguments,
            environment_variables,
                supervise_process_id, supervise_id, cmd_show_windows);
    }

    return snap::stubexecutable::run(stub_executable_arguments,
                                     environment_variables, cmd_show_windows);
}

inline int corerun_command_supervise(
    const std::string& stub_executable_full_path,
    std::vector<std::string>& arguments,
    std::map<std::string, std::string>& environment_variables,
    const int process_id,
    const std::string& process_application_id,
    const int cmd_show_windows)
{
    if(!pal_process_is_running(process_id))  
    {
        LOGE << "Supervision of target process with id " << std::to_string(process_id) << " cancelled because the program is not running.";
        return 1;
    }

    auto semaphore_name("corerun-" + process_application_id);

    if (semaphore_name.size() > PAL_MAX_PATH) {
        LOGW << "Semaphore name exceeds PAL_MAX_PATH length (" << std::to_string(PAL_MAX_PATH) << "). Name: " << semaphore_name;
        return 1;
    }

    corerun_supervisor_semaphore = std::make_unique<pal_semaphore_machine_wide>(semaphore_name);
    if(!corerun_supervisor_semaphore->try_create()) {
        LOGE << "Aborting supervision of target process with id " << std::to_string(process_id) << " because a supervisor is already running. Process application id: " << process_application_id;
        return 1;
    }

    const auto* const corerun_dash_dash = "--corerun-";

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
         << "Semaphore released: " <<  corerun_supervisor_semaphore->release() << ". "
         << "Startup arguments("<< std::to_string(arguments.size()) << "): "
         << this_exe::build_argv_str(arguments);

#if defined(PAL_PLATFORM_LINUX)
    PAL_UNUSED(cmd_show_windows);
    const auto child_pid = fork();
    if (child_pid == 0)
    {
        return snap::stubexecutable::run(arguments, environment_variables, -1);
    }
    return 0;
#else
    return snap::stubexecutable::run(arguments, environment_variables, cmd_show_windows);
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
