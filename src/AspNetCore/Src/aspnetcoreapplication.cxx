#include "precomp.hxx"
#include "fx_ver.h"
#include <algorithm>
typedef int(*hostfxr_main_fn) (const int argc, const wchar_t* argv[]);

// Initialization export

extern "C" __declspec(dllexport) void register_request_callback(PFN_REQUEST_HANDLER requestHandler, void* pvRequstHandlerContext)
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

extern "C" __declspec(dllexport) HRESULT http_post_completion(IHttpContext* pHttpContext)
{
    return pHttpContext->PostCompletion(0);
}

extern "C" __declspec(dllexport) void http_indicate_completion(IHttpContext* pHttpContext, REQUEST_NOTIFICATION_STATUS notificationStatus)
{
    pHttpContext->IndicateCompletion(notificationStatus);
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

// Thread execution callback
static void ExecuteAspNetCoreProcess(LPVOID pContext)
{
    auto pApplication = (ASPNETCORE_APPLICATION*)pContext;

    pApplication->ExecuteApplication();
}

ASPNETCORE_APPLICATION* ASPNETCORE_APPLICATION::s_Application = NULL;


void ASPNETCORE_APPLICATION::SetRequestHandlerCallback(PFN_REQUEST_HANDLER requestHandler, void* pvRequstHandlerContext)
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
    // Ideally this would be an export from a library in an unversioned folder
	std::wstring path;
	GetEnv(TEXT("PATH"), &path);

	size_t start = 0;
	size_t next = 0;
	std::wstring dotnetLocation;
	std::wstring name(TEXT("\\dotnet\\"));
	size_t test = path.find(L";", start);
	while ((next = path.find(L";", start)) != std::wstring::npos) {
		dotnetLocation = path.substr(start, next - start);
		if (dotnetLocation.find(name, dotnetLocation.size() - name.size()) != std::wstring::npos) {
			break;
		}
		start = next + 1;
	}

	// Verify that this directory exists
	if (!DirectoryExists(dotnetLocation)) {
		return;
	}

	// Add host\\fxr to the path (MUST NOT HAVE TRAILING SLASH)
	std::wstring hostFxr(TEXT("host\\fxr"));
	std::wstring hostFxrFolder = dotnetLocation + hostFxr;

	if (!DirectoryExists(hostFxrFolder)) {
		return;
	}

	std::wstring searchExpression = hostFxrFolder + std::wstring(TEXT("\\*"));
	std::vector<std::wstring> folders;
	FindDotNetFolders(searchExpression, &folders);

	if (folders.size() == 0) {
		return;
	}

	std::wstring version = FindHighestDotNetVersion(folders);
	std::wstring hostFxrLocation = hostFxrFolder + TEXT("\\") + version + TEXT("\\hostfxr.dll");

	auto module = LoadLibraryW(hostFxrLocation.c_str());

    if (module == nullptr)
    {
        // .NET Core not installed (we can log a more detailed error message here)
        return;
    }

    // Get the entry point for main
    auto proc = (hostfxr_main_fn)GetProcAddress(module, "hostfxr_main");
    const wchar_t* argv[2];

    // The first argument is mostly ignored
	argv[0] = dotnetLocation.c_str(); // TODO we may need to add .exe here
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

BOOL ASPNETCORE_APPLICATION::GetEnv(const wchar_t* name, std::wstring *recv) {
	recv->clear();

	auto length = ::GetEnvironmentVariableW(name, nullptr, 0);
	if (length == 0)
	{
		auto err = GetLastError();
		if (err != ERROR_ENVVAR_NOT_FOUND)
		{
		}
		return false;
	}
	auto buf = new wchar_t[length];
	if (::GetEnvironmentVariableW(name, buf, length) == 0)
	{
		return false;
	}

	recv->assign(buf);
	delete[] buf;

	return true;
}

void ASPNETCORE_APPLICATION::FindDotNetFolders(const std::wstring path, std::vector<std::wstring> *folders) {
	WIN32_FIND_DATAW data = { 0 };
	// TODO Need to copy data.cFileName rather than pushing the pointer
	auto handle = ::FindFirstFileExW(path.c_str(), FindExInfoStandard, &data, FindExSearchNameMatch, NULL, 0);
	if (handle == INVALID_HANDLE_VALUE) {
		return;
	}
	do {
		std::wstring folder(data.cFileName);
		folders->push_back(folder);
	} while (::FindNextFileW(handle, &data));
	::FindClose(handle);
}

std::wstring ASPNETCORE_APPLICATION::FindHighestDotNetVersion(std::vector<std::wstring> folders) {
	fx_ver_t max_ver(-1, -1, -1);
	for (const auto& dir : folders)
	{
		fx_ver_t fx_ver(-1, -1, -1);
		if (fx_ver_t::parse(dir, &fx_ver, false))
		{
			max_ver = std::max(max_ver, fx_ver);
		}
	}
	return max_ver.as_str();
}

BOOL ASPNETCORE_APPLICATION::DirectoryExists(const std::wstring path) {
	if (path.size() == 0) {
		return false;
	}
	WIN32_FILE_ATTRIBUTE_DATA data;


	if (GetFileAttributesExW(path.c_str(), GetFileExInfoStandard, &data)) {
		return true;
	}
	return false;
}

REQUEST_NOTIFICATION_STATUS ASPNETCORE_APPLICATION::ExecuteRequest(IHttpContext* pHttpContext)
{
    if (m_RequestHandler != NULL)
    {
        return m_RequestHandler(pHttpContext, m_RequstHandlerContext);
    }

    pHttpContext->GetResponse()->SetStatus(500, "Internal Server Error", 0, E_APPLICATION_ACTIVATION_EXEC_FAILURE);
    return RQ_NOTIFICATION_FINISH_REQUEST;
}


void ASPNETCORE_APPLICATION::Shutdown()
{

}