// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

#pragma once

typedef void(*request_handler_cb) (int error, IHttpContext* pHttpContext, void* pvCompletionContext);
typedef REQUEST_NOTIFICATION_STATUS(*PFN_REQUEST_HANDLER) (IHttpContext* pHttpContext, void* pvRequstHandlerContext);
typedef BOOL(*PFN_SHUTDOWN_HANDLER) (void* pvShutdownHandlerContext);

#include "application.h"

class INPROCESS_APPLICATION : public APPLICATION
{
public:
    INPROCESS_APPLICATION();

    ~INPROCESS_APPLICATION();

    __override
    HRESULT
    Initialize(_In_ APPLICATION_MANAGER* pApplicationManager,
               _In_ ASPNETCORE_CONFIG*   pConfiguration);

    VOID
    Recycle(
        VOID
    );

    __override
    VOID OnAppOfflineHandleChange();

    __override
    REQUEST_NOTIFICATION_STATUS
    ExecuteRequest(
        _In_ IHttpContext* pHttpContext
    );

    VOID
    SetCallbackHandles(
        _In_ PFN_REQUEST_HANDLER request_callback,
        _In_ PFN_SHUTDOWN_HANDLER shutdown_callback,
        _In_ VOID* pvRequstHandlerContext,
        _In_ VOID* pvShutdownHandlerContext
    );

    // Executes the .NET Core process
    HRESULT
    ExecuteApplication(
        VOID
    );

    HRESULT
    LoadManagedApplication(
            VOID
        );

    static
    INPROCESS_APPLICATION*
    GetInstance(
        VOID
    )
    {
        return s_Application;
    }


private:
    // Thread executing the .NET Core process
    HANDLE                          m_hThread;

    // The request handler callback from managed code
    PFN_REQUEST_HANDLER             m_RequestHandler;
    VOID*                           m_RequstHandlerContext;

    // The shutdown handler callback from managed code
    PFN_SHUTDOWN_HANDLER            m_ShutdownHandler;
    VOID*                           m_ShutdownHandlerContext;

    // The event that gets triggered when managed initialization is complete
    HANDLE                          m_pInitalizeEvent;

    // The exit code of the .NET Core process
    INT                             m_ProcessExitCode;

    BOOL                            m_fManagedAppLoaded;
    BOOL                            m_fLoadManagedAppError;
    BOOL                            m_fRecycleCalled;

    static INPROCESS_APPLICATION*   s_Application;

    static
    VOID
    FindDotNetFolders(
        _In_ STRU *pstrPath,   //todo -- this may not need to be stru, can be PCWSTR
        _Out_ std::vector<std::wstring> *pvFolders
    );

    static
    HRESULT
    FindHighestDotNetVersion(
        _In_ std::vector<std::wstring> vFolders,
        _Out_ STRU *pstrResult
    );

    static
    BOOL
    DirectoryExists(
    _In_ STRU *pstrPath  //todo: this does not need to be stru, can be PCWSTR
    );

    static BOOL
    GetEnv(
        _In_ PCWSTR pszEnvironmentVariable,
        _Out_ STRU *pstrResult
    );

    static
    VOID
    ExecuteAspNetCoreProcess(
        _In_ LPVOID pContext
    );

};