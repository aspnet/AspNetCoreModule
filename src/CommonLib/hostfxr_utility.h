// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

#pragma once

typedef INT(*hostfxr_get_native_search_directories_fn) (const int argc, const WCHAR* argv[], WCHAR* dest, size_t dest_size);
typedef INT(*hostfxr_main_fn) (CONST DWORD argc, CONST WCHAR* argv[]);

class HOSTFXR_PARAMETERS
{
public:
    HOSTFXR_PARAMETERS()
    {
    }

    ~HOSTFXR_PARAMETERS()
    {
    }

    STRU*
    QueryArguments(
        VOID
    )
    {
        return &m_struArguments;
    }

    STRU*
    QueryExePath(
        VOID
    )
    {
        return &m_struExePath;
    }

    STRU*
    QueryHostfxrLocation(
        VOID
    )
    {
        return &m_struHostFxrLocation;
    }

protected:
    STRU    m_struHostFxrLocation;
    STRU    m_struExePath;
    STRU    m_struArguments;
    // TODO more parameters
};

class HOSTFXR_UTILITY
{
public:
    HOSTFXR_UTILITY();
    ~HOSTFXR_UTILITY();

    static
    HRESULT
    GetHostFxrParameters(
        HOSTFXR_PARAMETERS* pHostFxrParameters,
        ASPNETCORE_CONFIG *pConfig
    );

    static
    HRESULT
    GetStandaloneHostfxrParameters(
        HOSTFXR_PARAMETERS* pHostFxrParameters,
        ASPNETCORE_CONFIG *pConfig
    );
};

