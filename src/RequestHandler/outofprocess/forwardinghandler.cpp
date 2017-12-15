#include "..\precomp.hxx"

// Just to be aware of the FORWARDING_HANDLER object size.
C_ASSERT(sizeof(FORWARDING_HANDLER) <= 632);

#define DEF_MAX_FORWARDS        32
#define HEX_TO_ASCII(c) ((CHAR)(((c) < 10) ? ((c) + '0') : ((c) + 'a' - 10)))
#define BUFFER_SIZE         (8192UL)
#define ENTITY_BUFFER_SIZE  (6 + BUFFER_SIZE + 2)

#define FORWARDING_HANDLER_SIGNATURE        ((DWORD)'FHLR')
#define FORWARDING_HANDLER_SIGNATURE_FREE   ((DWORD)'fhlr')

STRA                        FORWARDING_HANDLER::sm_pStra502ErrorMsg;
ALLOC_CACHE_HANDLER *       FORWARDING_HANDLER::sm_pAlloc = NULL;
TRACE_LOG *                 FORWARDING_HANDLER::sm_pTraceLog = NULL;
PROTOCOL_CONFIG             FORWARDING_HANDLER::sm_ProtocolConfig;
RESPONSE_HEADER_HASH *      FORWARDING_HANDLER::sm_pResponseHeaderHash = NULL;

FORWARDING_HANDLER::FORWARDING_HANDLER(
    _In_ IHttpContext     *pW3Context,
    _In_  HTTP_MODULE_ID  *pModuleId,
    _In_ APPLICATION      *pApplication
) : REQUEST_HANDLER(pW3Context, pModuleId, pApplication),
    m_Signature(FORWARDING_HANDLER_SIGNATURE),
    m_RequestStatus(FORWARDER_START),
    m_fHandleClosedDueToClient(FALSE),
    m_fResponseHeadersReceivedAndSet(FALSE),
    m_fDoReverseRewriteHeaders(FALSE),
    m_fFinishRequest(FALSE),
    m_fHasError(FALSE),
    m_pszHeaders(NULL),
    m_cchHeaders(0),
    m_BytesToReceive(0),
    m_BytesToSend(0),
    m_fWebSocketEnabled(FALSE),
    m_pWebSocket(NULL)
{
    DebugPrintf(ASPNETCORE_DEBUG_FLAG_INFO,
        "FORWARDING_HANDLER::FORWARDING_HANDLER");
}

FORWARDING_HANDLER::~FORWARDING_HANDLER(
)
{
    //
    // Destructor has started.
    //
    m_Signature = FORWARDING_HANDLER_SIGNATURE_FREE;

    DebugPrintf(ASPNETCORE_DEBUG_FLAG_INFO,
        "FORWARDING_HANDLER::~FORWARDING_HANDLER");
    //
    // RemoveRequest() should already have been called and m_pDisconnect
    // has been freed or m_pDisconnect was never initialized.
    //
    // Disconnect notification cleanup would happen first, before
    // the FORWARDING_HANDLER instance got removed from m_pSharedhandler list.
    // The m_pServer cleanup would happen afterwards, since there may be a 
    // call pending from SHARED_HANDLER to  FORWARDING_HANDLER::SetStatusAndHeaders()
    // 
    DBG_ASSERT(m_pDisconnect == NULL);

    if (m_pDisconnect != NULL)
    {
        m_pDisconnect->ResetHandler();
        m_pDisconnect = NULL;
    }

    FreeResponseBuffers();

    if (m_pWebSocket)
    {
        m_pWebSocket->Terminate();
        m_pWebSocket = NULL;
    }
}

__override
REQUEST_NOTIFICATION_STATUS
FORWARDING_HANDLER::OnExecuteRequestHandler()
{
    REQUEST_NOTIFICATION_STATUS retVal = RQ_NOTIFICATION_CONTINUE;
    HRESULT                     hr = S_OK;
    bool                        fRequestLocked = FALSE;
    bool                        fHandleSet = FALSE;
    bool                        fFailedToStartKestrel = FALSE;
    BOOL                        fSecure = FALSE;
    HINTERNET                   hConnect = NULL;
    IHttpRequest               *pRequest = m_pW3Context->GetRequest();
    IHttpResponse              *pResponse = m_pW3Context->GetResponse();
    IHttpConnection            *pClientConnection = NULL;
    OUT_OF_PROCESS_APPLICATION *pApplication = NULL;
    PROTOCOL_CONFIG            *pProtocol = &sm_ProtocolConfig;
    SERVER_PROCESS             *pServerProcess = NULL;

    USHORT                      cchHostName = 0;

    STACK_STRU(strDestination, 32);
    STACK_STRU(strUrl, 2048);
    STACK_STRU(struEscapedUrl, 2048);

    // override Protocol related config from aspNetCore config
    pProtocol->OverrideConfig(m_pApplication->QueryConfig());

    // check connection
    pClientConnection = m_pW3Context->GetConnection();
    if (pClientConnection == NULL ||
        !pClientConnection->IsConnected())
    {
        hr = HRESULT_FROM_WIN32(WSAECONNRESET);
        goto Failure;
    }

    pApplication = static_cast<OUT_OF_PROCESS_APPLICATION*> (m_pApplication);
    if (pApplication == NULL)
    {
        hr = E_INVALIDARG;
        goto Finished;
    }

    hr = pApplication->GetProcess(&pServerProcess);
    if (FAILED(hr))
    {
        fFailedToStartKestrel = TRUE;
        goto Failure;
    }

    if (pServerProcess == NULL)
    {
        fFailedToStartKestrel = TRUE;
        hr = HRESULT_FROM_WIN32(ERROR_CREATE_FAILED);
        goto Failure;
    }

    if (pServerProcess->QueryWinHttpConnection() == NULL)
    {
        hr = HRESULT_FROM_WIN32(ERROR_INVALID_HANDLE);
        goto Failure;
    }

    hConnect = pServerProcess->QueryWinHttpConnection()->QueryHandle();

    m_pszOriginalHostHeader = pRequest->GetHeader(HttpHeaderHost, &cchHostName);
    //
    // parse original url
    //
    if (FAILED(hr = UTILITY::SplitUrl(pRequest->GetRawHttpRequest()->CookedUrl.pFullUrl,
        &fSecure,
        &strDestination,
        &strUrl)))
    {
        goto Failure;
    }

    if (FAILED(hr = UTILITY::EscapeAbsPath(pRequest, &struEscapedUrl)))
    {
        goto Failure;
    }

    m_fDoReverseRewriteHeaders = pProtocol->QueryReverseRewriteHeaders();

    m_cMinBufferLimit = pProtocol->QueryMinResponseBuffer();

    //
    // Mark request as websocket if upgrade header is present.
    //
    if (g_fWebSocketSupported)
    {
        USHORT cchHeader = 0;
        PCSTR pszWebSocketHeader = pRequest->GetHeader("Upgrade", &cchHeader);
        if (cchHeader == 9 && _stricmp(pszWebSocketHeader, "websocket") == 0)
        {
            m_fWebSocketEnabled = TRUE;
        }
    }

    hr = CreateWinHttpRequest(pRequest,
        pProtocol,
        hConnect,
        &struEscapedUrl,
        pServerProcess);
    if (FAILED(hr))
    {
        goto Failure;
    }

    // Set client disconnect callback contract with IIS
    m_pDisconnect = static_cast<ASYNC_DISCONNECT_CONTEXT *>(
        pClientConnection->GetModuleContextContainer()->
        GetConnectionModuleContext(m_pModuleId));
    if (m_pDisconnect == NULL)
    {
        m_pDisconnect = new ASYNC_DISCONNECT_CONTEXT();
        if (m_pDisconnect == NULL)
        {
            hr = E_OUTOFMEMORY;
            goto Failure;
        }

        hr = pClientConnection->GetModuleContextContainer()->
            SetConnectionModuleContext(m_pDisconnect,
                m_pModuleId);
        DBG_ASSERT(hr != HRESULT_FROM_WIN32(ERROR_ALREADY_ASSIGNED));
        if (FAILED(hr))
        {
            goto Failure;
        }
    }

    m_pDisconnect->SetHandler(this);
    fHandleSet = TRUE;

    // require lock as client disconnect callback may happen
    AcquireSRWLockShared(&m_RequestLock);
    fRequestLocked = TRUE;

    //
    // Remember the handler being processed in the current thread
    // before staring a WinHTTP operation.
    //
    DBG_ASSERT(fRequestLocked);
    DBG_ASSERT(TlsGetValue(g_dwTlsIndex) == NULL);
    TlsSetValue(g_dwTlsIndex, this);
    DBG_ASSERT(TlsGetValue(g_dwTlsIndex) == this);

    if (m_hRequest == NULL)
    {
        hr = HRESULT_FROM_WIN32(WSAECONNRESET);
        goto Failure;
    }

    //
    // Begins normal request handling. Send request to server.
    //
    m_RequestStatus = FORWARDER_SENDING_REQUEST;

    //
    // Calculate the bytes to receive from the content length.
    //
    DWORD cbContentLength = 0;
    PCSTR pszContentLength = pRequest->GetHeader(HttpHeaderContentLength);
    if (pszContentLength != NULL)
    {
        cbContentLength = m_BytesToReceive = atol(pszContentLength);
        if (m_BytesToReceive == INFINITE)
        {
            hr = HRESULT_FROM_WIN32(WSAECONNRESET);
            goto Failure;
        }
    }
    else if (pRequest->GetHeader(HttpHeaderTransferEncoding) != NULL)
    {
        m_BytesToReceive = INFINITE;
    }

    if (m_fWebSocketEnabled)
    {
        //
        // Set the upgrade flag for a websocket request.
        //
        if (!WinHttpSetOption(m_hRequest,
            WINHTTP_OPTION_UPGRADE_TO_WEB_SOCKET,
            NULL,
            0))
        {
            hr = HRESULT_FROM_WIN32(GetLastError());
            goto Finished;
        }
    }

    m_cchLastSend = m_cchHeaders;

    //FREB log
    if (ANCMEvents::ANCM_REQUEST_FORWARD_START::IsEnabled(m_pW3Context->GetTraceContext()))
    {
        ANCMEvents::ANCM_REQUEST_FORWARD_START::RaiseEvent(
            m_pW3Context->GetTraceContext(),
            NULL);
    }

    if (!WinHttpSendRequest(m_hRequest,
        m_pszHeaders,
        m_cchHeaders,
        NULL,
        0,
        cbContentLength,
        reinterpret_cast<DWORD_PTR>(static_cast<PVOID>(this))))
    {
        hr = HRESULT_FROM_WIN32(GetLastError());
        DebugPrintf(ASPNETCORE_DEBUG_FLAG_INFO,
            "FORWARDING_HANDLER::OnExecuteRequestHandler, Send request failed");

        // FREB log
        if (ANCMEvents::ANCM_REQUEST_FORWARD_FAIL::IsEnabled(m_pW3Context->GetTraceContext()))
        {
            ANCMEvents::ANCM_REQUEST_FORWARD_FAIL::RaiseEvent(
                m_pW3Context->GetTraceContext(),
                NULL,
                hr);
        }

        goto Failure;
    }

    //
    // Async WinHTTP operation is in progress. Release this thread meanwhile,
    // OnWinHttpCompletion method should resume the work by posting an IIS completion.
    //
    retVal = RQ_NOTIFICATION_PENDING;
    goto Finished;

Failure:
    m_RequestStatus = FORWARDER_DONE;

    //disbale client disconnect callback
    if (m_pDisconnect != NULL)
    {
        if (fHandleSet)
        {
            m_pDisconnect->ResetHandler();
        }
        m_pDisconnect->CleanupStoredContext();
        m_pDisconnect = NULL;
    }

    pResponse->DisableKernelCache();
    pResponse->GetRawHttpResponse()->EntityChunkCount = 0;
    if (hr == HRESULT_FROM_WIN32(WSAECONNRESET))
    {
        pResponse->SetStatus(400, "Bad Request", 0, hr);
    }
    else
    {
        HTTP_DATA_CHUNK   DataChunk;
        pResponse->SetStatus(502, "Bad Gateway", 5, hr, NULL, TRUE);
        pResponse->SetHeader("Content-Type",
            "text/html",
            (USHORT)strlen("text/html"),
            FALSE
        );

        DataChunk.DataChunkType = HttpDataChunkFromMemory;
        DataChunk.FromMemory.pBuffer = (PVOID)sm_pStra502ErrorMsg.QueryStr();
        DataChunk.FromMemory.BufferLength = sm_pStra502ErrorMsg.QueryCB();
        pResponse->WriteEntityChunkByReference(&DataChunk);
    }
    //
    // Finish the request on failure.
    //
    retVal = RQ_NOTIFICATION_FINISH_REQUEST;

Finished:
    if (fRequestLocked)
    {
        DBG_ASSERT(TlsGetValue(g_dwTlsIndex) == this);
        TlsSetValue(g_dwTlsIndex, NULL);
        ReleaseSRWLockShared(&m_RequestLock);
        DBG_ASSERT(TlsGetValue(g_dwTlsIndex) == NULL);
    }
    return retVal;
}

__override
REQUEST_NOTIFICATION_STATUS
FORWARDING_HANDLER::OnAsyncCompletion(
    DWORD           cbCompletion,
    HRESULT         hrCompletionStatus
)
/*++

Routine Description:

Handle the completion from IIS and continue the execution
of this request based on the current state.

Arguments:

cbCompletion - Number of bytes associated with this completion
dwCompletionStatus - the win32 status associated with this completion

Return Value:

REQUEST_NOTIFICATION_STATUS

--*/
{
    HRESULT                     hr = S_OK;
    REQUEST_NOTIFICATION_STATUS retVal = RQ_NOTIFICATION_PENDING;
    bool                        fLocked = FALSE;
    bool                        fClientError = FALSE;
    bool                        fClosed = FALSE;

    DBG_ASSERT(m_pW3Context != NULL);
    __analysis_assume(m_pW3Context != NULL);

    //
    // Take a reference so that object does not go away as a result of
    // async completion.
    //
    ReferenceRequestHandler();

    if (sm_pTraceLog != NULL)
    {
        WriteRefTraceLogEx(sm_pTraceLog,
            m_cRefs,
            this,
            "FORWARDING_HANDLER::OnAsyncCompletion Enter",
            reinterpret_cast<PVOID>(static_cast<DWORD_PTR>(cbCompletion)),
            reinterpret_cast<PVOID>(static_cast<DWORD_PTR>(hrCompletionStatus)));
    }

    if (TlsGetValue(g_dwTlsIndex) != this)
    {
        DBG_ASSERT(TlsGetValue(g_dwTlsIndex) == NULL);
        AcquireSRWLockShared(&m_RequestLock);
        TlsSetValue(g_dwTlsIndex, this);
        DBG_ASSERT(TlsGetValue(g_dwTlsIndex) == this);

        fLocked = TRUE;
    }

    if (m_hRequest == NULL)
    {
        // Request is Done
        if (m_fFinishRequest)
        {
            if (m_fHasError)
            {
                retVal = RQ_NOTIFICATION_FINISH_REQUEST;
            }
            else
            {
                retVal = RQ_NOTIFICATION_CONTINUE;
            }
            goto Finished;
        }

        fClientError = m_fHandleClosedDueToClient;
        goto Failure;
    }

    //
    // Begins normal completion handling. There is already a shared acquired
    // for protecting the WinHTTP request handle from being closed.
    //
    switch (m_RequestStatus)
    {
    case FORWARDER_RECEIVED_WEBSOCKET_RESPONSE:
        DebugPrintf(ASPNETCORE_DEBUG_FLAG_INFO,
            "FORWARDING_HANDLER::OnAsyncCompletion, Send completed for 101 response");
        //
        // This should be the write completion of the 101 response.
        //
        m_pWebSocket = new WEBSOCKET_HANDLER();
        if (m_pWebSocket == NULL)
        {
            hr = E_OUTOFMEMORY;
            goto Finished;
        }

        hr = m_pWebSocket->ProcessRequest(this, m_pW3Context, m_hRequest);
        if (FAILED(hr))
        {
            goto Failure;
        }

        //
        // WebSocket upgrade is successful. Close the WinHttpRequest Handle
        //
        fClosed = WinHttpCloseHandle(m_hRequest);

        DBG_ASSERT(fClosed);
        if (fClosed)
        {
            m_hRequest = NULL;
        }
        else
        {
            hr = HRESULT_FROM_WIN32(GetLastError());
            goto Failure;
        }
        break;

    case FORWARDER_RECEIVING_RESPONSE:
        //
        // This is a completion of a write (send) to http.sys, abort in case of
        // failure, if there is more data available from WinHTTP, read it
        // or else ask if there is more.
        //
        if (FAILED(hrCompletionStatus))
        {
            hr = hrCompletionStatus;
            fClientError = TRUE;
            goto Failure;
        }

        hr = OnReceivingResponse();
        if (FAILED(hr))
        {
            goto Failure;
        }
        break;

    case FORWARDER_SENDING_REQUEST:
        hr = OnSendingRequest(cbCompletion,
            hrCompletionStatus,
            &fClientError);
        if (FAILED(hr))
        {
            goto Failure;
        }
        break;

    default:
        DBG_ASSERT(m_RequestStatus == FORWARDER_DONE);
        goto Finished;
    }

    //
    // Either OnReceivingResponse or OnSendingRequest initiated an
    // async WinHTTP operation, release this thread meanwhile,
    // OnWinHttpCompletion method should resume the work by posting an IIS completion.
    //
    retVal = RQ_NOTIFICATION_PENDING;
    goto Finished;

Failure:

    m_fHasError = TRUE;
    m_RequestStatus = FORWARDER_DONE;

    //disbale client disconnect callback
    if (m_pDisconnect != NULL)
    {
        m_pDisconnect->ResetHandler();
        m_pDisconnect = NULL;
    }

    //
    // Do the right thing based on where the error originated from.
    //
    IHttpResponse *pResponse = m_pW3Context->GetResponse();
    pResponse->DisableKernelCache();
    pResponse->GetRawHttpResponse()->EntityChunkCount = 0;

    if (fClientError)
    {
        if (!m_fResponseHeadersReceivedAndSet)
        {
            pResponse->SetStatus(400, "Bad Request", 0, HRESULT_FROM_WIN32(WSAECONNRESET));
        }
        else
        {
            //
            // Response headers from origin server were
            // already received and set for the current response.
            // Honor the response status.
            //
        }
    }
    else
    {
        STACK_STRU(strDescription, 128);

        pResponse->SetStatus(502, "Bad Gateway", 3, hr);

//        if (!(hr > HRESULT_FROM_WIN32(WINHTTP_ERROR_BASE) &&
//            hr <= HRESULT_FROM_WIN32(WINHTTP_ERROR_LAST)) ||
//#pragma prefast (suppress : __WARNING_FUNCTION_NEEDS_REVIEW, "Function and parameters reviewed.")
//            FormatMessage(
//                FORMAT_MESSAGE_IGNORE_INSERTS | FORMAT_MESSAGE_FROM_HMODULE,
//                g_hWinHttpModule,
//                HRESULT_CODE(hr),
//                0,
//                strDescription.QueryStr(),
//                strDescription.QuerySizeCCH(),
//                NULL) == 0)
//        {
//            /*LoadString(g_hModule,
//                IDS_SERVER_ERROR,
//                strDescription.QueryStr(),
//                strDescription.QuerySizeCCH());*/
//        }
//
//        (VOID)strDescription.SyncWithBuffer();
//        if (strDescription.QueryCCH() != 0)
//        {
//            pResponse->SetErrorDescription(
//                strDescription.QueryStr(),
//                strDescription.QueryCCH(),
//                FALSE);
//        }
    }

    //
    // Finish the request on failure.
    // Let IIS pipeline continue only after receiving handle close callback
    // from WinHttp. This ensures no more callback from WinHttp
    //
    if (m_hRequest != NULL)
    {
        if (WinHttpCloseHandle(m_hRequest))
        {
            m_hRequest = NULL;
        }
        else
        {
            // Failed to close the handle 
            // which should never happen as we registered a callback with WinHttp
            // For this unexpected failure, let conitnue IIS pipeline
           /* retVal = RQ_NOTIFICATION_FINISH_REQUEST;
            DebugBreak();*/
        }
    }
    retVal = RQ_NOTIFICATION_PENDING;

Finished:
    if (fLocked)
    {
        DBG_ASSERT(TlsGetValue(g_dwTlsIndex) == this);
        TlsSetValue(g_dwTlsIndex, NULL);
        ReleaseSRWLockShared(&m_RequestLock);
        DBG_ASSERT(TlsGetValue(g_dwTlsIndex) == NULL);
    }

    DereferenceRequestHandler();
    //
    // No code after this point, as the handle might be gone
    //
    return retVal;
}

// static
HRESULT
FORWARDING_HANDLER::StaticInitialize(
    BOOL fEnableReferenceCountTracing
)
/*++

Routine Description:

Global initialization routine for FORWARDING_HANDLERs

Arguments:

fEnableReferenceCountTracing  - True if ref count tracing should be use.

Return Value:

HRESULT

--*/
{
    HRESULT                         hr = S_OK;

    sm_pAlloc = new ALLOC_CACHE_HANDLER;
    if (sm_pAlloc == NULL)
    {
        hr = E_OUTOFMEMORY;
        goto Finished;
    }

    hr = sm_pAlloc->Initialize(sizeof(FORWARDING_HANDLER),
        64); // nThreshold
    if (FAILED(hr))
    {
        goto Finished;
    }

    sm_pResponseHeaderHash = new RESPONSE_HEADER_HASH;
    if (sm_pResponseHeaderHash == NULL)
    {
        hr = E_OUTOFMEMORY;
        goto Finished;
    }

    hr = sm_pResponseHeaderHash->Initialize();
    if (FAILED(hr))
    {
        goto Finished;
    }

    // Initialize PROTOCOL_CONFIG
    hr = sm_ProtocolConfig.Initialize();
    if (FAILED(hr))
    {
        goto Finished;
    }

    if (fEnableReferenceCountTracing)
    {
        sm_pTraceLog = CreateRefTraceLog(10000, 0);
    }

    sm_pStra502ErrorMsg.Copy(
        "<!DOCTYPE html PUBLIC \"-//W3C//DTD XHTML 1.0 Strict//EN\" \"http://www.w3.org/TR/xhtml1/DTD/xhtml1-strict.dtd\"> \
        <html xmlns=\"http://www.w3.org/1999/xhtml\"> \
        <head> \
        <meta http-equiv=\"Content-Type\" content=\"text/html; charset=iso-8859-1\" /> \
        <title> IIS 502.5 Error </title><style type=\"text/css\"></style></head> \
        <body> <div id = \"content\"> \
          <div class = \"content-container\"><h3> HTTP Error 502.5 - Process Failure </h3></div>  \
          <div class = \"content-container\"> \
           <fieldset> <h4> Common causes of this issue: </h4> \
            <ul><li> The application process failed to start </li> \
             <li> The application process started but then stopped </li> \
             <li> The application process started but failed to listen on the configured port </li></ul></fieldset> \
          </div> \
          <div class = \"content-container\"> \
            <fieldset><h4> Troubleshooting steps: </h4> \
             <ul><li> Check the system event log for error messages </li> \
             <li> Enable logging the application process' stdout messages </li> \
             <li> Attach a debugger to the application process and inspect </li></ul></fieldset> \
             <fieldset><h4> For more information visit: \
             <a href=\"https://go.microsoft.com/fwlink/?linkid=808681\"> <cite> https://go.microsoft.com/fwlink/?LinkID=808681 </cite></a></h4> \
             </fieldset> \
          </div> \
       </div></body></html>");

Finished:
    if (FAILED(hr))
    {
        StaticTerminate();
    }
    return hr;
}

//static
VOID
FORWARDING_HANDLER::StaticTerminate()
{
    sm_pStra502ErrorMsg.Reset();

    if (sm_pResponseHeaderHash != NULL)
    {
        sm_pResponseHeaderHash->Clear();
        delete sm_pResponseHeaderHash;
        sm_pResponseHeaderHash = NULL;
    }

    if (sm_pTraceLog != NULL)
    {
        DestroyRefTraceLog(sm_pTraceLog);
        sm_pTraceLog = NULL;
    }

    if (sm_pAlloc != NULL)
    {
        delete sm_pAlloc;
        sm_pAlloc = NULL;
    }
}

HRESULT
FORWARDING_HANDLER::GetHeaders(
    _In_ const PROTOCOL_CONFIG *    pProtocol,
    _In_    bool                    fForwardWindowsAuthToken,
    _In_    SERVER_PROCESS*         pServerProcess,
    _Out_   PCWSTR *                ppszHeaders,
    _Inout_ DWORD *                 pcchHeaders
)
{
    HRESULT hr = S_OK;
    PCSTR pszCurrentHeader;
    PCSTR ppHeadersToBeRemoved;
    PCSTR pszFinalHeader;
    USHORT cchCurrentHeader;
    DWORD cchFinalHeader;
    BOOL  fSecure = FALSE;  // dummy. Used in SplitUrl. Value will not be used 
                            // as ANCM always use http protocol to communicate with backend  
    STRU  struDestination;
    STRU  struUrl;
    STACK_STRA(strTemp, 64);
    HTTP_REQUEST_HEADERS *pHeaders;
    IHttpRequest *pRequest = m_pW3Context->GetRequest();
    MULTISZA mszMsAspNetCoreHeaders;

    //
    // We historically set the host section in request url to the new host header
    // this is wrong but Kestrel has dependency on it.
    // should change it in the future
    //
    if (!pProtocol->QueryPreserveHostHeader())
    {
        if (FAILED(hr = UTILITY::SplitUrl(pRequest->GetRawHttpRequest()->CookedUrl.pFullUrl,
            &fSecure,
            &struDestination,
            &struUrl)) ||
            FAILED(hr = strTemp.CopyW(struDestination.QueryStr())) ||
            FAILED(hr = pRequest->SetHeader(HttpHeaderHost,
                strTemp.QueryStr(),
                static_cast<USHORT>(strTemp.QueryCCH()),
                TRUE))) // fReplace
        {
            return hr;
        }
    }
    //
    // Strip all headers starting with MS-ASPNETCORE.
    // These headers are generated by the asp.net core module and 
    // passed to the process it creates.
    //

    pHeaders = &m_pW3Context->GetRequest()->GetRawHttpRequest()->Headers;
    for (DWORD i = 0; i<pHeaders->UnknownHeaderCount; i++)
    {
        if (_strnicmp(pHeaders->pUnknownHeaders[i].pName, "MS-ASPNETCORE", 13) == 0)
        {
            mszMsAspNetCoreHeaders.Append(pHeaders->pUnknownHeaders[i].pName, (DWORD)pHeaders->pUnknownHeaders[i].NameLength);
        }
    }

    ppHeadersToBeRemoved = mszMsAspNetCoreHeaders.First();

    //
    // iterate the list of headers to be removed and delete them from the request.
    //

    while (ppHeadersToBeRemoved != NULL)
    {
        m_pW3Context->GetRequest()->DeleteHeader(ppHeadersToBeRemoved);
        ppHeadersToBeRemoved = mszMsAspNetCoreHeaders.Next(ppHeadersToBeRemoved);
    }

    if (pServerProcess->QueryGuid() != NULL)
    {
        hr = m_pW3Context->GetRequest()->SetHeader("MS-ASPNETCORE-TOKEN",
            pServerProcess->QueryGuid(),
            (USHORT)strlen(pServerProcess->QueryGuid()),
            TRUE);
        if (FAILED(hr))
        {
            return hr;
        }
    }

    if (fForwardWindowsAuthToken &&
        (_wcsicmp(m_pW3Context->GetUser()->GetAuthenticationType(), L"negotiate") == 0 ||
            _wcsicmp(m_pW3Context->GetUser()->GetAuthenticationType(), L"ntlm") == 0))
    {
        if (m_pW3Context->GetUser()->GetPrimaryToken() != NULL &&
            m_pW3Context->GetUser()->GetPrimaryToken() != INVALID_HANDLE_VALUE)
        {
            HANDLE hTargetTokenHandle = NULL;
            hr = pServerProcess->SetWindowsAuthToken(m_pW3Context->GetUser()->GetPrimaryToken(),
                &hTargetTokenHandle);
            if (FAILED(hr))
            {
                return hr;
            }

            //
            // set request header with target token value
            //
            CHAR pszHandleStr[16] = { 0 };
            if (_ui64toa_s((UINT64)hTargetTokenHandle, pszHandleStr, 16, 16) != 0)
            {
                hr = HRESULT_FROM_WIN32(ERROR_INVALID_DATA);
                return hr;
            }

            hr = m_pW3Context->GetRequest()->SetHeader("MS-ASPNETCORE-WINAUTHTOKEN",
                pszHandleStr,
                (USHORT)strlen(pszHandleStr),
                TRUE);
            if (FAILED(hr))
            {
                return hr;
            }
        }
    }

    if (!pProtocol->QueryXForwardedForName()->IsEmpty())
    {
        strTemp.Reset();

        pszCurrentHeader = pRequest->GetHeader(pProtocol->QueryXForwardedForName()->QueryStr(), &cchCurrentHeader);
        if (pszCurrentHeader != NULL)
        {
            if (FAILED(hr = strTemp.Copy(pszCurrentHeader, cchCurrentHeader)) ||
                FAILED(hr = strTemp.Append(", ", 2)))
            {
                return hr;
            }
        }

        if (FAILED(hr = m_pW3Context->GetServerVariable("REMOTE_ADDR",
            &pszFinalHeader,
            &cchFinalHeader)))
        {
            return hr;
        }

        if (pRequest->GetRawHttpRequest()->Address.pRemoteAddress->sa_family == AF_INET6)
        {
            if (FAILED(hr = strTemp.Append("[", 1)) ||
                FAILED(hr = strTemp.Append(pszFinalHeader, cchFinalHeader)) ||
                FAILED(hr = strTemp.Append("]", 1)))
            {
                return hr;
            }
        }
        else
        {
            if (FAILED(hr = strTemp.Append(pszFinalHeader, cchFinalHeader)))
            {
                return hr;
            }
        }

        if (pProtocol->QueryIncludePortInXForwardedFor())
        {
            if (FAILED(hr = m_pW3Context->GetServerVariable("REMOTE_PORT",
                &pszFinalHeader,
                &cchFinalHeader)))
            {
                return hr;
            }

            if (FAILED(hr = strTemp.Append(":", 1)) ||
                FAILED(hr = strTemp.Append(pszFinalHeader, cchFinalHeader)))
            {
                return hr;
            }
        }

        if (FAILED(hr = pRequest->SetHeader(pProtocol->QueryXForwardedForName()->QueryStr(),
            strTemp.QueryStr(),
            static_cast<USHORT>(strTemp.QueryCCH()),
            TRUE))) // fReplace
        {
            return hr;
        }
    }

    if (!pProtocol->QuerySslHeaderName()->IsEmpty())
    {
        const HTTP_SSL_INFO *pSslInfo = pRequest->GetRawHttpRequest()->pSslInfo;
        LPSTR pszScheme = "http";
        if (pSslInfo != NULL)
        {
            pszScheme = "https";
        }

        strTemp.Reset();

        pszCurrentHeader = pRequest->GetHeader(pProtocol->QuerySslHeaderName()->QueryStr(), &cchCurrentHeader);
        if (pszCurrentHeader != NULL)
        {
            if (FAILED(hr = strTemp.Copy(pszCurrentHeader, cchCurrentHeader)) ||
                FAILED(hr = strTemp.Append(", ", 2)))
            {
                return hr;
            }
        }

        if (FAILED(hr = strTemp.Append(pszScheme)))
        {
            return hr;
        }

        if (FAILED(pRequest->SetHeader(pProtocol->QuerySslHeaderName()->QueryStr(),
            strTemp.QueryStr(),
            (USHORT)strTemp.QueryCCH(),
            TRUE)))
        {
            return hr;
        }
    }

    if (!pProtocol->QueryClientCertName()->IsEmpty())
    {
        if (pRequest->GetRawHttpRequest()->pSslInfo == NULL ||
            pRequest->GetRawHttpRequest()->pSslInfo->pClientCertInfo == NULL)
        {
            pRequest->DeleteHeader(pProtocol->QueryClientCertName()->QueryStr());
        }
        else
        {
            // Resize the buffer large enough to hold the encoded certificate info
            if (FAILED(hr = strTemp.Resize(
                1 + (pRequest->GetRawHttpRequest()->pSslInfo->pClientCertInfo->CertEncodedSize + 2) / 3 * 4)))
            {
                return hr;
            }

            Base64Encode(
                pRequest->GetRawHttpRequest()->pSslInfo->pClientCertInfo->pCertEncoded,
                pRequest->GetRawHttpRequest()->pSslInfo->pClientCertInfo->CertEncodedSize,
                strTemp.QueryStr(),
                strTemp.QuerySize(),
                NULL);
            strTemp.SyncWithBuffer();

            if (FAILED(hr = pRequest->SetHeader(
                pProtocol->QueryClientCertName()->QueryStr(),
                strTemp.QueryStr(),
                static_cast<USHORT>(strTemp.QueryCCH()),
                TRUE))) // fReplace
            {
                return hr;
            }
        }
    }

    //
    // Remove the connection header
    //
    if (!m_fWebSocketEnabled)
    {
        pRequest->DeleteHeader(HttpHeaderConnection);
    }

    //
    // Get all the headers to send to the client
    //
    hr = m_pW3Context->GetServerVariable("ALL_RAW",
        ppszHeaders,
        pcchHeaders);
    if (FAILED(hr))
    {
        return hr;
    }

    return S_OK;
}

HRESULT
FORWARDING_HANDLER::CreateWinHttpRequest(
    _In_ const IHttpRequest *       pRequest,
    _In_ const PROTOCOL_CONFIG *    pProtocol,
    _In_ HINTERNET                  hConnect,
    _Inout_ STRU *                  pstrUrl,
//    _In_ ASPNETCORE_CONFIG*         pAspNetCoreConfig,
    _In_ SERVER_PROCESS*            pServerProcess
)
{
    HRESULT         hr = S_OK;
    PCWSTR          pszVersion = NULL;
    PCSTR           pszVerb;
    STACK_STRU(strVerb, 32);

    //
    // Create the request handle for this request (leave some fields blank,
    // we will fill them when sending the request)
    //
    pszVerb = pRequest->GetHttpMethod();
    if (FAILED(hr = strVerb.CopyA(pszVerb)))
    {
        goto Finished;
    }

    //pszVersion = pProtocol->QueryVersion();
    if (pszVersion == NULL)
    {
        DWORD cchUnused;
        hr = m_pW3Context->GetServerVariable(
            "HTTP_VERSION",
            &pszVersion,
            &cchUnused);
        if (FAILED(hr))
        {
            goto Finished;
        }
    }

    m_hRequest = WinHttpOpenRequest(hConnect,
        strVerb.QueryStr(),
        pstrUrl->QueryStr(),
        pszVersion,
        WINHTTP_NO_REFERER,
        WINHTTP_DEFAULT_ACCEPT_TYPES,
        WINHTTP_FLAG_ESCAPE_DISABLE_QUERY
        | g_OptionalWinHttpFlags);
    if (m_hRequest == NULL)
    {
        hr = HRESULT_FROM_WIN32(GetLastError());
        goto Finished;
    }

    if (!WinHttpSetTimeouts(m_hRequest,
        pProtocol->QueryTimeout(),
        pProtocol->QueryTimeout(),
        pProtocol->QueryTimeout(),
        pProtocol->QueryTimeout()))
    {
        hr = HRESULT_FROM_WIN32(GetLastError());
        goto Finished;
    }

    DWORD dwResponseBufferLimit = pProtocol->QueryResponseBufferLimit();
    if (!WinHttpSetOption(m_hRequest,
        WINHTTP_OPTION_MAX_RESPONSE_DRAIN_SIZE,
        &dwResponseBufferLimit,
        sizeof(dwResponseBufferLimit)))
    {
        hr = HRESULT_FROM_WIN32(GetLastError());
        goto Finished;
    }

    DWORD dwMaxHeaderSize = pProtocol->QueryMaxResponseHeaderSize();
    if (!WinHttpSetOption(m_hRequest,
        WINHTTP_OPTION_MAX_RESPONSE_HEADER_SIZE,
        &dwMaxHeaderSize,
        sizeof(dwMaxHeaderSize)))
    {
        hr = HRESULT_FROM_WIN32(GetLastError());
        goto Finished;
    }

    DWORD dwOption = WINHTTP_DISABLE_COOKIES;

    dwOption |= WINHTTP_DISABLE_AUTHENTICATION;

    if (!pProtocol->QueryDoKeepAlive())
    {
        dwOption |= WINHTTP_DISABLE_KEEP_ALIVE;
    }
    if (!WinHttpSetOption(m_hRequest,
        WINHTTP_OPTION_DISABLE_FEATURE,
        &dwOption,
        sizeof(dwOption)))
    {
        hr = HRESULT_FROM_WIN32(GetLastError());
        goto Finished;
    }

    if (WinHttpSetStatusCallback(m_hRequest,
        FORWARDING_HANDLER::OnWinHttpCompletion,
        (WINHTTP_CALLBACK_FLAG_ALL_COMPLETIONS |
            WINHTTP_CALLBACK_FLAG_HANDLES |
            WINHTTP_CALLBACK_STATUS_SENDING_REQUEST),
        NULL) == WINHTTP_INVALID_STATUS_CALLBACK)
    {
        hr = HRESULT_FROM_WIN32(GetLastError());
        goto Finished;
    }

    hr = GetHeaders(pProtocol,
                    m_pApplication->QueryConfig()->QueryForwardWindowsAuthToken(),
                    pServerProcess,
                   &m_pszHeaders,
                   &m_cchHeaders);
    if (FAILED(hr))
    {
        goto Finished;
    }

Finished:

    return hr;
}

VOID
FORWARDING_HANDLER::OnWinHttpCompletion(
    HINTERNET   hRequest,
    DWORD_PTR   dwContext,
    DWORD       dwInternetStatus,
    LPVOID      lpvStatusInformation,
    DWORD       dwStatusInformationLength
)
{
    FORWARDING_HANDLER * pThis = static_cast<FORWARDING_HANDLER *>(reinterpret_cast<PVOID>(dwContext));
    if (pThis == NULL)
    {
        //error happened, nothing can be done here
        return;
    }
    DBG_ASSERT(pThis->m_Signature == FORWARDING_HANDLER_SIGNATURE);
    pThis->OnWinHttpCompletionInternal(hRequest,
        dwInternetStatus,
        lpvStatusInformation,
        dwStatusInformationLength);
}

VOID
FORWARDING_HANDLER::OnWinHttpCompletionInternal(
    _In_ HINTERNET   hRequest,
    _In_ DWORD       dwInternetStatus,
    _In_ LPVOID      lpvStatusInformation,
    _In_ DWORD       dwStatusInformationLength
)
/*++

Routine Description:

Completion call associated with a WinHTTP operation

Arguments:

hRequest - The winhttp request handle associated with this completion
dwInternetStatus - enum specifying what the completion is for
lpvStatusInformation - completion specific information
dwStatusInformationLength - length of the above information

Return Value:

None

--*/
{
    HRESULT hr = S_OK;
    bool fLockAcquired = FALSE;
    bool fClientError = FALSE;
    bool fAnotherCompletionExpected = FALSE;
    bool fDoPostCompletion = FALSE;
    bool fEndRequest = (dwInternetStatus == WINHTTP_CALLBACK_STATUS_HANDLE_CLOSING);

    DBG_ASSERT(m_pW3Context != NULL);
    __analysis_assume(m_pW3Context != NULL);
    IHttpResponse * pResponse = m_pW3Context->GetResponse();

    // Reference the request handler to prevent it from being released prematurely
    ReferenceRequestHandler();

    UNREFERENCED_PARAMETER(dwStatusInformationLength);

    if (sm_pTraceLog != NULL)
    {
        WriteRefTraceLogEx(sm_pTraceLog,
            m_cRefs,
            this,
            "FORWARDING_HANDLER::OnWinHttpCompletionInternal Enter",
            reinterpret_cast<PVOID>(static_cast<DWORD_PTR>(dwInternetStatus)),
            NULL);
    }

    //FREB log
    if (ANCMEvents::ANCM_WINHTTP_CALLBACK::IsEnabled(m_pW3Context->GetTraceContext()))
    {
        ANCMEvents::ANCM_WINHTTP_CALLBACK::RaiseEvent(
            m_pW3Context->GetTraceContext(),
            NULL,
            dwInternetStatus);
    }

    //
    // ReadLock on the winhttp handle to protect from a client disconnect/
    // server stop closing the handle while we are using it.
    //
    // WinHttp can call async completion on the same thread/stack, so
    // we have to account for that and not try to take the lock again,
    // otherwise, we could end up in a deadlock.
    //

    if (TlsGetValue(g_dwTlsIndex) != this)
    {
        DBG_ASSERT(TlsGetValue(g_dwTlsIndex) == NULL);

        AcquireSRWLockShared(&m_RequestLock);
        TlsSetValue(g_dwTlsIndex, this);
        fLockAcquired = TRUE;
        DBG_ASSERT(TlsGetValue(g_dwTlsIndex) == this);
    }

#ifdef DEBUG
    DebugPrintf(ASPNETCORE_DEBUG_FLAG_INFO,
        "FORWARDING_HANDLER::OnWinHttpCompletionInternal %x -- %d --%p\n", dwInternetStatus, GetCurrentThreadId(), m_pW3Context);
#endif // DEBUG

    if (!fEndRequest)
    {
        if (!m_pW3Context->GetConnection()->IsConnected())
        {
            hr = ERROR_CONNECTION_ABORTED;
            fClientError = m_fHandleClosedDueToClient = TRUE;
            fAnotherCompletionExpected = TRUE;
            goto Failure;
        }
    }

    //
    // In case of websocket, http request handle (m_hRequest) will be closed immediately after upgrading success
    // This close will trigger a callback with WINHTTP_CALLBACK_STATUS_HANDLE_CLOSING
    // As m_RequestStatus is FORWARDER_RECEIVED_WEBSOCKET_RESPONSE, this callback will be skipped.
    // When WebSocket handle (m_pWebsocket) gets closed, another winhttp handle close callback will be triggered
    // This callback will be captured and then notify IIS pipeline to continue
    // This ensures no request leaks
    //
    if (m_RequestStatus == FORWARDER_RECEIVED_WEBSOCKET_RESPONSE)
    {
        fAnotherCompletionExpected = TRUE;
        if (m_pWebSocket == NULL)
        {
            goto Finished;
        }

        switch (dwInternetStatus)
        {
        case WINHTTP_CALLBACK_STATUS_SHUTDOWN_COMPLETE:
            m_pWebSocket->OnWinHttpShutdownComplete();
            break;

        case WINHTTP_CALLBACK_STATUS_WRITE_COMPLETE:
            m_pWebSocket->OnWinHttpSendComplete(
                (WINHTTP_WEB_SOCKET_STATUS*)lpvStatusInformation
            );
            break;

        case WINHTTP_CALLBACK_STATUS_READ_COMPLETE:
            m_pWebSocket->OnWinHttpReceiveComplete(
                (WINHTTP_WEB_SOCKET_STATUS*)lpvStatusInformation
            );
            break;

        case WINHTTP_CALLBACK_STATUS_REQUEST_ERROR:
            m_pWebSocket->OnWinHttpIoError(
                (WINHTTP_WEB_SOCKET_ASYNC_RESULT*)lpvStatusInformation
            );
            break;
        }
        goto Finished;
    }

    switch (dwInternetStatus)
    {
    case WINHTTP_CALLBACK_STATUS_SENDREQUEST_COMPLETE:
    case WINHTTP_CALLBACK_STATUS_WRITE_COMPLETE:
        hr = OnWinHttpCompletionSendRequestOrWriteComplete(hRequest,
            dwInternetStatus,
            &fClientError,
            &fAnotherCompletionExpected);
        break;

    case WINHTTP_CALLBACK_STATUS_HEADERS_AVAILABLE:
        hr = OnWinHttpCompletionStatusHeadersAvailable(hRequest,
            &fAnotherCompletionExpected);
        break;

    case WINHTTP_CALLBACK_STATUS_DATA_AVAILABLE:
        hr = OnWinHttpCompletionStatusDataAvailable(hRequest,
            *reinterpret_cast<const DWORD *>(lpvStatusInformation), // dwBytes
            &fAnotherCompletionExpected);
        break;

    case WINHTTP_CALLBACK_STATUS_READ_COMPLETE:
        hr = OnWinHttpCompletionStatusReadComplete(pResponse,
            dwStatusInformationLength,
            &fAnotherCompletionExpected);
        break;

    case WINHTTP_CALLBACK_STATUS_REQUEST_ERROR:
        hr = HRESULT_FROM_WIN32(static_cast<const WINHTTP_ASYNC_RESULT *>(lpvStatusInformation)->dwError);
        break;

    case WINHTTP_CALLBACK_STATUS_SENDING_REQUEST:
        //
        // This is a notification, not a completion.  This notifiation happens
        // during the Send Request operation.
        //
        fAnotherCompletionExpected = TRUE;
        break;

    case WINHTTP_CALLBACK_STATUS_REQUEST_SENT:
        //
        // Need to ignore this event.  We get it as a side-effect of registering
        // for WINHTTP_CALLBACK_STATUS_SENDING_REQUEST (which we actually need).
        //
        hr = S_OK;
        fAnotherCompletionExpected = TRUE;
        break;

    case WINHTTP_CALLBACK_STATUS_HANDLE_CLOSING:
        if (ANCMEvents::ANCM_REQUEST_FORWARD_END::IsEnabled(m_pW3Context->GetTraceContext()))
        {
            ANCMEvents::ANCM_REQUEST_FORWARD_END::RaiseEvent(
                m_pW3Context->GetTraceContext(),
                NULL);
        }
        if (m_RequestStatus != FORWARDER_DONE || m_fHandleClosedDueToClient)
        {
            hr = ERROR_CONNECTION_ABORTED;
            fClientError = m_fHandleClosedDueToClient;
        }
        m_hRequest = NULL;
        fAnotherCompletionExpected = FALSE;
        break;

    case WINHTTP_CALLBACK_STATUS_CONNECTION_CLOSED:
        hr = ERROR_CONNECTION_ABORTED;
        break;

    default:
        //
        // E_UNEXPECTED is rarely used, if seen means that this condition may been occurred.
        //
        DBG_ASSERT(FALSE);
        hr = E_UNEXPECTED;
        if (sm_pTraceLog != NULL)
        {
            WriteRefTraceLogEx(sm_pTraceLog,
                m_cRefs,
                this,
                "FORWARDING_HANDLER::OnWinHttpCompletionInternal Unexpected WinHTTP Status",
                reinterpret_cast<PVOID>(static_cast<DWORD_PTR>(dwInternetStatus)),
                NULL);
        }
        break;
    }

    //
    // Handle failure code for switch statement above.
    //
    if (FAILED(hr))
    {
        goto Failure;
    }

    //
    // WinHTTP completion handled successfully.
    //
    goto Finished;

Failure:

    m_RequestStatus = FORWARDER_DONE;
    m_fHasError = TRUE;

    pResponse->DisableKernelCache();
    pResponse->GetRawHttpResponse()->EntityChunkCount = 0;

    if (hr == HRESULT_FROM_WIN32(ERROR_WINHTTP_INVALID_SERVER_RESPONSE))
    {
        m_fResetConnection = TRUE;
    }

    if (fClientError || m_fHandleClosedDueToClient)
    {
        if (!m_fResponseHeadersReceivedAndSet)
        {
            pResponse->SetStatus(400, "Bad Request", 0, HRESULT_FROM_WIN32(WSAECONNRESET));
        }
        else
        {
            //
            // Response headers from origin server were
            // already received and set for the current response.
            // Honor the response status.
            //
        }
    }
    else
    {
        STACK_STRU(strDescription, 128);

        pResponse->SetStatus(502, "Bad Gateway", 3, hr);
        /*
        if (!(hr > HRESULT_FROM_WIN32(WINHTTP_ERROR_BASE) &&
            hr <= HRESULT_FROM_WIN32(WINHTTP_ERROR_LAST)) ||
#pragma prefast (suppress : __WARNING_FUNCTION_NEEDS_REVIEW, "Function and parameters reviewed.")
                FormatMessage(
                    FORMAT_MESSAGE_IGNORE_INSERTS | FORMAT_MESSAGE_FROM_HMODULE,
                    g_hWinHttpModule,
                    HRESULT_CODE(hr),
                    0,
                    strDescription.QueryStr(),
                    strDescription.QuerySizeCCH(),
                    NULL) == 0)
            {
                LoadString(g_hModule,
                    IDS_SERVER_ERROR,
                    strDescription.QueryStr(),
                    strDescription.QuerySizeCCH());
            }

            strDescription.SyncWithBuffer();
            if (strDescription.QueryCCH() != 0)
            {
                pResponse->SetErrorDescription(
                    strDescription.QueryStr(),
                    strDescription.QueryCCH(),
                    FALSE);
            }*/
    }
    //}

    // FREB log
    if (ANCMEvents::ANCM_REQUEST_FORWARD_FAIL::IsEnabled(m_pW3Context->GetTraceContext()))
    {
        ANCMEvents::ANCM_REQUEST_FORWARD_FAIL::RaiseEvent(
            m_pW3Context->GetTraceContext(),
            NULL,
            hr);
    }

Finished:

    if (fLockAcquired)
    {
        DBG_ASSERT(TlsGetValue(g_dwTlsIndex) == this);
        TlsSetValue(g_dwTlsIndex, NULL);
        ReleaseSRWLockShared(&m_RequestLock);
        DBG_ASSERT(TlsGetValue(g_dwTlsIndex) == NULL);
    }

    if (m_RequestStatus == FORWARDER_DONE)
    {
        //disbale client disconnect callback
        if (m_pDisconnect != NULL)
        {
            m_pDisconnect->ResetHandler();
            m_pDisconnect = NULL;
        }

        if (m_hRequest != NULL)
        {
            WinHttpSetStatusCallback(m_hRequest,
                FORWARDING_HANDLER::OnWinHttpCompletion,
                WINHTTP_CALLBACK_FLAG_HANDLES,
                NULL);
            if (WinHttpCloseHandle(m_hRequest))
            {
                m_hRequest = NULL;
            }
            else
            {
                // unexpected WinHttp error, log it
                /*DebugBreak();
                m_RequestStatus = FORWARDER_FINISH_REQUEST;
                fDoPostCompletion = TRUE;*/
            }
        }

        //
        // If the request is a websocket request, initiate cleanup.
        //
        if (m_pWebSocket != NULL)
        {
            m_pWebSocket->TerminateRequest();
        }

        if (fEndRequest)
        {
            // only postCompletion after WINHTTP_CALLBACK_STATUS_HANDLE_CLOSING
            // so that no further WinHttp callback will be called
            // in case of websocket, m_hRequest has already been closed after upgrade
            // websocket will handle completion
            m_fFinishRequest = TRUE;
            fDoPostCompletion = TRUE;
        }
    }
    //
    // Completion may had been already posted to IIS if an async
    // operation was started in this method (either WinHTTP or IIS e.g. ReadyEntityBody)
    // If fAnotherCompletionExpected is false, this method must post the completion.
    //
    else if (!fAnotherCompletionExpected)
    {
        //
        // Since we use TLS to guard WinHttp operation, call PostCompletion instead of
        // IndicateCompletion to allow cleaning up the TLS before thread reuse.
        //
        fDoPostCompletion = TRUE;
    }

    DereferenceRequestHandler();
    if (fDoPostCompletion)
    {
        m_pW3Context->PostCompletion(0);
    }
}

HRESULT
FORWARDING_HANDLER::OnWinHttpCompletionSendRequestOrWriteComplete(
    HINTERNET                   hRequest,
    DWORD,
    __out bool *                pfClientError,
    __out bool *                pfAnotherCompletionExpected
)
{
    HRESULT hr = S_OK;
    IHttpRequest *      pRequest = m_pW3Context->GetRequest();

    //
    // completion for sending the initial request or request entity to
    // winhttp, get more request entity if available, else start receiving
    // the response
    //
    if (m_BytesToReceive > 0)
    {
        if (m_pEntityBuffer == NULL)
        {
            m_pEntityBuffer = GetNewResponseBuffer(
                ENTITY_BUFFER_SIZE);
            if (m_pEntityBuffer == NULL)
            {
                hr = E_OUTOFMEMORY;
                goto Finished;
            }
        }

        if (sm_pTraceLog != NULL)
        {
            WriteRefTraceLogEx(sm_pTraceLog,
                m_cRefs,
                this,
                "Calling ReadEntityBody",
                NULL,
                NULL);
        }
        hr = pRequest->ReadEntityBody(
            m_pEntityBuffer + 6,
            min(m_BytesToReceive, BUFFER_SIZE),
            TRUE,       // fAsync
            NULL,       // pcbBytesReceived
            NULL);      // pfCompletionPending
        if (hr == HRESULT_FROM_WIN32(ERROR_HANDLE_EOF))
        {
            DBG_ASSERT(m_BytesToReceive == 0 ||
                m_BytesToReceive == INFINITE);

            //
            // ERROR_HANDLE_EOF is not an error.
            //
            hr = S_OK;

            if (m_BytesToReceive == INFINITE)
            {
                m_BytesToReceive = 0;
                m_cchLastSend = 5;

                //
                // WinHttpWriteData can operate asynchronously.
                //
                // Take reference so that object does not go away as a result of
                // async completion.
                //
                //ReferenceForwardingHandler();
                if (!WinHttpWriteData(m_hRequest,
                    "0\r\n\r\n",
                    5,
                    NULL))
                {
                    hr = HRESULT_FROM_WIN32(GetLastError());
                    //DereferenceForwardingHandler();
                    goto Finished;
                }
                *pfAnotherCompletionExpected = TRUE;

                goto Finished;
            }
        }
        else if (FAILED(hr))
        {
            *pfClientError = TRUE;
            goto Finished;
        }
        else
        {
            //
            // ReadEntityBody will post a completion to IIS.
            //
            *pfAnotherCompletionExpected = TRUE;

            goto Finished;
        }
    }

    m_RequestStatus = FORWARDER_RECEIVING_RESPONSE;

    if (!WinHttpReceiveResponse(hRequest, NULL))
    {
        hr = HRESULT_FROM_WIN32(GetLastError());
        goto Finished;
    }
    *pfAnotherCompletionExpected = TRUE;

Finished:

    return hr;
}

HRESULT
FORWARDING_HANDLER::OnWinHttpCompletionStatusHeadersAvailable(
    HINTERNET                   hRequest,
    __out bool *                pfAnotherCompletionExpected
)
{
    HRESULT       hr = S_OK;
    STACK_BUFFER(bufHeaderBuffer, 2048);
    STACK_STRA(strHeaders, 2048);
    DWORD         dwHeaderSize = bufHeaderBuffer.QuerySize();

    UNREFERENCED_PARAMETER(pfAnotherCompletionExpected);

    //
    // Headers are available, read the status line and headers and pass
    // them on to the client
    //
    // WinHttpQueryHeaders operates synchronously,
    // no need for taking reference.
    //
    dwHeaderSize = bufHeaderBuffer.QuerySize();
    if (!WinHttpQueryHeaders(hRequest,
        WINHTTP_QUERY_RAW_HEADERS_CRLF,
        WINHTTP_HEADER_NAME_BY_INDEX,
        bufHeaderBuffer.QueryPtr(),
        &dwHeaderSize,
        WINHTTP_NO_HEADER_INDEX))
    {
        if (!bufHeaderBuffer.Resize(dwHeaderSize))
        {
            hr = E_OUTOFMEMORY;
            goto Finished;
        }

        //
        // WinHttpQueryHeaders operates synchronously,
        // no need for taking reference.
        //
        if (!WinHttpQueryHeaders(hRequest,
            WINHTTP_QUERY_RAW_HEADERS_CRLF,
            WINHTTP_HEADER_NAME_BY_INDEX,
            bufHeaderBuffer.QueryPtr(),
            &dwHeaderSize,
            WINHTTP_NO_HEADER_INDEX))
        {
            hr = HRESULT_FROM_WIN32(GetLastError());
            goto Finished;
        }
    }

    if (FAILED(hr = strHeaders.CopyW(
        reinterpret_cast<PWSTR>(bufHeaderBuffer.QueryPtr()))))
    {
        goto Finished;
    }

    // Issue: The reason we add trailing \r\n is to eliminate issues that have been observed
    // in some configurations where status and headers would not have final \r\n nor \r\n\r\n
    // (last header was null terminated).That caused crash within header parsing code that expected valid
    // format. Parsing code was fized to return ERROR_INVALID_PARAMETER, but we still should make
    // Example of a status+header string that was causing problems (note the missing \r\n at the end)
    // HTTP/1.1 302 Moved Permanently\r\n....\r\nLocation:http://site\0
    //

    if (!strHeaders.IsEmpty() && strHeaders.QueryStr()[strHeaders.QueryCCH() - 1] != '\n')
    {
        hr = strHeaders.Append("\r\n");
        if (FAILED(hr))
        {
            goto Finished;
        }
    }

    if (FAILED(hr = SetStatusAndHeaders(
        strHeaders.QueryStr(),
        strHeaders.QueryCCH())))
    {
        goto Finished;
    }

    FreeResponseBuffers();

    //
    // If the request was websocket, and response was 101,
    // trigger a flush, so that IIS's websocket module
    // can get a chance to initialize and complete the handshake.
    //

    if (m_fWebSocketEnabled)
    {
        m_RequestStatus = FORWARDER_RECEIVED_WEBSOCKET_RESPONSE;

        hr = m_pW3Context->GetResponse()->Flush(
            TRUE,
            TRUE,
            NULL,
            NULL);

        if (FAILED(hr))
        {
            *pfAnotherCompletionExpected = FALSE;
        }
        else
        {
            *pfAnotherCompletionExpected = TRUE;
        }
    }

Finished:

    return hr;
}

HRESULT
FORWARDING_HANDLER::OnWinHttpCompletionStatusDataAvailable(
    HINTERNET                   hRequest,
    DWORD                       dwBytes,
    _Out_ bool *                pfAnotherCompletionExpected
)
{
    HRESULT hr = S_OK;

    //
    // Response data is available from winhttp, read it
    //
    if (dwBytes == 0)
    {
        if (m_cContentLength != 0)
        {
            hr = HRESULT_FROM_WIN32(ERROR_WINHTTP_INVALID_SERVER_RESPONSE);
            goto Finished;
        }

        m_RequestStatus = FORWARDER_DONE;

        goto Finished;
    }

    m_BytesToSend = dwBytes;
    if (m_cContentLength != 0)
    {
        m_cContentLength -= dwBytes;
    }

    m_pEntityBuffer = GetNewResponseBuffer(
        min(m_BytesToSend, BUFFER_SIZE));
    if (m_pEntityBuffer == NULL)
    {
        hr = E_OUTOFMEMORY;
        goto Finished;
    }

    //
    // WinHttpReadData can operate asynchronously.
    //
    // Take reference so that object does not go away as a result of
    // async completion.
    //
    //ReferenceForwardingHandler();
    if (!WinHttpReadData(hRequest,
        m_pEntityBuffer,
        min(m_BytesToSend, BUFFER_SIZE),
        NULL))
    {
        hr = HRESULT_FROM_WIN32(GetLastError());
        //DereferenceForwardingHandler();
        goto Finished;
    }
    *pfAnotherCompletionExpected = TRUE;

Finished:

    return hr;
}

HRESULT
FORWARDING_HANDLER::OnWinHttpCompletionStatusReadComplete(
    __in IHttpResponse *        pResponse,
    DWORD                       dwStatusInformationLength,
    __out bool *                pfAnotherCompletionExpected
)
{
    HRESULT hr = S_OK;

    //
    // Response data has been read from winhttp, send it to the client
    //
    m_BytesToSend -= dwStatusInformationLength;

    if (m_cMinBufferLimit >= BUFFER_SIZE / 2)
    {
        if (m_cContentLength != 0)
        {
            m_cContentLength -= dwStatusInformationLength;
        }

        //
        // If we were not using WinHttpQueryDataAvailable and winhttp
        // did not fill our buffer, we must have reached the end of the
        // response
        //
        if (dwStatusInformationLength == 0 ||
            m_BytesToSend != 0)
        {
            if (m_cContentLength != 0)
            {
                hr = HRESULT_FROM_WIN32(ERROR_WINHTTP_INVALID_SERVER_RESPONSE);
                goto Finished;
            }

            m_RequestStatus = FORWARDER_DONE;
        }
    }
    else
    {
        DBG_ASSERT(dwStatusInformationLength != 0);
    }

    if (dwStatusInformationLength == 0)
    {
        goto Finished;
    }
    else
    {
        m_cBytesBuffered += dwStatusInformationLength;

        HTTP_DATA_CHUNK Chunk;
        Chunk.DataChunkType = HttpDataChunkFromMemory;
        Chunk.FromMemory.pBuffer = m_pEntityBuffer;
        Chunk.FromMemory.BufferLength = dwStatusInformationLength;
        if (FAILED(hr = pResponse->WriteEntityChunkByReference(&Chunk)))
        {
            goto Finished;
        }
    }

    if (m_cBytesBuffered >= m_cMinBufferLimit)
    {
        //
        // Always post a completion to resume the WinHTTP data pump.
        //
        hr = pResponse->Flush(TRUE,     // fAsync
            TRUE,     // fMoreData
            NULL);    // pcbSent    
        if (FAILED(hr))
        {
            goto Finished;
        }
        *pfAnotherCompletionExpected = TRUE;
    }
    else
    {
        *pfAnotherCompletionExpected = FALSE;
    }

Finished:

    return hr;
}

HRESULT
FORWARDING_HANDLER::OnSendingRequest(
    DWORD                       cbCompletion,
    HRESULT                     hrCompletionStatus,
    __out bool *                pfClientError
)
{
    HRESULT hr = S_OK;
    //
    // This is a completion for a read from http.sys, abort in case
    // of failure, if we read anything write it out over WinHTTP,
    // but we have already reached EOF, now read the response
    //
    if (hrCompletionStatus == HRESULT_FROM_WIN32(ERROR_HANDLE_EOF))
    {
        DBG_ASSERT(m_BytesToReceive == 0 || m_BytesToReceive == INFINITE);
        if (m_BytesToReceive == INFINITE)
        {
            m_BytesToReceive = 0;
            m_cchLastSend = 5; // "0\r\n\r\n"

            if (!WinHttpWriteData(m_hRequest,
                "0\r\n\r\n",
                5,
                NULL))
            {
                hr = HRESULT_FROM_WIN32(GetLastError());
                goto Failure;
            }
        }
        else
        {
            m_RequestStatus = FORWARDER_RECEIVING_RESPONSE;

            if (!WinHttpReceiveResponse(m_hRequest, NULL))
            {
                hr = HRESULT_FROM_WIN32(GetLastError());
                goto Failure;
            }
        }
    }
    else if (SUCCEEDED(hrCompletionStatus))
    {
        DWORD cbOffset;

        if (m_BytesToReceive != INFINITE)
        {
            m_BytesToReceive -= cbCompletion;
            cbOffset = 6;
        }
        else
        {
            //
            // For chunk-encoded requests, need to re-chunk the entity body
            // Add the CRLF just before and after the chunk data
            //
            m_pEntityBuffer[4] = '\r';
            m_pEntityBuffer[5] = '\n';

            m_pEntityBuffer[cbCompletion + 6] = '\r';
            m_pEntityBuffer[cbCompletion + 7] = '\n';

            if (cbCompletion < 0x10)
            {
                cbOffset = 3;
                m_pEntityBuffer[3] = HEX_TO_ASCII(cbCompletion);
                cbCompletion += 5;
            }
            else if (cbCompletion < 0x100)
            {
                cbOffset = 2;
                m_pEntityBuffer[2] = HEX_TO_ASCII(cbCompletion >> 4);
                m_pEntityBuffer[3] = HEX_TO_ASCII(cbCompletion & 0xf);
                cbCompletion += 6;
            }
            else if (cbCompletion < 0x1000)
            {
                cbOffset = 1;
                m_pEntityBuffer[1] = HEX_TO_ASCII(cbCompletion >> 8);
                m_pEntityBuffer[2] = HEX_TO_ASCII((cbCompletion >> 4) & 0xf);
                m_pEntityBuffer[3] = HEX_TO_ASCII(cbCompletion & 0xf);
                cbCompletion += 7;
            }
            else
            {
                DBG_ASSERT(cbCompletion < 0x10000);

                cbOffset = 0;
                m_pEntityBuffer[0] = HEX_TO_ASCII(cbCompletion >> 12);
                m_pEntityBuffer[1] = HEX_TO_ASCII((cbCompletion >> 8) & 0xf);
                m_pEntityBuffer[2] = HEX_TO_ASCII((cbCompletion >> 4) & 0xf);
                m_pEntityBuffer[3] = HEX_TO_ASCII(cbCompletion & 0xf);
                cbCompletion += 8;
            }
        }
        m_cchLastSend = cbCompletion;

        if (!WinHttpWriteData(m_hRequest,
            m_pEntityBuffer + cbOffset,
            cbCompletion,
            NULL))
        {
            hr = HRESULT_FROM_WIN32(GetLastError());
            goto Failure;
        }
    }
    else
    {
        hr = hrCompletionStatus;
        *pfClientError = TRUE;
        goto Failure;
    }

Failure:

    return hr;
}

HRESULT
FORWARDING_HANDLER::OnReceivingResponse(
)
{
    HRESULT hr = S_OK;

    if (m_cBytesBuffered >= m_cMinBufferLimit)
    {
        FreeResponseBuffers();
    }

    if (m_BytesToSend == 0)
    {
        //
        // If response buffering is enabled, try to read large chunks
        // at a time - also treat very small buffering limit as no
        // buffering
        //
        m_BytesToSend = min(m_cMinBufferLimit, BUFFER_SIZE);
        if (m_BytesToSend < BUFFER_SIZE / 2)
        {
            //
            // Disable buffering.
            //
            m_BytesToSend = 0;
        }
    }

    if (m_BytesToSend == 0)
    {
        //
        // No buffering enabled.
        //
        if (!WinHttpQueryDataAvailable(m_hRequest, NULL))
        {
            hr = HRESULT_FROM_WIN32(GetLastError());
            goto Failure;
        }
    }
    else
    {
        //
        // Buffering enabled.
        //
        if (m_pEntityBuffer == NULL)
        {
            m_pEntityBuffer = GetNewResponseBuffer(min(m_BytesToSend, BUFFER_SIZE));
            if (m_pEntityBuffer == NULL)
            {
                hr = E_OUTOFMEMORY;
                goto Failure;
            }
        }

        if (!WinHttpReadData(m_hRequest,
            m_pEntityBuffer,
            min(m_BytesToSend, BUFFER_SIZE),
            NULL))
        {
            hr = HRESULT_FROM_WIN32(GetLastError());
            goto Failure;
        }
    }

Failure:
    return hr;
}

BYTE *
FORWARDING_HANDLER::GetNewResponseBuffer(
    DWORD   dwBufferSize
)
{
    DWORD dwNeededSize = (m_cEntityBuffers + 1) * sizeof(BYTE *);
    if (dwNeededSize > m_buffEntityBuffers.QuerySize() &&
        !m_buffEntityBuffers.Resize(
            max(dwNeededSize, m_buffEntityBuffers.QuerySize() * 2)))
    {
        return NULL;
    }

    BYTE *pBuffer = (BYTE *)HeapAlloc(GetProcessHeap(),
        0, // dwFlags
        dwBufferSize);
    if (pBuffer == NULL)
    {
        return NULL;
    }

    m_buffEntityBuffers.QueryPtr()[m_cEntityBuffers] = pBuffer;
    m_cEntityBuffers++;

    return pBuffer;
}

VOID
FORWARDING_HANDLER::FreeResponseBuffers()
{
    BYTE **pBuffers = m_buffEntityBuffers.QueryPtr();
    for (DWORD i = 0; i<m_cEntityBuffers; i++)
    {
        HeapFree(GetProcessHeap(),
            0, // dwFlags
            pBuffers[i]);
    }
    m_cEntityBuffers = 0;
    m_pEntityBuffer = NULL;
    m_cBytesBuffered = 0;
}

HRESULT
FORWARDING_HANDLER::SetStatusAndHeaders(
    PCSTR           pszHeaders,
    DWORD
)
{
    HRESULT         hr;
    IHttpResponse * pResponse = m_pW3Context->GetResponse();
    IHttpRequest *  pRequest = m_pW3Context->GetRequest();
    STACK_STRA(strHeaderName, 128);
    STACK_STRA(strHeaderValue, 2048);
    DWORD           index = 0;
    PSTR            pchNewline;
    PCSTR           pchEndofHeaderValue;
    BOOL            fServerHeaderPresent = FALSE;

    _ASSERT(pszHeaders != NULL);

    //
    // The first line is the status line
    //
    PSTR pchStatus = const_cast<PSTR>(strchr(pszHeaders, ' '));
    if (pchStatus == NULL)
    {
        return HRESULT_FROM_WIN32(ERROR_INVALID_PARAMETER);
    }
    while (*pchStatus == ' ')
    {
        pchStatus++;
    }
    USHORT uStatus = static_cast<USHORT>(atoi(pchStatus));

    if (m_fWebSocketEnabled && uStatus != 101)
    {
        //
        // Expected 101 response.
        //

        m_fWebSocketEnabled = FALSE;
    }

    pchStatus = strchr(pchStatus, ' ');
    if (pchStatus == NULL)
    {
        return HRESULT_FROM_WIN32(ERROR_INVALID_PARAMETER);
    }
    while (*pchStatus == ' ')
    {
        pchStatus++;
    }
    if (*pchStatus == '\r' || *pchStatus == '\n')
    {
        pchStatus--;
    }

    pchNewline = strchr(pchStatus, '\n');
    if (pchNewline == NULL)
    {
        return HRESULT_FROM_WIN32(ERROR_INVALID_PARAMETER);
    }

    if (uStatus != 200)
    {
        //
        // Skip over any spaces before the '\n'
        //
        for (pchEndofHeaderValue = pchNewline - 1;
            (pchEndofHeaderValue > pchStatus) &&
            ((*pchEndofHeaderValue == ' ') ||
            (*pchEndofHeaderValue == '\r'));
            pchEndofHeaderValue--)
        {
        }

        //
        // Copy the status description
        //
        if (FAILED(hr = strHeaderValue.Copy(
            pchStatus,
            (DWORD)(pchEndofHeaderValue - pchStatus) + 1)) ||
            FAILED(hr = pResponse->SetStatus(uStatus,
                strHeaderValue.QueryStr(),
                0,
                S_OK,
                NULL,
                TRUE)))
        {
            return hr;
        }
    }

    for (index = static_cast<DWORD>(pchNewline - pszHeaders) + 1;
        pszHeaders[index] != '\r' && pszHeaders[index] != '\n' && pszHeaders[index] != '\0';
        index = static_cast<DWORD>(pchNewline - pszHeaders) + 1)
    {
        //
        // Find the ':' in Header : Value\r\n
        //
        PCSTR pchColon = strchr(pszHeaders + index, ':');

        //
        // Find the '\n' in Header : Value\r\n
        //
        pchNewline = const_cast<PSTR>(strchr(pszHeaders + index, '\n'));

        if (pchNewline == NULL)
        {
            return HRESULT_FROM_WIN32(ERROR_INVALID_PARAMETER);
        }

        //
        // Take care of header continuation
        //
        while (pchNewline[1] == ' ' ||
            pchNewline[1] == '\t')
        {
            pchNewline = strchr(pchNewline + 1, '\n');
        }

        DBG_ASSERT(
            (pchColon != NULL) && (pchColon < pchNewline));
        if ((pchColon == NULL) || (pchColon >= pchNewline))
        {
            return HRESULT_FROM_WIN32(ERROR_INVALID_PARAMETER);
        }

        //
        // Skip over any spaces before the ':'
        //
        PCSTR pchEndofHeaderName;
        for (pchEndofHeaderName = pchColon - 1;
            (pchEndofHeaderName >= pszHeaders + index) &&
            (*pchEndofHeaderName == ' ');
            pchEndofHeaderName--)
        {
        }

        pchEndofHeaderName++;

        //
        // Copy the header name
        //
        if (FAILED(hr = strHeaderName.Copy(
            pszHeaders + index,
            (DWORD)(pchEndofHeaderName - pszHeaders) - index)))
        {
            return hr;
        }

        //
        // Skip over the ':' and any trailing spaces
        //
        for (index = static_cast<DWORD>(pchColon - pszHeaders) + 1;
            pszHeaders[index] == ' ';
            index++)
        {
        }

        //
        // Skip over any spaces before the '\n'
        //
        for (pchEndofHeaderValue = pchNewline - 1;
            (pchEndofHeaderValue >= pszHeaders + index) &&
            ((*pchEndofHeaderValue == ' ') ||
            (*pchEndofHeaderValue == '\r'));
            pchEndofHeaderValue--)
        {
        }

        pchEndofHeaderValue++;

        //
        // Copy the header value
        //
        if (pchEndofHeaderValue == pszHeaders + index)
        {
            strHeaderValue.Reset();
        }
        else if (FAILED(hr = strHeaderValue.Copy(
            pszHeaders + index,
            (DWORD)(pchEndofHeaderValue - pszHeaders) - index)))
        {
            return hr;
        }

        //
        // Do not pass the transfer-encoding:chunked, Connection, Date or
        // Server headers along
        //
        DWORD headerIndex = sm_pResponseHeaderHash->GetIndex(strHeaderName.QueryStr());
        if (headerIndex == UNKNOWN_INDEX)
        {
            hr = pResponse->SetHeader(strHeaderName.QueryStr(),
                strHeaderValue.QueryStr(),
                static_cast<USHORT>(strHeaderValue.QueryCCH()),
                FALSE); // fReplace
        }
        else
        {
            switch (headerIndex)
            {
            case HttpHeaderTransferEncoding:
                if (!strHeaderValue.Equals("chunked", TRUE))
                {
                    break;
                }
                __fallthrough;
            case HttpHeaderConnection:
            case HttpHeaderDate:
                continue;

            case HttpHeaderServer:
                fServerHeaderPresent = TRUE;
                break;

            case HttpHeaderContentLength:
                if (pRequest->GetRawHttpRequest()->Verb != HttpVerbHEAD)
                {
                    m_cContentLength = _atoi64(strHeaderValue.QueryStr());
                }
                break;
            }

            hr = pResponse->SetHeader(static_cast<HTTP_HEADER_ID>(headerIndex),
                strHeaderValue.QueryStr(),
                static_cast<USHORT>(strHeaderValue.QueryCCH()),
                TRUE); // fReplace
        }
        if (FAILED(hr))
        {
            return hr;
        }
    }

    //
    // Explicitly remove the Server header if the back-end didn't set one.
    //

    if (!fServerHeaderPresent)
    {
        pResponse->DeleteHeader("Server");
    }

    if (m_fDoReverseRewriteHeaders)
    {
        hr = DoReverseRewrite(pResponse);
        if (FAILED(hr))
        {
            return hr;
        }
    }

    m_fResponseHeadersReceivedAndSet = TRUE;

    return S_OK;
}

HRESULT
FORWARDING_HANDLER::DoReverseRewrite(
    _In_ IHttpResponse *pResponse
)
{
    DBG_ASSERT(pResponse == m_pW3Context->GetResponse());
    BOOL fSecure = (m_pW3Context->GetRequest()->GetRawHttpRequest()->pSslInfo != NULL);
    STRA strTemp;
    PCSTR pszHeader;
    PCSTR pszStartHost;
    PCSTR pszEndHost;
    HTTP_RESPONSE_HEADERS *pHeaders;
    HRESULT hr;

    //
    // Content-Location and Location are easy, one known header in
    // http[s]://host/url format
    //
    pszHeader = pResponse->GetHeader(HttpHeaderContentLocation);
    if (pszHeader != NULL)
    {
        if (_strnicmp(pszHeader, "http://", 7) == 0)
        {
            pszStartHost = pszHeader + 7;
        }
        else if (_strnicmp(pszHeader, "https://", 8) == 0)
        {
            pszStartHost = pszHeader + 8;
        }
        else
        {
            goto Location;
        }

        pszEndHost = strchr(pszStartHost, '/');

        if (FAILED(hr = strTemp.Copy(fSecure ? "https://" : "http://")) ||
            FAILED(hr = strTemp.Append(m_pszOriginalHostHeader)))
        {
            return hr;
        }
        if (pszEndHost != NULL &&
            FAILED(hr = strTemp.Append(pszEndHost)))
        {
            return hr;
        }
        if (FAILED(hr = pResponse->SetHeader(HttpHeaderContentLocation,
            strTemp.QueryStr(),
            static_cast<USHORT>(strTemp.QueryCCH()),
            TRUE)))
        {
            return hr;
        }
    }

Location:

    pszHeader = pResponse->GetHeader(HttpHeaderLocation);
    if (pszHeader != NULL)
    {
        if (_strnicmp(pszHeader, "http://", 7) == 0)
        {
            pszStartHost = pszHeader + 7;
        }
        else if (_strnicmp(pszHeader, "https://", 8) == 0)
        {
            pszStartHost = pszHeader + 8;
        }
        else
        {
            goto SetCookie;
        }

        pszEndHost = strchr(pszStartHost, '/');

        if (FAILED(hr = strTemp.Copy(fSecure ? "https://" : "http://")) ||
            FAILED(hr = strTemp.Append(m_pszOriginalHostHeader)))
        {
            return hr;
        }
        if (pszEndHost != NULL &&
            FAILED(hr = strTemp.Append(pszEndHost)))
        {
            return hr;
        }
        if (FAILED(hr = pResponse->SetHeader(HttpHeaderLocation,
            strTemp.QueryStr(),
            static_cast<USHORT>(strTemp.QueryCCH()),
            TRUE)))
        {
            return hr;
        }
    }

SetCookie:

    //
    // Set-Cookie is different - possibly multiple unknown headers with
    // syntax name=value ; ... ; Domain=.host ; ...
    //
    pHeaders = &pResponse->GetRawHttpResponse()->Headers;
    for (DWORD i = 0; i<pHeaders->UnknownHeaderCount; i++)
    {
        if (_stricmp(pHeaders->pUnknownHeaders[i].pName, "Set-Cookie") != 0)
        {
            continue;
        }

        pszHeader = pHeaders->pUnknownHeaders[i].pRawValue;
        pszStartHost = strchr(pszHeader, ';');
        while (pszStartHost != NULL)
        {
            pszStartHost++;
            while (IsSpace(*pszStartHost))
            {
                pszStartHost++;
            }

            if (_strnicmp(pszStartHost, "Domain", 6) != 0)
            {
                pszStartHost = strchr(pszStartHost, ';');
                continue;
            }
            pszStartHost += 6;

            while (IsSpace(*pszStartHost))
            {
                pszStartHost++;
            }
            if (*pszStartHost != '=')
            {
                break;
            }
            pszStartHost++;
            while (IsSpace(*pszStartHost))
            {
                pszStartHost++;
            }
            if (*pszStartHost == '.')
            {
                pszStartHost++;
            }
            pszEndHost = pszStartHost;
            while (!IsSpace(*pszEndHost) &&
                *pszEndHost != ';' &&
                *pszEndHost != '\0')
            {
                pszEndHost++;
            }

            if (FAILED(hr = strTemp.Copy(pszHeader, static_cast<DWORD>(pszStartHost - pszHeader))) ||
                FAILED(hr = strTemp.Append(m_pszOriginalHostHeader)) ||
                FAILED(hr = strTemp.Append(pszEndHost)))
            {
                return hr;
            }

            pszHeader = (PCSTR)m_pW3Context->AllocateRequestMemory(strTemp.QueryCCH() + 1);
            if (pszHeader == NULL)
            {
                return E_OUTOFMEMORY;
            }
            StringCchCopyA(const_cast<PSTR>(pszHeader), strTemp.QueryCCH() + 1, strTemp.QueryStr());
            pHeaders->pUnknownHeaders[i].pRawValue = pszHeader;
            pHeaders->pUnknownHeaders[i].RawValueLength = static_cast<USHORT>(strTemp.QueryCCH());

            break;
        }
    }

    return S_OK;
}

VOID
FORWARDING_HANDLER::TerminateRequest(
    bool    fClientInitiated
)
{
    UNREFERENCED_PARAMETER(fClientInitiated);
    AcquireSRWLockExclusive(&m_RequestLock);
    // Set tls as close winhttp handle will immediately trigger
    // a winhttp callback on the same thread and we donot want to
    // acquire the lock again
    TlsSetValue(g_dwTlsIndex, this);
    DBG_ASSERT(TlsGetValue(g_dwTlsIndex) == this);

    if (m_hRequest != NULL)
    {
#ifdef DEBUG
        DebugPrintf(ASPNETCORE_DEBUG_FLAG_INFO,
            "FORWARDING_HANDLER::TerminateRequest %d --%p\n", GetCurrentThreadId(), m_pW3Context);
#endif // DEBUG
        m_fHandleClosedDueToClient = fClientInitiated;
        WinHttpCloseHandle(m_hRequest);
    }

    //
    // If the request is a websocket request, initiate cleanup.
    //
    if (m_pWebSocket != NULL)
    {
        m_pWebSocket->TerminateRequest();
    }

    TlsSetValue(g_dwTlsIndex, NULL);
    ReleaseSRWLockExclusive(&m_RequestLock);
    DBG_ASSERT(TlsGetValue(g_dwTlsIndex) == NULL);
}

