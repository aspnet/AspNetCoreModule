#pragma once

extern DWORD            g_OptionalWinHttpFlags;


enum FORWARDING_REQUEST_STATUS
{
    FORWARDER_START,
    FORWARDER_SENDING_REQUEST,
    FORWARDER_RECEIVING_RESPONSE,
    FORWARDER_RECEIVED_WEBSOCKET_RESPONSE,
    FORWARDER_DONE,
    FORWARDER_FINISH_REQUEST
};


class FORWARDING_HANDLER : public REQUEST_HANDLER
{
public:
    FORWARDING_HANDLER(

        _In_ IHttpContext     *pW3Context,
        _In_  HTTP_MODULE_ID  *pModuleId,
        _In_ APPLICATION      *pApplication);

    ~FORWARDING_HANDLER();

    __override
    REQUEST_NOTIFICATION_STATUS
    OnExecuteRequestHandler();

    __override
    REQUEST_NOTIFICATION_STATUS
    OnAsyncCompletion(
        DWORD       cbCompletion,
        HRESULT     hrCompletionStatus
    );

    VOID
    SetStatus(
        FORWARDING_REQUEST_STATUS status
    )
    {
        m_RequestStatus = status;
    }

    static
    VOID
    CALLBACK
    FORWARDING_HANDLER::OnWinHttpCompletion(
        HINTERNET   hRequest,
        DWORD_PTR   dwContext,
        DWORD       dwInternetStatus,
        LPVOID      lpvStatusInformation,
        DWORD       dwStatusInformationLength
    );

    static
    HRESULT
    StaticInitialize(
        BOOL fEnableReferenceCountTracing
    );

    static
    VOID
    StaticTerminate();

    VOID
    TerminateRequest(
        bool    fClientInitiated
    );

private:
    HRESULT
    CreateWinHttpRequest(
        _In_ const IHttpRequest *       pRequest,
        _In_ const PROTOCOL_CONFIG *    pProtocol,
        _In_ HINTERNET                  hConnect,
        _Inout_ STRU *                  pstrUrl,
//        _In_ ASPNETCORE_CONFIG*              pAspNetCoreConfig,
        _In_ SERVER_PROCESS*                 pServerProcess
    );

    VOID
    FORWARDING_HANDLER::OnWinHttpCompletionInternal(
        _In_ HINTERNET   hRequest,
        _In_ DWORD       dwInternetStatus,
        _In_ LPVOID      lpvStatusInformation,
        _In_ DWORD       dwStatusInformationLength
    );

    HRESULT
    OnWinHttpCompletionSendRequestOrWriteComplete(
        HINTERNET                   hRequest,
        DWORD                       dwInternetStatus,
        _Out_ bool *                pfClientError,
        _Out_ bool *                pfAnotherCompletionExpected
    );

    HRESULT
    OnWinHttpCompletionStatusHeadersAvailable(
        HINTERNET                   hRequest,
        _Out_ bool *                pfAnotherCompletionExpected
    );

    HRESULT
    OnWinHttpCompletionStatusDataAvailable(
        HINTERNET                   hRequest,
        DWORD                       dwBytes,
        _Out_ bool *                pfAnotherCompletionExpected
    );

    HRESULT
    OnWinHttpCompletionStatusReadComplete(
        _In_ IHttpResponse *        pResponse,
        DWORD                       dwStatusInformationLength,
        _Out_ bool *                pfAnotherCompletionExpected
    );

    HRESULT
    OnSendingRequest(
        DWORD                       cbCompletion,
        HRESULT                     hrCompletionStatus,
        _Out_ bool *                pfClientError
    );

    HRESULT
    OnReceivingResponse();

    BYTE *
    GetNewResponseBuffer(
        DWORD   dwBufferSize
    );

    VOID
    FreeResponseBuffers();

    HRESULT
    SetStatusAndHeaders(
        PCSTR               pszHeaders,
        DWORD               cchHeaders
    );

    HRESULT
    DoReverseRewrite(
        _In_ IHttpResponse *pResponse
    );

    HRESULT
    GetHeaders(
        _In_ const PROTOCOL_CONFIG *    pProtocol,
        _In_    bool                    fForwardWindowsAuthToken,
        _In_    SERVER_PROCESS*         pServerProcess,
        _Out_   PCWSTR *                ppszHeaders,
        _Inout_ DWORD *                 pcchHeaders
    );

    DWORD                               m_Signature;
    //
    // WinHTTP request handle is protected using a read-write lock.
    //
    SRWLOCK                             m_RequestLock;
    HINTERNET                           m_hRequest;
    FORWARDING_REQUEST_STATUS           m_RequestStatus;

    bool                                m_fWebSocketEnabled;
    bool                                m_fResponseHeadersReceivedAndSet;
    bool                                m_fResetConnection;
    bool                                m_fHandleClosedDueToClient;
    bool                                m_fFinishRequest;
    bool                                m_fHasError;
    BOOL                                m_fDoReverseRewriteHeaders;
    PCSTR                               m_pszOriginalHostHeader;
    PCWSTR                              m_pszHeaders;
    DWORD                               m_cchHeaders;
    DWORD                               m_BytesToReceive;
    DWORD                               m_BytesToSend;
    DWORD                               m_cchLastSend;
    DWORD                               m_cEntityBuffers;
    DWORD                               m_cBytesBuffered;
    DWORD                               m_cMinBufferLimit;
    ULONGLONG                           m_cContentLength;
    WEBSOCKET_HANDLER *                 m_pWebSocket;
    ASYNC_DISCONNECT_CONTEXT *          m_pDisconnect;

    BYTE *                              m_pEntityBuffer;
    static const SIZE_T                 INLINE_ENTITY_BUFFERS = 8;
    BUFFER_T<BYTE*, INLINE_ENTITY_BUFFERS> m_buffEntityBuffers;

    static ALLOC_CACHE_HANDLER *        sm_pAlloc;
    static PROTOCOL_CONFIG              sm_ProtocolConfig;
    static RESPONSE_HEADER_HASH *       sm_pResponseHeaderHash;
    //
    // Reference cout tracing for debugging purposes.
    //
    static TRACE_LOG *                  sm_pTraceLog;

    static STRA                         sm_pStra502ErrorMsg;
};