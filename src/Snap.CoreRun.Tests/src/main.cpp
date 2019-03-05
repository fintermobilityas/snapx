#include "gtest/gtest.h"

#if defined(PAL_LOGGING_ENABLED)
#include <plog/Log.h>
#include <plog/Appenders/ColorConsoleAppender.h> 
#include <plog/Appenders/DebugOutputAppender.h> 
#endif

int main(int argc, char* argv[])
{
#ifdef PAL_LOGGING_ENABLED
    static plog::ColorConsoleAppender<plog::TxtFormatter> consoleAppender;
    static plog::DebugOutputAppender<plog::TxtFormatter> debugOutputAppender;
    plog::init(plog::Severity::verbose, &consoleAppender)
        .addAppender(&debugOutputAppender); 
#endif
     
    testing::InitGoogleTest(&argc, argv);
    return RUN_ALL_TESTS();
}
