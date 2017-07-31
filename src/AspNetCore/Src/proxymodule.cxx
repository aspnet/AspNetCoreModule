// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

#include "precomp.hxx"


__override
HRESULT
CProxyModuleFactory::GetHttpModule(
    CHttpModule **      ppModule,
    IModuleAllocator *  pAllocator
)
{
    CProxyModule *pModule = new (pAllocator) CProxyModule();
    if (pModule == NULL)
    {
        return E_OUTOFMEMORY;
    }

    *ppModule = pModule;
    return S_OK;
}

__override
VOID
CProxyModuleFactory::Terminate(
    VOID
)
/*++

Routine description:

    Function called by IIS for global (non-request-specific) notifications

Arguments:

    None.

Return value:

    None

--*/
{
    FORWARDING_HANDLER::StaticTerminate();

    WEBSOCKET_HANDLER::StaticTerminate();

    if (g_pResponseHeaderHash != NULL)
    {
        g_pResponseHeaderHash->Clear();
        delete g_pResponseHeaderHash;
        g_pResponseHeaderHash = NULL;
    }

    ALLOC_CACHE_HANDLER::StaticTerminate();

    delete this;
}

CProxyModule::CProxyModule(
) : m_pHandler(NULL)
{
}

CProxyModule::~CProxyModule()
{
    if (m_pHandler != NULL)
    {
        m_pHandler->DereferenceForwardingHandler();
        m_pHandler = NULL;
    }
}

__override
REQUEST_NOTIFICATION_STATUS
CProxyModule::OnExecuteRequestHandler(
    IHttpContext *          pHttpContext,
    IHttpEventProvider *
)
{
    HRESULT hr;
    APPLICATION_MANAGER* pApplicationManager;
    APPLICATION* pApplication;

    pApplicationManager = APPLICATION_MANAGER::GetInstance();
    if (pApplicationManager == NULL)
    {
        hr = E_OUTOFMEMORY;
        goto Failed;
    }

    hr = pApplicationManager->GetApplication(pHttpContext,
        &pApplication);
    if (FAILED(hr))
    {
        goto Failed;
    }

    ASPNETCORE_CONFIG* config;
    ASPNETCORE_CONFIG::GetConfig(pHttpContext, &config);

    ASPNETCORE_APPLICATION* pAspNetCoreApplication;
    hr = pApplication->GetAspNetCoreApplication(config, &pAspNetCoreApplication);
    if (FAILED(hr))
    {
        goto Failed;
    }

    // Allow reading and writing to simultaneously
    ((IHttpContext3*)pHttpContext)->EnableFullDuplex();

    // Disable response buffering by default, we'll do a write behind buffering in managed code
    ((IHttpResponse2*)pHttpContext->GetResponse())->DisableBuffering();

    // TODO: Optimize sync completions
    pAspNetCoreApplication->ExecuteRequest(pHttpContext);
    return RQ_NOTIFICATION_PENDING;

Failed:
    pHttpContext->GetResponse()->SetStatus(500, "Internal Server Error", 0, E_APPLICATION_ACTIVATION_EXEC_FAILURE);
    return RQ_NOTIFICATION_FINISH_REQUEST;
}

__override
REQUEST_NOTIFICATION_STATUS
CProxyModule::OnAsyncCompletion(
    IHttpContext *,
    DWORD                   dwNotification,
    BOOL                    fPostNotification,
    IHttpEventProvider *,
    IHttpCompletionInfo *   pCompletionInfo
)
{
    return REQUEST_NOTIFICATION_STATUS::RQ_NOTIFICATION_CONTINUE;
}