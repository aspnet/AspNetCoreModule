#pragma once

#include "aspnetcoreconfig.h"
class HOSTFXR_UTILITY
{
public:
    HOSTFXR_UTILITY();
    ~HOSTFXR_UTILITY();

    static
    HRESULT
    FindHostFxrDll(
        ASPNETCORE_CONFIG *pConfig,
        STRU* struHostFxrDllLocation
    );

    static
    HRESULT
    GetStandaloneHostfxrLocation(
        STRU* struHostfxrPath,
        ASPNETCORE_CONFIG *pConfig
    );

    static
    HRESULT
    GetPortableHostfxrLocation(
        STRU* struHostfxrPath,
        ASPNETCORE_CONFIG *pConfig
    );
};

