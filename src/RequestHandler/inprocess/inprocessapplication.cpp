#include "..\precomp.hxx"

IN_PROCESS_APPLICATION*  IN_PROCESS_APPLICATION::s_Application = NULL;

IN_PROCESS_APPLICATION::IN_PROCESS_APPLICATION(
    IHttpServer*        pHttpServer, 
    ASPNETCORE_CONFIG*  pConfig) :
    APPLICATION(pHttpServer, pConfig),
    m_ProcessExitCode(0),
    m_fManagedAppLoaded(FALSE),
    m_fLoadManagedAppError(FALSE),
    m_fInitialized(FALSE),
    m_fRecycleProcessCalled(FALSE)
{
    // is it guaranteed that we have already checked app offline at this point?
    // If so, I don't think there is much to do here.
    DBG_ASSERT(pHttpServer != NULL);
    DBG_ASSERT(pConfig != NULL);
    InitializeSRWLock(&m_srwLock);

    // TODO we can probably initialized as I believe we are the only ones calling recycle.
    m_fInitialized = TRUE;
    m_status = APPLICATION_STATUS::RUNNING;
}

IN_PROCESS_APPLICATION::~IN_PROCESS_APPLICATION()
{
    Recycle();
}

__override
VOID
IN_PROCESS_APPLICATION::ShutDown()
{
    //todo
}

// This is the same function as before, TODO configrm if we need to change anything for configuration.
VOID
IN_PROCESS_APPLICATION::Recycle(
    VOID
)
{
    if (m_fInitialized)
    {
        DWORD    dwThreadStatus = 0;
        DWORD    dwTimeout = m_pConfig->QueryShutdownTimeLimitInMS();

        AcquireSRWLockExclusive(&m_srwLock);

        if (!m_pHttpServer->IsCommandLineLaunch() &&
            !m_fRecycleProcessCalled &&
            (m_pHttpServer->GetAdminManager() != NULL))
        {
            // IIS scenario.
            // notify IIS first so that new request will be routed to new worker process
            m_pHttpServer->RecycleProcess(L"AspNetCore Recycle Process on Demand");
        }

        m_fRecycleProcessCalled = TRUE;

        // First call into the managed server and shutdown
        if (m_ShutdownHandler != NULL)
        {
            m_ShutdownHandler(m_ShutdownHandlerContext);
            m_ShutdownHandler = NULL;
        }

        if (m_hThread != NULL &&
            GetExitCodeThread(m_hThread, &dwThreadStatus) != 0 &&
            dwThreadStatus == STILL_ACTIVE)
        {
            // wait for gracefullshut down, i.e., the exit of the background thread or timeout
            if (WaitForSingleObject(m_hThread, dwTimeout) != WAIT_OBJECT_0)
            {
                // if the thread is still running, we need kill it first before exit to avoid AV
                if (GetExitCodeThread(m_hThread, &dwThreadStatus) != 0 && dwThreadStatus == STILL_ACTIVE)
                {
                    TerminateThread(m_hThread, STATUS_CONTROL_C_EXIT);
                }
            }
        }

        CloseHandle(m_hThread);
        m_hThread = NULL;
        s_Application = NULL;

        ReleaseSRWLockExclusive(&m_srwLock);
        if (m_pHttpServer && m_pHttpServer->IsCommandLineLaunch())
        {
            // IISExpress scenario
            // Can only call exit to terminate current process
            exit(0);
        }
    }
}

REQUEST_NOTIFICATION_STATUS
IN_PROCESS_APPLICATION::OnAsyncCompletion(
    IHttpContext* pHttpContext,
    DWORD           cbCompletion,
    HRESULT         hrCompletionStatus,
    IN_PROCESS_HANDLER* pInProcessHandler
)
{
    HRESULT hr;
    REQUEST_NOTIFICATION_STATUS dwRequestNotificationStatus = RQ_NOTIFICATION_CONTINUE;

    if (pInProcessHandler->QueryIsManagedRequestComplete())
    {
        // means PostCompletion has been called and this is the associated callback.
        dwRequestNotificationStatus = pInProcessHandler->QueryAsyncCompletionStatus();
        // TODO cleanup whatever disconnect listener there is
        return dwRequestNotificationStatus;
    }
    else
    {
        // Call the managed handler for async completion.
        return m_AsyncCompletionHandler(pInProcessHandler->QueryManagedHttpContext(), hrCompletionStatus, cbCompletion);
    }
}

REQUEST_NOTIFICATION_STATUS
IN_PROCESS_APPLICATION::OnExecuteRequest(
    _In_ IHttpContext* pHttpContext,
    _In_ IN_PROCESS_HANDLER* pInProcessHandler
)
{
    if (m_RequestHandler != NULL)
    {
        return m_RequestHandler(pHttpContext, pInProcessHandler, m_RequestHandlerContext);
    }

    ////
    //// return error as the application did not register callback
    ////
    //if (ANCMEvents::ANCM_EXECUTE_sREQUEST_FAIL::IsEnabled(pHttpContext->GetTraceContext()))
    //{
    //    ANCMEvents::ANCM_EXECUTE_REQUEST_FAIL::RaiseEvent(pHttpContext->GetTraceContext(),
    //        NULL,
    //        E_APPLICATION_ACTIVATION_EXEC_FAILURE);
    //}
    pHttpContext->GetResponse()->SetStatus(500, "Internal Server Error", 0, E_APPLICATION_ACTIVATION_EXEC_FAILURE);
    return RQ_NOTIFICATION_FINISH_REQUEST;
}

VOID
IN_PROCESS_APPLICATION::SetCallbackHandles(
    _In_ PFN_REQUEST_HANDLER request_handler,
    _In_ PFN_SHUTDOWN_HANDLER shutdown_handler,
    _In_ PFN_MANAGED_CONTEXT_HANDLER async_completion_handler,
    _In_ VOID* pvRequstHandlerContext,
    _In_ VOID* pvShutdownHandlerContext
)
{
    m_RequestHandler = request_handler;
    m_RequestHandlerContext = pvRequstHandlerContext;
    m_ShutdownHandler = shutdown_handler;
    m_ShutdownHandlerContext = pvShutdownHandlerContext;
    m_AsyncCompletionHandler = async_completion_handler;

    // Initialization complete
    SetEvent(m_pInitalizeEvent);
}

// Will be called by the inprocesshandler
HRESULT
IN_PROCESS_APPLICATION::LoadManagedApplication
(
    VOID
)
{
    HRESULT    hr = S_OK;
    DWORD      dwTimeout;
    DWORD      dwResult;
    BOOL       fLocked = FALSE;
    PCWSTR     apsz[1];
    STACK_STRU(strEventMsg, 256);

    if (m_fManagedAppLoaded || m_fLoadManagedAppError)
    {
        // Core CLR has already been loaded.
        // Cannot load more than once even there was a failure
        goto Finished;
    }

    AcquireSRWLockExclusive(&m_srwLock);
    fLocked = TRUE;
    if (m_fManagedAppLoaded || m_fLoadManagedAppError)
    {
        goto Finished;
    }

    m_hThread = CreateThread(
        NULL,       // default security attributes
        0,          // default stack size
        (LPTHREAD_START_ROUTINE)ExecuteAspNetCoreProcess,
        this,       // thread function arguments
        0,          // default creation flags
        NULL);      // receive thread identifier

    if (m_hThread == NULL)
    {
        hr = HRESULT_FROM_WIN32(GetLastError());
        goto Finished;
    }

    m_pInitalizeEvent = CreateEvent(
        NULL,   // default security attributes
        TRUE,   // manual reset event
        FALSE,  // not set
        NULL);  // name

    if (m_pInitalizeEvent == NULL)
    {
        hr = HRESULT_FROM_WIN32(GetLastError());
    }

    // If the debugger is attached, never timeout
    if (IsDebuggerPresent())
    {
        dwTimeout = INFINITE;
    }
    else
    {
        dwTimeout = m_pConfig->QueryStartupTimeLimitInMS();
    }

    const HANDLE pHandles[2]{ m_hThread, m_pInitalizeEvent };

    // Wait on either the thread to complete or the event to be set
    dwResult = WaitForMultipleObjects(2, pHandles, FALSE, dwTimeout);

    // It all timed out
    if (dwResult == WAIT_TIMEOUT)
    {
        // do we need kill the backend thread
        hr = HRESULT_FROM_WIN32(dwResult);
        goto Finished;
    }
    else if (dwResult == WAIT_FAILED)
    {
        hr = HRESULT_FROM_WIN32(GetLastError());
        goto Finished;
    }

    // The thread ended it means that something failed
    if (dwResult == WAIT_OBJECT_0)
    {
        hr = E_APPLICATION_ACTIVATION_EXEC_FAILURE;
        goto Finished;
    }

    m_fManagedAppLoaded = TRUE;

Finished:
    if (fLocked)
    {
        ReleaseSRWLockExclusive(&m_srwLock);
    }

    if (FAILED(hr))
    {
        // Question: in case of application loading failure, should we allow retry on 
        // following request or block the activation at all
        m_fLoadManagedAppError = FALSE; // m_hThread != NULL ?

        if (SUCCEEDED(strEventMsg.SafeSnwprintf(
            ASPNETCORE_EVENT_LOAD_CLR_FALIURE_MSG,
            m_pConfig->QueryApplicationPath()->QueryStr(),
            m_pConfig->QueryApplicationFullPath()->QueryStr(),
            hr)))
        {
            apsz[0] = strEventMsg.QueryStr();

            //
            // not checking return code because if ReportEvent
            // fails, we cannot do anything.
            //
            if (REQUEST_HANDLER::QueryEventLog() != NULL)
            {
                ReportEventW(REQUEST_HANDLER::QueryEventLog(),
                    EVENTLOG_ERROR_TYPE,
                    0,
                    ASPNETCORE_EVENT_LOAD_CLR_FALIURE,
                    NULL,
                    1,
                    0,
                    apsz,
                    NULL);
            }
        }
    }
    return hr;
}

// static
VOID
IN_PROCESS_APPLICATION::ExecuteAspNetCoreProcess(
    _In_ LPVOID pContext
)
{

    IN_PROCESS_APPLICATION *pApplication = (IN_PROCESS_APPLICATION*)pContext;
    DBG_ASSERT(pApplication != NULL);
    pApplication->ExecuteApplication();
    //
    // no need to log the error here as if error happened, the thread will exit
    // the error will ba catched by caller LoadManagedApplication which will log an error
    //
}

// Need to pass the path to hostfxr here.
HRESULT
IN_PROCESS_APPLICATION::ExecuteApplication(
)
{
    HRESULT     hr = S_OK;

    STRU                        strApplicationFullPath;
    STRU                        struHostFxrDllLocation;
    PWSTR                       strDelimeterContext = NULL;
    PCWSTR                      pszDotnetExeLocation = NULL;
    HMODULE                     hModule;
    PCWSTR                      argv[2];
    hostfxr_main_fn             pProc;
    bool                        fFound = FALSE;


    hModule = LoadLibraryW(m_pConfig->QueryHostfxrPath()->QueryStr());

    if (hModule == NULL)
    {
        // .NET Core not installed (we can log a more detailed error message here)
        hr = ERROR_BAD_ENVIRONMENT;
        goto Finished;
    }

    // Get the entry point for main
    pProc = (hostfxr_main_fn)GetProcAddress(hModule, "hostfxr_main");
    if (pProc == NULL)
    {
        hr = ERROR_BAD_ENVIRONMENT; // better hrresult?
        goto Finished;
    }

    argv[0] = m_pConfig->QueryHostfxrPath()->QueryStr();
    UTILITY::ConvertPathToFullPath(m_pConfig->QueryArguments()->QueryStr(),
        m_pConfig->QueryApplicationFullPath()->QueryStr(),
        &strApplicationFullPath);
    argv[1] = strApplicationFullPath.QueryStr();

    // There can only ever be a single instance of .NET Core
    // loaded in the process but we need to get config information to boot it up in the
    // first place. This is happening in an execute request handler and everyone waits
    // until this initialization is done.

    // We set a static so that managed code can call back into this instance and
    // set the callbacks
    s_Application = this;
    
    RunDotnetApplication(argv, pProc);
Finished:
    //
    // this method is called by the background thread and should never exit unless shutdown
    //
    if (!m_fRecycleProcessCalled)
    {
        STRU                    strEventMsg;
        LPCWSTR                 apsz[1];
        //if (SUCCEEDED(strEventMsg.SafeSnwprintf(
        //    ASPNETCORE_EVENT_INPROCESS_THREAD_EXIT_MSG,
        //    m_pConfiguration->QueryApplicationPath()->QueryStr(),
        //    m_pConfiguration->QueryApplicationFullPath()->QueryStr(),
        //    m_ProcessExitCode
        //)))
        //{
        //    apsz[0] = strEventMsg.QueryStr();

        //    //
        //    // not checking return code because if ReportEvent
        //    // fails, we cannot do anything.
        //    //
        //    if (FORWARDING_HANDLER::QueryEventLog() != NULL)
        //    {
        //        ReportEventW(FORWARDING_HANDLER::QueryEventLog(),
        //            EVENTLOG_ERROR_TYPE,
        //            0,
        //            ASPNETCORE_EVENT_INPROCESS_THREAD_EXIT,
        //            NULL,
        //            1,
        //            0,
        //            apsz,
        //            NULL);
        //    }
        //    // error. the thread exits after application started
        //    // Question: should we shutdown current worker process or keep the application in failure state?
        //    // for now, we reccylce to keep the same behavior as that of out-of-process
        //}
        if (m_fManagedAppLoaded)
        {
            Recycle();
        }
    }
    return hr;
}

//
// Calls hostfxr_main with the hostfxr and application as arguments.
// Method should be called with only 
// Need to have __try / __except in methods that require unwinding.
// 
HRESULT
IN_PROCESS_APPLICATION::RunDotnetApplication(PCWSTR* argv, hostfxr_main_fn pProc)
{
    HRESULT hr = S_OK;
    __try
    {
        m_ProcessExitCode = pProc(2, argv);
    }
    __except (FilterException(GetExceptionCode(), GetExceptionInformation()))
    {
        // TODO Log error message here.
        hr = E_FAIL;
    }
    return hr;
}

// static
INT
IN_PROCESS_APPLICATION::FilterException(unsigned int code, struct _EXCEPTION_POINTERS *ep)
{
    // We assume that any exception is a failure as the applicaiton didn't start.
    // TODO, log error based on exception code.
    return EXCEPTION_EXECUTE_HANDLER;
}