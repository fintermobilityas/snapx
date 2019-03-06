#include "gtest/gtest.h"
#include "corerun.hpp"

int main(int argc, char* argv[])
{
    this_exe::plog_init();

    testing::InitGoogleTest(&argc, argv);
    return RUN_ALL_TESTS();
}
