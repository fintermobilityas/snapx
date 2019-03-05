#if defined(PAL_PLATFORM_WINDOWS)
// Copyright (c) 2013 GitHub, Inc. All rights reserved.
// Use of this source code is governed by MIT license that can be found in the
// LICENSE file.
//
// This file is modified from Rescle written by yoshio.okumura@gmail.com:
// http://code.google.com/p/rescle/

#include "rcedit.hpp"

#pragma warning(disable : 4244 4267)  

namespace snap::rcedit
{

    namespace {
#pragma pack(push,2)
        typedef struct _GRPICONENTRY {
            BYTE width;
            BYTE height;
            BYTE colourCount;
            BYTE reserved;
            BYTE planes;
            BYTE bitCount;
            WORD bytesInRes;
            WORD bytesInRes2;
            WORD reserved2;
            WORD id;
        } GRPICONENTRY;
#pragma pack(pop)

#pragma pack(push,2)
        typedef struct _GRPICONHEADER {
            WORD reserved;
            WORD type;
            WORD count;
            GRPICONENTRY entries[1];
        } GRPICONHEADER;
#pragma pack(pop)

#pragma pack(push,1)
        typedef struct _VS_VERSION_HEADER {
            WORD wLength;
            WORD wValueLength;
            WORD wType;
        } VS_VERSION_HEADER;
#pragma pack(pop)

#pragma pack(push,1)
        typedef struct _VS_VERSION_STRING {
            VS_VERSION_HEADER Header;
            WCHAR szKey[1];
        } VS_VERSION_STRING;
#pragma pack(pop)

#pragma pack(push,1)
        typedef struct _VS_VERSION_ROOT_INFO {
            WCHAR szKey[16];
            WORD  Padding1[1];
            VS_FIXEDFILEINFO Info;
        } VS_VERSION_ROOT_INFO;
#pragma pack(pop)

#pragma pack(push,1)
        typedef struct _VS_VERSION_ROOT {
            VS_VERSION_HEADER Header;
            VS_VERSION_ROOT_INFO Info;
        } VS_VERSION_ROOT;
#pragma pack(pop)

        // The default en-us LANGID.
        LANGID kLangEnUs = 1033;
        LANGID kCodePageEnUs = 1200;

        template<typename T>
        inline T round(T value, int modula = 4) {
            return value + ((value % modula > 0) ? (modula - value % modula) : 0);
        }

        class ScopedFile {
        public:
            ScopedFile(const WCHAR* path)
                : file_(CreateFile(path, GENERIC_READ, FILE_SHARE_READ, nullptr, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, nullptr)) {}
            ~ScopedFile() { CloseHandle(file_); }

            operator HANDLE() { return file_; }

        private:
            HANDLE file_;
        };
    }

    ResourceUpdater::ResourceUpdater() : module_(nullptr) {
    }

    ResourceUpdater::~ResourceUpdater() {
        if (module_ != nullptr) {
            FreeLibrary(module_);
            module_ = nullptr;
        }
    }

    bool ResourceUpdater::Load(const WCHAR* filename) {
        wchar_t abspath[MAX_PATH] = { 0 };
        if (_wfullpath(abspath, filename, MAX_PATH)) {
            module_ = LoadLibraryEx(abspath, nullptr, DONT_RESOLVE_DLL_REFERENCES | LOAD_LIBRARY_AS_DATAFILE);
        } else {
            module_ = LoadLibraryEx(filename, nullptr, DONT_RESOLVE_DLL_REFERENCES | LOAD_LIBRARY_AS_DATAFILE);
        }

        if (module_ == nullptr) {
            return false;
        }

        this->filename_ = filename;

        EnumResourceNamesW(module_, RT_GROUP_ICON, OnEnumResourceName, reinterpret_cast<LONG_PTR>(this));
        EnumResourceNamesW(module_, RT_ICON, OnEnumResourceName, reinterpret_cast<LONG_PTR>(this));

        return true;
    }

    bool ResourceUpdater::SetIcon(const WCHAR* path, const LANGID& langId, UINT iconBundle) {
        std::unique_ptr<IconsValue>& pIcon = iconBundleMap_[langId].iconBundles[iconBundle];
        if (!pIcon) {
            pIcon = std::make_unique<IconsValue>();
        }

        auto& icon = *pIcon;
        DWORD bytes;

        ScopedFile file(path);
        if (file == INVALID_HANDLE_VALUE) {
            return false;
        }

        IconsValue::ICONHEADER& header = icon.header;
        if (!ReadFile(file, &header, 3 * sizeof(WORD), &bytes, nullptr)) {
            return false;
        }

        if (header.reserved != 0 || header.type != 1) {
            return false;
        }

        header.entries.resize(header.count);
        if (!ReadFile(file, header.entries.data(), header.count * sizeof(IconsValue::ICONENTRY), &bytes, nullptr)) {
            return false;
        }

        icon.images.resize(header.count);
        for (size_t i = 0; i < header.count; ++i) {
            icon.images[i].resize(header.entries[i].bytesInRes);
            SetFilePointer(file, header.entries[i].imageOffset, nullptr, FILE_BEGIN);
            if (!ReadFile(file, icon.images[i].data(), icon.images[i].size(), &bytes, nullptr)) {
                return false;
            }
        }

        icon.grpHeader.resize(3 * sizeof(WORD) + header.count * sizeof(GRPICONENTRY));
        GRPICONHEADER* pGrpHeader = reinterpret_cast<GRPICONHEADER*>(icon.grpHeader.data());
        pGrpHeader->reserved = 0;
        pGrpHeader->type = 1;
        pGrpHeader->count = header.count;
        for (size_t i = 0; i < header.count; ++i) {
            GRPICONENTRY* entry = pGrpHeader->entries + i;
            entry->bitCount = 0;
            entry->bytesInRes = header.entries[i].bitCount;
            entry->bytesInRes2 = header.entries[i].bytesInRes;
            entry->colourCount = header.entries[i].colorCount;
            entry->height = header.entries[i].height;
            entry->id = i + 1;
            entry->planes = header.entries[i].planes;
            entry->reserved = header.entries[i].reserved;
            entry->width = header.entries[i].width;
            entry->reserved2 = 0;
        }

        return true;
    }

    bool ResourceUpdater::SetIcon(const WCHAR* path, const LANGID& langId) {
        UINT iconBundle = iconBundleMap_.count(langId) ? iconBundleMap_[langId].iconBundles.begin()->first : 0u;
        return SetIcon(path, langId, iconBundle);
    }

    bool ResourceUpdater::SetIcon(const WCHAR* path) {
        LANGID langId = iconBundleMap_.empty() ? kLangEnUs
            : iconBundleMap_.begin()->first;
        return SetIcon(path, langId);
    }

    bool ResourceUpdater::HasIcon() {
        return !iconBundleMap_.empty();
    }

    bool ResourceUpdater::Commit() {
        if (module_ == nullptr) {
            return false;
        }
        FreeLibrary(module_);
        module_ = nullptr;

        ScopedResourceUpdater ru(filename_.c_str(), false);
        if (ru.Get() == nullptr) {
            return false;
        }

        for (const auto& iLangIconInfoPair : iconBundleMap_) {
            auto langId = iLangIconInfoPair.first;
            auto maxIconId = iLangIconInfoPair.second.maxIconId;
            for (const auto& iNameBundlePair : iLangIconInfoPair.second.iconBundles) {
                UINT bundleId = iNameBundlePair.first;
                const std::unique_ptr<IconsValue>& pIcon = iNameBundlePair.second;
                if (!pIcon) {
                    continue;
                }

                auto& icon = *pIcon;
                // update icon.
                if (icon.grpHeader.size() > 0) {
                    if (!UpdateResource(ru.Get(), RT_GROUP_ICON, MAKEINTRESOURCE(bundleId),
                        langId, icon.grpHeader.data(), icon.grpHeader.size())) {
                        return false;
                    }

                    for (size_t i = 0; i < icon.header.count; ++i) {
                        if (!UpdateResource(ru.Get(), RT_ICON, MAKEINTRESOURCE(i + 1),
                            langId, icon.images[i].data(), icon.images[i].size())) {

                            return false;
                        }
                    }

                    for (size_t i = icon.header.count; i < maxIconId; ++i) {
                        if (!UpdateResource(ru.Get(), RT_ICON, MAKEINTRESOURCE(i + 1),
                            langId, nullptr, 0)) {
                            return false;
                        }
                    }
                }
            }
        }

        return ru.Commit();
    }

    // static
    BOOL CALLBACK ResourceUpdater::OnEnumResourceLanguage(HANDLE hModule, LPCWSTR lpszType, LPCWSTR lpszName, WORD wIDLanguage, LONG_PTR lParam) {
        ResourceUpdater* instance = reinterpret_cast<ResourceUpdater*>(lParam);
        auto iconId = 0u;
        auto maxIconId = 0u;
        if (IS_INTRESOURCE(lpszName) && IS_INTRESOURCE(lpszType)) {
            if (lpszType == RT_ICON)
            {
                iconId = reinterpret_cast<ptrdiff_t>(lpszName);
                maxIconId = instance->iconBundleMap_[wIDLanguage].maxIconId;
                if (iconId > maxIconId) {
                    maxIconId = iconId;
                }
            }
            else if (lpszType == RT_GROUP_ICON)
            {
                iconId = reinterpret_cast<ptrdiff_t>(lpszName);
                instance->iconBundleMap_[wIDLanguage].iconBundles[iconId] = nullptr;
            }
        }
        return TRUE;
    }

    // static
    BOOL CALLBACK ResourceUpdater::OnEnumResourceName(HMODULE hModule, LPCWSTR lpszType, LPWSTR lpszName, LONG_PTR lParam) {
        EnumResourceLanguages(hModule, lpszType, lpszName, (ENUMRESLANGPROCW)OnEnumResourceLanguage, lParam);
        return TRUE;
    }

    ScopedResourceUpdater::ScopedResourceUpdater(const WCHAR* filename, bool deleteOld)
        : handle_(BeginUpdateResource(filename, deleteOld)) {
    }

    ScopedResourceUpdater::~ScopedResourceUpdater() {
        if (!commited_) {
            EndUpdate(false);
        }
    }

    HANDLE ScopedResourceUpdater::Get() const {
        return handle_;
    }

    bool ScopedResourceUpdater::Commit() {
        commited_ = true;
        return EndUpdate(true);
    }

    bool ScopedResourceUpdater::EndUpdate(bool doesCommit) {
        BOOL fDiscard = doesCommit ? FALSE : TRUE;
        BOOL bResult = EndUpdateResource(handle_, fDiscard);
        DWORD e = GetLastError();
        return bResult ? true : false;
    }

}
#endif
