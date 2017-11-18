// dllmain.cpp : Defines the entry point for the DLL application.
#include "precomp.hxx"

BOOL APIENTRY DllMain(HMODULE hModule,
    DWORD  ul_reason_for_call,
    LPVOID lpReserved
)
{
    switch (ul_reason_for_call)
    {
    case DLL_PROCESS_ATTACH:
        DisableThreadLibraryCalls(hModule);
        break;
    default:
        break;
    }
    return TRUE;
}

HRESULT
__stdcall
CreateApplication(
    _In_  IHttpServer        *pServer,
    _In_  ASPNETCORE_CONFIG  *pConfig,
    _Out_ APPLICATION       **ppApplication
)
{
    HRESULT      hr = S_OK;
    APPLICATION *pApplication = NULL;

    if (pConfig->QueryHostingModel() == APP_HOSTING_MODEL::HOSTING_IN_PROCESS)
    {
        pApplication = new IN_PROCESS_APPLICATION(pServer, pConfig);
    }
    else if (pConfig->QueryHostingModel() == APP_HOSTING_MODEL::HOSTING_OUT_PROCESS)
    {
        pApplication = new OUT_OF_PROCESS_APPLICATION(pServer, pConfig);
    }
    else
    {
        return HRESULT_FROM_WIN32(ERROR_NOT_SUPPORTED);
    }

    if (pApplication == NULL)
    {
        hr = HRESULT_FROM_WIN32(ERROR_OUTOFMEMORY);
    }
    else
    {
        *ppApplication = pApplication;
    }
    return hr;
}

HRESULT
__stdcall
CreateRequestHandler(
    _In_  IHttpContext       *pHttpContext,
    _In_  ASPNETCORE_CONFIG  *pConfig,
    _In_  APPLICATION        *pApplication,
    _Out_ REQUEST_HANDLER   **pRequestHandler
)
{
    HRESULT hr = S_OK;
    REQUEST_HANDLER* pHandler = NULL;
    if (pConfig->QueryHostingModel() == APP_HOSTING_MODEL::HOSTING_IN_PROCESS)
    {
        pHandler = new IN_PROCESS_HANDLER(pHttpContext, pApplication);
    }
    else if (pConfig->QueryHostingModel() == APP_HOSTING_MODEL::HOSTING_OUT_PROCESS)
    {
        pHandler = new FORWARDING_HANDLER(pHttpContext, pApplication);
    }
    else
    {
        return HRESULT_FROM_WIN32(ERROR_NOT_SUPPORTED);
    }

    if (pHandler == NULL)
    {
        hr = HRESULT_FROM_WIN32(ERROR_OUTOFMEMORY);
    }
    else
    {
        *pRequestHandler = pHandler;
    }
    return hr;
}
