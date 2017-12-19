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
        if (m_pcwArguments != NULL)
        {
            delete[] m_pcwArguments;
        }
    }

    PCWSTR**
    QueryArguments(
        VOID
    )
    {
        return &m_pcwArguments;
    }

    STRU*
    QueryHostfxrLocation(
        VOID
    )
    {
        return &m_struHostFxrLocation;
    }

    DWORD*
    QueryArgCount(
        VOID
    )
    {
        return &m_dwArgc;
    }

protected:
    STRU    m_struHostFxrLocation;
    PCWSTR* m_pcwArguments;
    DWORD   m_dwArgc;
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
    HRESULT GetArguments(
        STRU * struArguments, 
        STRU * pstruExePath, 
        HOSTFXR_PARAMETERS * pHostFxrParameters
    );

private:
    static
    HRESULT
    GetStandaloneHostfxrParameters(
        HOSTFXR_PARAMETERS* pHostFxrParameters,
        ASPNETCORE_CONFIG *pConfig
    );
};

