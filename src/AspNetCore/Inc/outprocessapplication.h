// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

#pragma once

#include "application.h"

class OUTPROCESS_APPLICATION : public APPLICATION
{
public:
    OUTPROCESS_APPLICATION();

    ~OUTPROCESS_APPLICATION();

    __override
    HRESULT Initialize(_In_ APPLICATION_MANAGER* pApplicationManager,
                       _In_ ASPNETCORE_CONFIG*   pConfiguration);

    __override
    VOID OnAppOfflineHandleChange();

    __override
   REQUEST_NOTIFICATION_STATUS
   ExecuteRequest(
       _In_ IHttpContext* pHttpContext
   );

    HRESULT
    GetProcess(
        _In_    IHttpContext          *context,
        _Out_   SERVER_PROCESS       **ppServerProcess
    )
    {
        return m_pProcessManager->GetProcess(context, m_pConfiguration, ppServerProcess);
    }

private:
 /*   HRESULT
    GetHeaders(
        FORWARDING_HANDLER *    pForwardingHandler,
        const PROTOCOL_CONFIG * pProtocol,
        PCWSTR *                ppszHeaders,
        DWORD *                 pcchHeaders,
        ASPNETCORE_CONFIG*      pAspNetCoreConfig,
        SERVER_PROCESS*         pServerProcess
    );

    HRESULT
    CreateWinHttpRequest(
        __in const FORWARDING_HANDLER * pForwardingHandler,
        __in const PROTOCOL_CONFIG *    pProtocol,
        __in HINTERNET                  hConnect,
        __inout STRU *                  pstrUrl,
        ASPNETCORE_CONFIG*              pAspNetCoreConfig,
        SERVER_PROCESS*                 pServerProcess
    );*/

    PROCESS_MANAGER*        m_pProcessManager;
};
