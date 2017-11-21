#pragma once

extern DWORD            g_OptionalWinHttpFlags;

class FORWARDING_HANDLER : public REQUEST_HANDLER
{
public:
    FORWARDING_HANDLER(
        _In_ IHttpContext *pW3Context,
        _In_ APPLICATION  *pApplication);

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

private:
    HRESULT
    CreateWinHttpRequest(
        _In_ const IHttpRequest *       pRequest,
        _In_ const PROTOCOL_CONFIG *    pProtocol,
        _In_ HINTERNET                  hConnect,
        _Inout_ STRU *                  pstrUrl,
        _In_ ASPNETCORE_CONFIG*              pAspNetCoreConfig,
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
    GetHeaders(
        _In_ const PROTOCOL_CONFIG *    pProtocol,
        _In_    bool                    fForwardWindowsAuthToken,
        _In_    SERVER_PROCESS*         pServerProcess,
        _Out_   PCWSTR *                ppszHeaders,
        _Inout_ DWORD *                 pcchHeaders
    );

    //
    // WinHTTP request handle is protected using a read-write lock.
    //
    SRWLOCK                             m_RequestLock;
    HINTERNET                           m_hRequest;

    bool                                m_fWebSocketEnabled;
    PCWSTR                              m_pszHeaders;
    DWORD                               m_cchHeaders;

};