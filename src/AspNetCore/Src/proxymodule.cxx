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

    // TODO: Optimize sync completions
    pAspNetCoreApplication->ExecuteRequest(pHttpContext);
    return RQ_NOTIFICATION_PENDING;

Failed:
    pHttpContext->GetResponse()->SetStatus(500, "Internal Server Error", 0, E_APPLICATION_ACTIVATION_EXEC_FAILURE);
    return RQ_NOTIFICATION_FINISH_REQUEST;

    /*
    m_pHandler = new FORWARDING_HANDLER(pHttpContext);
    if (m_pHandler == NULL)
    {
        pHttpContext->GetResponse()->SetStatus(500, "Internal Server Error", 0, E_OUTOFMEMORY);
        return RQ_NOTIFICATION_FINISH_REQUEST;
    }

    return m_pHandler->OnExecuteRequestHandler();*/
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
    // This shouldn't be called
    return REQUEST_NOTIFICATION_STATUS::RQ_NOTIFICATION_FINISH_REQUEST;
    /*UNREFERENCED_PARAMETER(dwNotification);
    UNREFERENCED_PARAMETER(fPostNotification);
    DBG_ASSERT(dwNotification == RQ_EXECUTE_REQUEST_HANDLER);
    DBG_ASSERT(fPostNotification == FALSE);

    return m_pHandler->OnAsyncCompletion(
        pCompletionInfo->GetCompletionBytes(),
        pCompletionInfo->GetCompletionStatus());*/
}