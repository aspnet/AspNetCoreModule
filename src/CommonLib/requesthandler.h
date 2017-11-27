// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

#pragma once
//#ifdef REQUESTHANDLER_EXPORTS
//#  define EXPORT __declspec(dllexport)
//#else
//#  define EXPORT __declspec(dllimport)
//#endif
#include "stdafx.h"
#include "application.h"
//
// Abstract class
//
class REQUEST_HANDLER
{
public:
    REQUEST_HANDLER(
        _In_ IHttpContext *pW3Context,
        _In_ APPLICATION  *pApplication
    );

    virtual
    REQUEST_NOTIFICATION_STATUS
    OnExecuteRequestHandler() = 0;

    virtual
    REQUEST_NOTIFICATION_STATUS
    OnAsyncCompletion(
        DWORD      cbCompletion,
        HRESULT    hrCompletionStatus
    ) = 0;

    virtual
    ~REQUEST_HANDLER(
        VOID
    );

    VOID
    ReferenceRequestHandler(
        VOID
    ) const;

    VOID
    DereferenceRequestHandler(
        VOID
    ) const;

    static
    HANDLE
    QueryEventLog()
    {
        return sm_hEventLog;
    }

    static
    HRESULT
    StaticInitialize(
        IHttpServer* pServer
    );

protected:
    mutable LONG    m_cRefs;
    IHttpContext*   m_pW3Context;
    APPLICATION*    m_pApplication;
    static HANDLE   sm_hEventLog;
};