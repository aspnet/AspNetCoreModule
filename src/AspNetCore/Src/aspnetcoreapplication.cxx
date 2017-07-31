#include "precomp.hxx"

typedef int(*hostfxr_main_fn) (const int argc, const wchar_t* argv[]);

// Initialization export

extern "C" __declspec(dllexport) void register_request_callback(request_handler requestHandler, void* pvRequstHandlerContext)
{
    ASPNETCORE_APPLICATION::GetInstance()->SetRequestHandlerCallback(requestHandler, pvRequstHandlerContext);
}

// HTTP exports

extern "C" __declspec(dllexport) HTTP_REQUEST* http_get_raw_request(IHttpContext* pHttpContext)
{
    return pHttpContext->GetRequest()->GetRawHttpRequest();
}

extern "C" __declspec(dllexport) HTTP_RESPONSE* http_get_raw_response(IHttpContext* pHttpContext)
{
    return pHttpContext->GetResponse()->GetRawHttpResponse();
}

extern "C" __declspec(dllexport) void http_set_response_status_code(IHttpContext* pHttpContext, USHORT statusCode, PCSTR pszReason)
{
    pHttpContext->GetResponse()->SetStatus(statusCode, pszReason);
}

extern "C" __declspec(dllexport) void http_get_completion_info(IHttpCompletionInfo2* info, DWORD* cbBytes, HRESULT* hr)
{
    *cbBytes = info->GetCompletionBytes();
    *hr = info->GetCompletionStatus();
}

extern "C" __declspec(dllexport) HRESULT http_read_request_bytes(
    IHttpContext* pHttpContext,
    CHAR* pvBuffer,
    DWORD cbBuffer,
    PFN_ASYNC_COMPLETION pfnCompletionCallback,
    void* pvCompletionContext,
    DWORD* pDwBytesReceived,
    BOOL* pfCompletionPending)
{
    auto pHttpRequest = (IHttpRequest3*)pHttpContext->GetRequest();

    BOOL fAsync = TRUE;
    BOOL fCompletionPending;

    HRESULT hr = pHttpRequest->ReadEntityBody(
        pvBuffer,
        cbBuffer,
        fAsync,
        pfnCompletionCallback,
        pvCompletionContext,
        pDwBytesReceived,
        pfCompletionPending);

    if (hr == HRESULT_FROM_WIN32(ERROR_HANDLE_EOF))
    {
        // We reached the end of the data
        hr = S_OK;
        fCompletionPending = FALSE;
        *pDwBytesReceived = 0;
    }

    return hr;
}

extern "C" __declspec(dllexport) HRESULT http_write_response_bytes(
    IHttpContext* pHttpContext,
    CHAR* pvBuffer,
    DWORD cbBuffer,
    PFN_ASYNC_COMPLETION pfnCompletionCallback,
    void* pvCompletionContext,
    BOOL* pfCompletionExpected)
{
    auto pHttpResponse = (IHttpResponse2*)pHttpContext->GetResponse();

    HTTP_DATA_CHUNK chunk;
    chunk.DataChunkType = HttpDataChunkFromMemory;
    chunk.FromMemory.pBuffer = pvBuffer;
    chunk.FromMemory.BufferLength = cbBuffer;

    BOOL fAsync = TRUE;
    BOOL fMoreData = TRUE;
    DWORD dwBytesSent;

    HRESULT hr = pHttpResponse->WriteEntityChunks(
        &chunk,
        1,
        fAsync,
        fMoreData,
        pfnCompletionCallback,
        pvCompletionContext,
        &dwBytesSent,
        pfCompletionExpected);

    return hr;
}

extern "C" __declspec(dllexport) HRESULT http_flush_response_bytes(
    IHttpContext* pHttpContext,
    PFN_ASYNC_COMPLETION pfnCompletionCallback,
    void* pvCompletionContext,
    BOOL* pfCompletionExpected)
{
    auto pHttpResponse = (IHttpResponse2*)pHttpContext->GetResponse();

    BOOL fAsync = TRUE;
    BOOL fMoreData = TRUE;
    DWORD dwBytesSent;

    HRESULT hr = pHttpResponse->Flush(
        fAsync,
        fMoreData,
        pfnCompletionCallback,
        pvCompletionContext,
        &dwBytesSent,
        pfCompletionExpected);

    return hr;
}

// Request callback
static void OnRequestCompleted(int error, IHttpContext* pHttpContext, void* state)
{
    ((ASPNETCORE_APPLICATION*)state)->CompleteRequest(pHttpContext, error);
}

// Thread execution callback
static void ExecuteAspNetCoreProcess(LPVOID pContext)
{
    auto pApplication = (ASPNETCORE_APPLICATION*)pContext;

    pApplication->ExecuteApplication();
}

ASPNETCORE_APPLICATION* ASPNETCORE_APPLICATION::s_Application = NULL;


void ASPNETCORE_APPLICATION::SetRequestHandlerCallback(request_handler requestHandler, void* pvRequstHandlerContext)
{
    m_RequestHandler = requestHandler;
    m_RequstHandlerContext = pvRequstHandlerContext;

    // Initialization complete
    SetEvent(m_InitalizeEvent);
}

HRESULT ASPNETCORE_APPLICATION::Initialize(ASPNETCORE_CONFIG * pConfig)
{
    m_pConfiguration = pConfig;

    m_InitalizeEvent = CreateEvent(
        NULL,   // default security attributes
        TRUE,   // manual reset event
        FALSE,  // not set
        NULL);  // name

    m_Thread = CreateThread(
        NULL,       // default security attributes
        0,          // default stack size
        (LPTHREAD_START_ROUTINE)ExecuteAspNetCoreProcess,
        this,       // thread function arguments
        0,          // default creation flags
        NULL);      // receive thread identifier

    if (m_Thread == NULL)
    {
        return HRESULT_FROM_WIN32(GetLastError());
    }

    // If the debugger is attached, never timeout
    auto dwTimeout = INFINITE; // IsDebuggerPresent() ? INFINITE : pConfig->QueryStartupTimeLimitInMS();

    // What should the timeout be? (This is sorta hacky)
    const HANDLE pHandles[2]{ m_Thread, m_InitalizeEvent };

    // Wait on either the thread to complete or the event to be set
    auto dwResult = WaitForMultipleObjects(2, pHandles, FALSE, dwTimeout);

    // It all timed out
    if (dwResult == WAIT_TIMEOUT)
    {
        return E_FAIL;
    }

    // The thread ended it means that something failed
    if (WaitForSingleObject(m_Thread, 0) == WAIT_OBJECT_0)
    {
        // Check the exit code here?
        return E_FAIL;
    }

    return S_OK;
}

void ASPNETCORE_APPLICATION::ExecuteApplication()
{
    // TODO: Implement steps to properly find the version:
    // 1. Look at the PATH and find the muxer location (split PATH on ; and find location with dotnet.exe)
    // 2. Look at the application path to figure out which version of hostfxr.dll to pick.
    // This data is in the runtimeconfig.json file
    // Ideally this would be an export from a library in an unversioned folder
    // See https://github.com/dotnet/core-setup/blob/4e075b0c2abfbdaa564977ae2daba5b2b2f5c481/src/corehost/corehost.cpp#L83

    auto module = LoadLibraryW(L"C:\\Program Files\\dotnet\\host\\fxr\\2.0.0-preview2-25407-01\\hostfxr.dll");

    if (module == nullptr)
    {
        // .NET Core not installed (we can log a more detailed error message here)
        return;
    }

    // Get the entry point for main
    auto proc = (hostfxr_main_fn)GetProcAddress(module, "hostfxr_main");
    const wchar_t* argv[2];

    // The first argument is mostly ignored
    argv[0] = L"C:\\Program Files\\dotnet\\dotnet.exe";
    argv[1] = m_pConfiguration->QueryArguments()->QueryStr();

    // Hack from hell, there can only ever be a single instance of .NET Core
    // loaded in the process but we need to get config information to boot it up in the
    // first place. This is happening in an execute request handler and everyone waits
    // until this initialization is done. 

    // We set a static so that managed code can call back into this instance and
    // set the callbacks
    s_Application = this;

    m_ProcessExitCode = proc(2, argv);
}

void ASPNETCORE_APPLICATION::ExecuteRequest(IHttpContext* pHttpContext)
{
    if (m_RequestHandler != NULL)
    {
        m_RequestHandler(pHttpContext, OnRequestCompleted, this, m_RequstHandlerContext);
    }
}

void ASPNETCORE_APPLICATION::CompleteRequest(IHttpContext* pHttpContext, int error)
{
    // Post the completion of the http request
    pHttpContext->PostCompletion(0);
    // pHttpContext->IndicateCompletion(REQUEST_NOTIFICATION_STATUS::RQ_NOTIFICATION_CONTINUE);
}


void ASPNETCORE_APPLICATION::Shutdown()
{

}