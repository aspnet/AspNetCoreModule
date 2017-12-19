// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

#pragma once

typedef INT(*hostfxr_get_native_search_directories_fn) (const int argc, const WCHAR* argv[], WCHAR* dest, size_t dest_size);
typedef INT(*hostfxr_main_fn) (CONST DWORD argc, CONST WCHAR* argv[]);


class HOSTFXR_UTILITY
{
public:
    HOSTFXR_UTILITY();
    ~HOSTFXR_UTILITY();

    static
    HRESULT
    GetHostFxrParameters(
        ASPNETCORE_CONFIG *pConfig
    );

    static
    HRESULT GetArguments(
        STRU * struArguments, 
        STRU * pstruExePath,
        ASPNETCORE_CONFIG *pConfig
    );

private:
    static
    HRESULT
    GetStandaloneHostfxrParameters(
        ASPNETCORE_CONFIG *pConfig
    );
};

