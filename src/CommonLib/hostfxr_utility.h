// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

#pragma once
class HOSTFXR_PARAMETERS
{
public:
    HOSTFXR_PARAMETERS();
    ~HOSTFXR_PARAMETERS();

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

    static
    HRESULT
    GetPortableHostfxrParameters(
        HOSTFXR_PARAMETERS* pHostFxrParameters,
        ASPNETCORE_CONFIG *pConfig
    );
private:
    static
    HANDLE CheckIfFileExists(STRU * struFile);
};

