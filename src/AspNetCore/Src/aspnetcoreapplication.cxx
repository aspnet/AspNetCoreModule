#include "precomp.hxx"

typedef int(*hostfxr_main_fn) (const int argc, const wchar_t* argv[]);

// Initialization export

extern "C" __declspec(dllexport) void register_request_callback(request_handler requestHandler)
{
    ASPNETCORE_APPLICATION::GetInstance()->SetRequestHandlerCallback(requestHandler);
}

// HTTP exports

extern "C" __declspec(dllexport) void http_write_response_bytes(IHttpContext* pHttpContext, CHAR* buffer, int count)
{
    auto pHttpResponse = pHttpContext->GetResponse();

    HTTP_DATA_CHUNK chunk;
    chunk.DataChunkType = HttpDataChunkFromMemory;
    chunk.FromMemory.pBuffer = buffer;
    chunk.FromMemory.BufferLength = count;

    BOOL fAsync = FALSE; // TODO: Use async
    BOOL fMoreData = FALSE;
    BOOL fCompletionExpected = FALSE;
    DWORD dwBytesSent;

    pHttpResponse->WriteEntityChunks(&chunk, 1, fAsync, fMoreData, &dwBytesSent, &fCompletionExpected);
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


void ASPNETCORE_APPLICATION::SetRequestHandlerCallback(request_handler requestHandler)
{
    m_RequestHandler = requestHandler;

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
    auto dwTimeout = INFINITE; // IsDebuggerPresent() ? INFINITE : 30000;

    // What should the timeout be? (This is sorta hacky)
    const HANDLE pHandles[2]{ m_Thread, m_InitalizeEvent };

    // Wait on either the thread tot
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
    // REVIEW: How do we get the right version of hostfxr?
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
        m_RequestHandler(pHttpContext, OnRequestCompleted, this);
    }
}

void ASPNETCORE_APPLICATION::CompleteRequest(IHttpContext* pHttpContext, int error)
{
    // Post the completion of the http request
    pHttpContext->PostCompletion(0);
}


void ASPNETCORE_APPLICATION::Shutdown()
{

}