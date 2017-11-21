#include "..\precomp.hxx"

FORWARDING_HANDLER::FORWARDING_HANDLER(
    _In_ IHttpContext *pW3Context,
    _In_ APPLICATION  *pApplication
) : REQUEST_HANDLER(pW3Context, pApplication)
{
    m_fWebSocketEnabled = FALSE;
    m_pszHeaders = NULL;
    m_cchHeaders = 0;
    //todo
}

FORWARDING_HANDLER::~FORWARDING_HANDLER(
)
{
    //todo
}

__override
REQUEST_NOTIFICATION_STATUS
FORWARDING_HANDLER::OnExecuteRequestHandler()
{
    REQUEST_NOTIFICATION_STATUS retVal = RQ_NOTIFICATION_CONTINUE;
    HRESULT                     hr = S_OK;
    HINTERNET                   hConnect = NULL;
    IHttpRequest               *pRequest = m_pW3Context->GetRequest();
    IHttpResponse              *pResponse = m_pW3Context->GetResponse();
    IHttpConnection            *pClientConnection = NULL;
    OUT_OF_PROCESS_APPLICATION *pApplication = NULL;
    PROTOCOL_CONFIG            *pProtocol = NULL;
    SERVER_PROCESS             *pServerProcess = NULL;
    FORWARDER_CONNECTION       *pConnection = NULL;

    STACK_STRU(struEscapedUrl, 2048);

    // check connection
    pClientConnection = m_pW3Context->GetConnection();
    if (pClientConnection == NULL ||
        !pClientConnection->IsConnected())
    {
        hr = HRESULT_FROM_WIN32(WSAECONNRESET);
        goto Failure;
    }

    pApplication = dynamic_cast<OUT_OF_PROCESS_APPLICATION*> (m_pApplication);
    if (pApplication == NULL)
    {
        hr = E_INVALIDARG;
        goto Finished;
    }

    pApplication->GetProcess(&pServerProcess);
    if (FAILED(hr))
    {
        goto Failure;
    }

    if (pServerProcess->QueryWinHttpConnection() == NULL)
    {
        hr = HRESULT_FROM_WIN32(ERROR_INVALID_HANDLE);
        goto Failure;
    }

    hConnect = pServerProcess->QueryWinHttpConnection()->QueryHandle();

    hr = CreateWinHttpRequest(pRequest,
        pProtocol,
        hConnect,
        &struEscapedUrl,
        pApplication->QueryConfig(),
        pServerProcess);

    if (FAILED(hr))
    {
        goto Failure;
    }



Failure:
Finished:
    return retVal;
}

__override
REQUEST_NOTIFICATION_STATUS
FORWARDING_HANDLER::OnAsyncCompletion(
    DWORD           cbCompletion,
    HRESULT         hrCompletionStatus
)
{
    //todo
    return RQ_NOTIFICATION_CONTINUE;
}


HRESULT
FORWARDING_HANDLER::CreateWinHttpRequest(
    _In_ const IHttpRequest *       pRequest,
    _In_ const PROTOCOL_CONFIG *    pProtocol,
    _In_ HINTERNET                  hConnect,
    _Inout_ STRU *                  pstrUrl,
    _In_ ASPNETCORE_CONFIG*         pAspNetCoreConfig,
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
    //HRESULT hr = S_OK;

//Finished:

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