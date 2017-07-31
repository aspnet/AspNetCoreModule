#pragma once

typedef void(*request_handler_cb) (int error, IHttpContext* pHttpContext, void* pvCompletionContext);
typedef void(*request_handler) (IHttpContext* pHttpContext, request_handler_cb pfnCompletionCallback, void* pvCompletionContext, void* pvRequstHandlerContext);

class ASPNETCORE_APPLICATION
{
public:
    ASPNETCORE_APPLICATION() : m_pConfiguration(NULL), m_RequestHandler(NULL)
    { }
    ~ASPNETCORE_APPLICATION() { }

    HRESULT Initialize(ASPNETCORE_CONFIG* pConfig);
    void ExecuteRequest(IHttpContext* pHttpContext);
    void Shutdown();
    void SetRequestHandlerCallback(request_handler callback, void* pvRequstHandlerContext);

    // Executes the .NET Core process
    void ExecuteApplication();
    void CompleteRequest(IHttpContext* pHttpContext, int error);

    static ASPNETCORE_APPLICATION* GetInstance()
    {
        return s_Application;
    }

private:
    // Thread executig the .NET Core process
    HANDLE m_Thread;

    // Configuration for this application
    ASPNETCORE_CONFIG* m_pConfiguration;

    // The request handler callback from managed code
    request_handler m_RequestHandler;
    void* m_RequstHandlerContext;

    // The event that gets triggered when managed initialization is complete
    HANDLE m_InitalizeEvent;

    // The exit code of the .NET Core process
    int m_ProcessExitCode;

    static ASPNETCORE_APPLICATION* s_Application;
};

