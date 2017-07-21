#pragma once

typedef void(*request_handler_cb) (int error, IHttpContext* pHttpContext, void* pvCompletionContext);
typedef REQUEST_NOTIFICATION_STATUS(*PFN_REQUEST_HANDLER) (IHttpContext* pHttpContext, void* pvRequstHandlerContext);
typedef BOOL(*PFN_SHUTDOWN_HANDLER) (void* pvShutdownHandlerContext);

class ASPNETCORE_APPLICATION
{
public:

    ASPNETCORE_APPLICATION():
        m_pConfiguration(NULL),
        m_RequestHandler(NULL)
    {
    }

    ~ASPNETCORE_APPLICATION()
    {
    }

    HRESULT
    Initialize(
        _In_ ASPNETCORE_CONFIG* pConfig
    );

    REQUEST_NOTIFICATION_STATUS
    ExecuteRequest(
        _In_ IHttpContext* pHttpContext
    );

    VOID
    Shutdown(
        VOID
    );

    VOID
    SetCallbackHandles(
        _In_ PFN_REQUEST_HANDLER request_callback,
        _In_ PFN_SHUTDOWN_HANDLER shutdown_callback,
        _In_ VOID* pvRequstHandlerContext,
        _In_ VOID* pvShutdownHandlerContext
    );

    // Executes the .NET Core process
    VOID
    ExecuteApplication(
        VOID
    );

    VOID
    FindDotNetFolders(
        _In_ STRU *pstrPath,
        _Out_ std::vector<std::wstring> *pvFolders
    );

    VOID
    FindHighestDotNetVersion(
        _In_ std::vector<std::wstring> vFolders,
        _Out_ STRU *pstrResult
    );

    BOOL
    DirectoryExists(
        _In_ STRU *pstrPath
    );

    BOOL
    GetEnv(
        _In_ PCWSTR pszEnvironmentVariable,
        _Out_ STRU *pstrResult
    );

    ASPNETCORE_CONFIG*
    GetConfig(
        VOID
    )
    {
		return m_pConfiguration;
	}

    static
    ASPNETCORE_APPLICATION*
    GetInstance(
        VOID
    )
    {
        return s_Application;
    }

private:
    // Thread executing the .NET Core process
    HANDLE m_hThread;

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

