#if PLATFORM_WINDOWS
// Copyright (c) 2013 GitHub, Inc. All rights reserved.
// Use of this source code is governed by MIT license that can be found in the
// LICENSE file.
//
// This file is modified from Rescle written by yoshio.okumura@gmail.com:
// http://code.google.com/p/rescle/

#pragma once

#include <string>
#include <vector>
#include <map>
#include <memory> // unique_ptr

#include <windows.h>

namespace snap::rcedit
{

    struct IconsValue {
        typedef struct _ICONENTRY {
            BYTE width;
            BYTE height;
            BYTE colorCount;
            BYTE reserved;
            WORD planes;
            WORD bitCount;
            DWORD bytesInRes;
            DWORD imageOffset;
        } ICONENTRY;

        typedef struct _ICONHEADER {
            WORD reserved;
            WORD type;
            WORD count;
            std::vector<ICONENTRY> entries;
        } ICONHEADER;

        ICONHEADER header;
        std::vector<std::vector<BYTE>> images;
        std::vector<BYTE> grpHeader;
    };

    class ResourceUpdater {
    public:
        typedef std::map<UINT, std::unique_ptr<IconsValue>> IconTable;

        struct IconResInfo {
            UINT maxIconId = 0;
            IconTable iconBundles;
        };

        typedef std::map<LANGID, IconResInfo> IconTableMap;

        ResourceUpdater();
        ~ResourceUpdater();

        bool Load(const WCHAR* filename);
        bool SetIcon(const WCHAR* path, const LANGID& langId, UINT iconBundle);
        bool SetIcon(const WCHAR* path, const LANGID& langId);
        bool SetIcon(const WCHAR* path);
        bool Commit();

        static BOOL CALLBACK OnEnumResourceName(HMODULE hModule, LPCWSTR lpszType, LPWSTR lpszName, LONG_PTR lParam);
        static BOOL CALLBACK OnEnumResourceLanguage(HANDLE hModule, LPCWSTR lpszType, LPCWSTR lpszName, WORD wIDLanguage, LONG_PTR lParam);

        HMODULE module_;
        std::wstring filename_;
        IconTableMap iconBundleMap_;
    };

    class ScopedResourceUpdater {
    public:
        ScopedResourceUpdater(const WCHAR* filename, bool deleteOld);
        ~ScopedResourceUpdater();

        HANDLE Get() const;
        bool Commit();

    private:
        bool EndUpdate(bool doesCommit);

        HANDLE handle_;
        bool commited_ = false;
    };

}
#endif
