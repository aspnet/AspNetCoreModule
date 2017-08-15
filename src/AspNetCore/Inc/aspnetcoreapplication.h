#pragma once

typedef void(*request_handler_cb) (int error, IHttpContext* pHttpContext, void* pvCompletionContext);
typedef REQUEST_NOTIFICATION_STATUS(*PFN_REQUEST_HANDLER) (IHttpContext* pHttpContext, void* pvRequstHandlerContext);
typedef BOOL(*PFN_SHUTDOWN_HANDLER) (void* pvShutdownHandlerContext);

class ASPNETCORE_APPLICATION
{
public:
    ASPNETCORE_APPLICATION() : m_pConfiguration(NULL), m_RequestHandler(NULL)
    { }
    ~ASPNETCORE_APPLICATION() { }

    HRESULT Initialize(ASPNETCORE_CONFIG* pConfig);
    REQUEST_NOTIFICATION_STATUS ExecuteRequest(IHttpContext* pHttpContext);
    void Shutdown();
    void SetCallbackHandles(PFN_REQUEST_HANDLER request_callback, PFN_SHUTDOWN_HANDLER shutdown_callback, void* pvRequstHandlerContext, void* pvShutdownHandlerContext);

    // Executes the .NET Core process
    void ExecuteApplication();
	void FindDotNetFolders(const std::wstring path, std::vector<std::wstring> *folders);
	std::wstring FindHighestDotNetVersion(std::vector<std::wstring> folders);
	BOOL DirectoryExists(const std::wstring path);
	BOOL GetEnv(const wchar_t* name, std::wstring *recv);
	ASPNETCORE_CONFIG* GetConfig() {
		return m_pConfiguration;
	}

    static ASPNETCORE_APPLICATION* GetInstance()
    {
        return s_Application;
    }

private:
    // Thread executing the .NET Core process
    HANDLE m_Thread;

    // Configuration for this application
    ASPNETCORE_CONFIG* m_pConfiguration;

    // The request handler callback from managed code
    PFN_REQUEST_HANDLER m_RequestHandler;
    void* m_RequstHandlerContext;

    // The shutdown handler callback from managed code
    PFN_SHUTDOWN_HANDLER m_ShutdownHandler;
    void* m_ShutdownHandlerContext;
    // The event that gets triggered when managed initialization is complete
    HANDLE m_InitalizeEvent;

    // The exit code of the .NET Core process
    int m_ProcessExitCode;

    static ASPNETCORE_APPLICATION* s_Application;
};

