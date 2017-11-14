#pragma once
#include"stdafx.h"
#include "aspnetcoreconfig.h"

enum APPLICATION_STATUS
{
    UNKNOWN = 0,
    RUNNING,
    FAUL
};

class APPLICATION
{
public:
    APPLICATION(
        _In_ IHttpServer* pHttpServer, 
        _In_ ASPNETCORE_CONFIG* pConfig);

    virtual
    VOID
    ShutDown() = 0;

    virtual
    ~APPLICATION();

    APPLICATION_STATUS
    QueryStatus();

    VOID
    ReferenceApplication()
    const;

    VOID
    DereferenceApplication()
    const;

protected:
    mutable LONG            m_cRefs;
    APPLICATION_STATUS      m_status;
    IHttpServer*            m_pHttpServer;
    ASPNETCORE_CONFIG*      m_pConfig;
};