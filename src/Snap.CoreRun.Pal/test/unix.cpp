#include "gtest/gtest.h"

inline void independentMethod_pal_unix(int& value) {
    value = 0;
}

TEST(IndependentMethod, ResetsToZero) {
    int i = 3;
    independentMethod_pal_unix(i);
    EXPECT_EQ(0, i);

    i = 12;
    independentMethod_pal_unix(i);
    EXPECT_EQ(0,i);
}