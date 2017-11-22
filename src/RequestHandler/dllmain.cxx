// dllmain.cpp : Defines the entry point for the DLL application.
#include "precomp.hxx"
#include <IPHlpApi.h>
#include <VersionHelpers.h>

BOOL                g_fNsiApiNotSupported = FALSE;
BOOL                g_fWebSocketSupported = FALSE;
BOOL                g_fEnableReferenceCountTracing = FALSE;
DWORD               g_OptionalWinHttpFlags = 0;
DWORD               g_dwAspNetCoreDebugFlags = 0;
DWORD               g_dwDebugFlags = 0;

VOID
InitializeGlobalConfiguration(
    VOID
)
{
    HKEY hKey;
    OSVERSIONINFO osvi;

    if (RegOpenKeyEx(HKEY_LOCAL_MACHINE,
        L"SOFTWARE\\Microsoft\\IIS Extensions\\IIS AspNetCore Module\\Parameters",
        0,
        KEY_READ,
        &hKey) == NO_ERROR)
    {
        DWORD dwType;
        DWORD dwData;
        DWORD cbData;

        cbData = sizeof(dwData);
        if ((RegQueryValueEx(hKey,
            L"OptionalWinHttpFlags",
            NULL,
            &dwType,
            (LPBYTE)&dwData,
            &cbData) == NO_ERROR) &&
            (dwType == REG_DWORD))
        {
            g_OptionalWinHttpFlags = dwData;
        }

        cbData = sizeof(dwData);
        if ((RegQueryValueEx(hKey,
            L"EnableReferenceCountTracing",
            NULL,
            &dwType,
            (LPBYTE)&dwData,
            &cbData) == NO_ERROR) &&
            (dwType == REG_DWORD) && (dwData == 1 || dwData == 0))
        {
            g_fEnableReferenceCountTracing = !!dwData;
        }

        cbData = sizeof(dwData);
        if ((RegQueryValueEx(hKey,
            L"DebugFlags",
            NULL,
            &dwType,
            (LPBYTE)&dwData,
            &cbData) == NO_ERROR) &&
            (dwType == REG_DWORD))
        {
            g_dwAspNetCoreDebugFlags = dwData;
        }

        RegCloseKey(hKey);
    }

    DWORD dwSize = 0;
    DWORD dwResult = GetExtendedTcpTable(NULL,
        &dwSize,
        FALSE,
        AF_INET,
        TCP_TABLE_OWNER_PID_LISTENER,
        0);
    if (dwResult != NO_ERROR && dwResult != ERROR_INSUFFICIENT_BUFFER)
    {
        g_fNsiApiNotSupported = TRUE;
    }

    // WebSocket is supported on Win8 and above only
    // todo: test on win7
    g_fWebSocketSupported = IsWindows8OrGreater();

}

BOOL APIENTRY DllMain(HMODULE hModule,
    DWORD  ul_reason_for_call,
    LPVOID lpReserved
)
{
    switch (ul_reason_for_call)
    {
    case DLL_PROCESS_ATTACH:
        DisableThreadLibraryCalls(hModule);
        // Initialze some global variables here
        InitializeGlobalConfiguration();
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
    REQUEST_HANDLER::StaticInitialize(pServer);

    if (pConfig->QueryHostingModel() == APP_HOSTING_MODEL::HOSTING_IN_PROCESS)
    {
        pApplication = new IN_PROCESS_APPLICATION(pServer, pConfig);
    }
    else if (pConfig->QueryHostingModel() == APP_HOSTING_MODEL::HOSTING_OUT_PROCESS)
    {
        pApplication = new OUT_OF_PROCESS_APPLICATION(pServer, pConfig);
        hr = ((OUT_OF_PROCESS_APPLICATION*)pApplication)->Initialize();
        if (FAILED(hr))
        {
            delete pApplication;
            pApplication = NULL;
            goto Finished;
        }
    }
    else
    {
        hr = HRESULT_FROM_WIN32(ERROR_NOT_SUPPORTED);
        goto Finished;
    }

    if (pApplication == NULL)
    {
        hr = HRESULT_FROM_WIN32(ERROR_OUTOFMEMORY);
        goto Finished;
    }
    else
    {
        *ppApplication = pApplication;
    }

Finished:
    return hr;
}

HRESULT
__stdcall
CreateRequestHandler(
    _In_  IHttpContext       *pHttpContext,
    _In_  APPLICATION        *pApplication,
    _Out_ REQUEST_HANDLER   **pRequestHandler
)
{
    HRESULT hr = S_OK;
    REQUEST_HANDLER* pHandler = NULL;
    ASPNETCORE_CONFIG* pConfig = pApplication->QueryConfig();
    DBG_ASSERT(pConfig);

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

