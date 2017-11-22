#include "stdafx.h"

HANDLE   REQUEST_HANDLER::sm_hEventLog = NULL;

REQUEST_HANDLER::REQUEST_HANDLER(
    _In_ IHttpContext *pW3Context,
    _In_ APPLICATION  *pApplication)
    : m_cRefs(1)
{
    //InitializeSRWLock(&m_srwLock);
    
    m_pW3Context = pW3Context;
    m_pApplication = pApplication;
}


REQUEST_HANDLER::~REQUEST_HANDLER()
{
}

VOID
REQUEST_HANDLER::ReferenceRequestHandler(
    VOID
) const
{
    InterlockedIncrement(&m_cRefs);
}


VOID
REQUEST_HANDLER::DereferenceRequestHandler(
    VOID
) const
{
    DBG_ASSERT(m_cRefs != 0);

    LONG cRefs = 0;
    if ((cRefs = InterlockedDecrement(&m_cRefs)) == 0)
    {
        delete this;
    }
}

// static 
HRESULT
REQUEST_HANDLER::StaticInitialize(
    IHttpServer* pServer
)
{
    if (pServer->IsCommandLineLaunch())
    {
        sm_hEventLog = RegisterEventSource(NULL, ASPNETCORE_IISEXPRESS_EVENT_PROVIDER);
    }
    else
    {
        sm_hEventLog = RegisterEventSource(NULL, ASPNETCORE_EVENT_PROVIDER);
    }
    return S_OK;
}

