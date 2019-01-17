#pragma once

#include "corerun.hpp"

namespace snap
{
    using namespace std;

    class resourcewriter
    {
    public:
        static int copy_resources_to_executable(const std::wstring& src, const std::wstring& dest);

    private:
        static BOOL CALLBACK EnumResLangProc(HMODULE hModule, LPCTSTR lpszType, LPCTSTR lpszName, WORD wIDLanguage, LONG_PTR lParam);
        static BOOL CALLBACK EnumResNameProc(HMODULE hModule, LPCTSTR lpszType, LPTSTR lpszName, LONG_PTR lParam);
        static BOOL CALLBACK EnumResTypeProc(HMODULE hMod, LPTSTR lpszType, LONG_PTR lParam);
    };
}
