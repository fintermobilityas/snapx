#include "resourcewriter.hpp"

#include <vector>

int snap::resourcewriter::copy_resources_to_executable(const std::wstring& src, const std::wstring& dest)
{
    const auto h_src = LoadLibraryEx(src.c_str(), nullptr, LOAD_LIBRARY_AS_DATAFILE);
    if (!h_src) {
        return GetLastError();
    }

    auto h_update = BeginUpdateResource(dest.c_str(), true);
    if (!h_update) {
        return GetLastError();
    }

    std::vector<wchar_t*> type_list;
    EnumResourceTypes(h_src, EnumResTypeProc, reinterpret_cast<LONG_PTR>(&type_list));

    for (auto& type : type_list) {
        EnumResourceNames(h_src, type, EnumResNameProc, reinterpret_cast<LONG_PTR>(h_update));
    }

    EndUpdateResource(h_update, false);

    return true;
}

BOOL snap::resourcewriter::EnumResLangProc(HMODULE hModule, LPCTSTR lpszType, LPCTSTR lpszName, WORD wIDLanguage, LONG_PTR lParam)
{
    const auto h_update = reinterpret_cast<HANDLE>(lParam);
    const auto h_find_it_again = FindResourceEx(hModule, lpszType, lpszName, wIDLanguage);

    const auto h_global = LoadResource(hModule, h_find_it_again);
    if (!h_global) return true;

    UpdateResource(h_update, lpszType, lpszName, wIDLanguage, LockResource(h_global), SizeofResource(hModule, h_find_it_again));
    return true;
}

BOOL snap::resourcewriter::EnumResNameProc(HMODULE hModule, LPCTSTR lpszType, LPTSTR lpszName, LONG_PTR lParam)
{
    auto h_update = reinterpret_cast<HANDLE>(lParam);

    EnumResourceLanguages(hModule, lpszType, lpszName, EnumResLangProc, reinterpret_cast<LONG_PTR>(h_update));
    return true;
}

BOOL snap::resourcewriter::EnumResTypeProc(HMODULE hMod, LPTSTR lpszType, LONG_PTR lParam)
{
    auto* type_list = reinterpret_cast<std::vector<wchar_t*>*>(lParam);
    if (IS_INTRESOURCE(lpszType)) {
        type_list->push_back(lpszType);
    }
    else {
        type_list->push_back(_wcsdup(lpszType));
    }

    return true;
}
