// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

#include "precomp.hxx"

APPLICATION_INFO::~APPLICATION_INFO()
{
    if (m_pAppOfflineHtm != NULL)
    {
        m_pAppOfflineHtm->DereferenceAppOfflineHtm();
        m_pAppOfflineHtm = NULL;
    }

    if (m_pFileWatcherEntry != NULL)
    {
        // Mark the entry as invalid,
        // StopMonitor will close the file handle and trigger a FCN
        // the entry will delete itself when processing this FCN 
        m_pFileWatcherEntry->MarkEntryInValid();
        m_pFileWatcherEntry->StopMonitor();
        m_pFileWatcherEntry = NULL;
    }

    if (m_pApplication != NULL)
    {
        // shutdown the application
        m_pApplication->ShutDown();
        m_pApplication->DereferenceApplication();
        m_pApplication = NULL;
    }

    if (m_pHostFxrParameters != NULL)
    {
        delete m_pHostFxrParameters;
    }

    // configuration should be dereferenced after application shutdown
    // since the former will use it during shutdown
    if (m_pConfiguration != NULL)
    {
        // Need to dereference the configuration instance
        m_pConfiguration->DereferenceConfiguration();
        m_pConfiguration = NULL;
    }
}

HRESULT
APPLICATION_INFO::Initialize(
    _In_ ASPNETCORE_CONFIG   *pConfiguration,
    _In_ FILE_WATCHER        *pFileWatcher
)
{
    HRESULT hr = S_OK;

    DBG_ASSERT(pConfiguration);
    DBG_ASSERT(pFileWatcher);

    m_pConfiguration = pConfiguration;

    // reference the configuration instance to prevent it will be not release
    // earlier in case of configuration change and shutdown
    m_pConfiguration->ReferenceConfiguration();

    hr = m_applicationInfoKey.Initialize(pConfiguration->QueryConfigPath()->QueryStr());
    if (FAILED(hr))
    {
        goto Finished;
    }

    if (m_pFileWatcherEntry == NULL)
    {
        m_pFileWatcherEntry = new FILE_WATCHER_ENTRY(pFileWatcher);
        if (m_pFileWatcherEntry == NULL)
        {
            hr = E_OUTOFMEMORY;
            goto Finished;
        }
    }

    UpdateAppOfflineFileHandle();

Finished:
    return hr;
}

HRESULT
APPLICATION_INFO::StartMonitoringAppOffline()
{
    HRESULT hr = S_OK;
    if (m_pFileWatcherEntry != NULL)
    {
        hr = m_pFileWatcherEntry->Create(m_pConfiguration->QueryApplicationPhysicalPath()->QueryStr(), L"app_offline.htm", this, NULL);
    }
    return hr;
}

VOID
APPLICATION_INFO::UpdateAppOfflineFileHandle()
{
    STRU strFilePath;
    UTILITY::ConvertPathToFullPath(L".\\app_offline.htm", 
        m_pConfiguration->QueryApplicationPhysicalPath()->QueryStr(), 
        &strFilePath);
    APP_OFFLINE_HTM *pOldAppOfflineHtm = NULL;
    APP_OFFLINE_HTM *pNewAppOfflineHtm = NULL;

    if (INVALID_FILE_ATTRIBUTES == GetFileAttributes(strFilePath.QueryStr()) && 
        GetLastError() == ERROR_FILE_NOT_FOUND)
    {
        m_fAppOfflineFound = FALSE;
    }
    else
    {
        m_fAppOfflineFound = TRUE;
        pNewAppOfflineHtm = new APP_OFFLINE_HTM(strFilePath.QueryStr());

        if (pNewAppOfflineHtm != NULL)
        {
            if (pNewAppOfflineHtm->Load())
            {
                //
                // loaded the new app_offline.htm
                //
                pOldAppOfflineHtm = (APP_OFFLINE_HTM *)InterlockedExchangePointer((VOID**)&m_pAppOfflineHtm, pNewAppOfflineHtm);

                if (pOldAppOfflineHtm != NULL)
                {
                    pOldAppOfflineHtm->DereferenceAppOfflineHtm();
                    pOldAppOfflineHtm = NULL;
                }
            }
            else
            {
                // ignored the new app_offline file because the file does not exist.
                pNewAppOfflineHtm->DereferenceAppOfflineHtm();
                pNewAppOfflineHtm = NULL;
            }
        }

        // recycle the application
        if (m_pApplication != NULL)
        {
            m_pApplication->ShutDown();
            m_pApplication->DereferenceApplication();
            m_pApplication = NULL;
        }
    }
}

HRESULT
APPLICATION_INFO::EnsureApplicationCreated()
{
    HRESULT             hr = S_OK;
    BOOL                fLocked = FALSE;
    APPLICATION*        pApplication = NULL;
    STACK_STRU(struFileName, 300);  // >MAX_PATH
    STRU                hostFxrDllLocation;
    HOSTFXR_PARAMETERS* pHostFxrParameters = NULL;

    if (m_pApplication != NULL)
    {
        goto Finished;
    }

    hr = FindRequestHandlerAssembly(&pHostFxrParameters);
    if (FAILED(hr))
    {
        goto Finished;
    }

    if (m_pApplication == NULL)
    {
        AcquireSRWLockExclusive(&m_srwLock);
        fLocked = TRUE;
        if (m_pApplication != NULL)
        {
            goto Finished;
        }

        hr = g_pfnAspNetCoreCreateApplication(m_pServer, m_pConfiguration, pHostFxrParameters, &pApplication);
        if (FAILED(hr))
        {
            goto Finished;
        }
        m_pApplication = pApplication;
    }
    m_pHostFxrParameters = pHostFxrParameters;
Finished:
    if (fLocked)
    {
        ReleaseSRWLockExclusive(&m_srwLock);
    }
    return hr;
}

HRESULT
APPLICATION_INFO::FindRequestHandlerAssembly(_Out_ HOSTFXR_PARAMETERS** out_pHostfxrParameters)
{
    HRESULT             hr = S_OK;
    BOOL                fLocked = FALSE;
    HOSTFXR_PARAMETERS* pHostfxrParameters = NULL;
    STACK_STRU(struFileName, 256);

    if (g_fAspnetcoreRHLoadedError)
    {
        hr = E_APPLICATION_ACTIVATION_EXEC_FAILURE;
        goto Finished;
    }
    else if (!g_fAspnetcoreRHAssemblyLoaded)
    {
        AcquireSRWLockExclusive(&g_srwLock);
        fLocked = TRUE;
        if (g_fAspnetcoreRHLoadedError)
        {
            hr = E_APPLICATION_ACTIVATION_EXEC_FAILURE;
            goto Finished;
        }
        if (g_fAspnetcoreRHAssemblyLoaded)
        {
            goto Finished;
        }

        if (m_pConfiguration->QueryHostingModel() == APP_HOSTING_MODEL::HOSTING_IN_PROCESS)
        {
            pHostfxrParameters = new HOSTFXR_PARAMETERS();
            if (pHostfxrParameters == NULL)
            {
                hr = E_OUTOFMEMORY;
                goto Finished;
            }

            if (FAILED(hr = HOSTFXR_UTILITY::GetHostFxrParameters(pHostfxrParameters, m_pConfiguration)))
            {
                // TODO eventually make this fail for in process loading.

            }
            m_pHostFxrParameters = pHostfxrParameters;
            LoadManagedApplication();
            goto Finished;
        }

        g_hAspnetCoreRH = LoadLibraryW(struFileName.QueryStr());
        if (g_hAspnetCoreRH == NULL)
        {
            hr = HRESULT_FROM_WIN32(GetLastError());
            goto Finished;
        }

        g_pfnAspNetCoreCreateApplication = (PFN_ASPNETCORE_CREATE_APPLICATION)
            GetProcAddress(g_hAspnetCoreRH, "CreateApplication");
        if (g_pfnAspNetCoreCreateApplication == NULL)
        {
            hr = HRESULT_FROM_WIN32(GetLastError());
            goto Finished;
        }

        g_pfnAspNetCoreCreateRequestHandler = (PFN_ASPNETCORE_CREATE_REQUEST_HANDLER)
            GetProcAddress(g_hAspnetCoreRH, "CreateRequestHandler");
        if (g_pfnAspNetCoreCreateRequestHandler == NULL)
        {
            hr = HRESULT_FROM_WIN32(GetLastError());
            goto Finished;
        }
        g_fAspnetcoreRHAssemblyLoaded = TRUE;

        *out_pHostfxrParameters = pHostfxrParameters;
    }

Finished:
    //
    // Question: we remember the load failure so that we will not try again.
    // User needs to check whether the fuction pointer is NULL 
    //
    if (FAILED(hr))
    {
        if (pHostfxrParameters != NULL)
        {
            delete pHostfxrParameters;
        }
    }
    if (!g_fAspnetcoreRHLoadedError && FAILED(hr))
    {
        g_fAspnetcoreRHLoadedError = TRUE;
    }

    if (fLocked)
    {
        ReleaseSRWLockExclusive(&g_srwLock);
    }
    return hr;
}

HRESULT
APPLICATION_INFO::FindNativeAssemblyFromGlobalLocation(STRU* struFilename)
{
    HRESULT hr = S_OK;
    DWORD dwSize = MAX_PATH;
    BOOL  fDone = FALSE;
    DWORD dwPosition = 0;

    // Though we could call LoadLibrary(L"aspnetcorerh.dll") relying the OS to solve
    // the path (the targeted dll is the same folder of w3wp.exe/iisexpress)
    // let's still load with full path to avoid security issue
    while (!fDone)
    {
        DWORD dwReturnedSize = GetModuleFileName(NULL, struFilename->QueryStr(), dwSize);
        if (dwReturnedSize == 0)
        {
            hr = HRESULT_FROM_WIN32(GetLastError());
            fDone = TRUE;
            goto Finished;
        }
        else if ((dwReturnedSize == dwSize) && (GetLastError() == ERROR_INSUFFICIENT_BUFFER))
        {
            dwSize *= 2; // smaller buffer. increase the buffer and retry
            struFilename->Resize(dwSize + 20); // aspnetcorerh.dll
        }
        else
        {
            fDone = TRUE;
        }
    }

    if (FAILED(hr = struFilename->SyncWithBuffer()))
    {
        goto Finished;
    }
    dwPosition = struFilename->LastIndexOf(L'\\', 0);
    struFilename->QueryStr()[dwPosition] = L'\0';

    if (FAILED(hr = struFilename->SyncWithBuffer()) ||
        FAILED(hr = struFilename->Append(L"\\")) ||
        FAILED(hr = struFilename->Append(g_pwzAspnetcoreRequestHandlerName)))
    {
        goto Finished;
    }

Finished:
    return hr;
}

// Will be called by the inprocesshandler
HRESULT
APPLICATION_INFO::LoadManagedApplication
(
    VOID
)
{
    HRESULT    hr = S_OK;
    DWORD      dwTimeout;
    DWORD      dwResult;
    BOOL       fLocked = FALSE;
    //PCWSTR     apsz[1];
    //STACK_STRU(strEventMsg, 256);

    //if (m_fManagedAppLoaded || m_fLoadManagedAppError)
    //{
    //    // Core CLR has already been loaded.
    //    // Cannot load more than once even there was a failure
    //    if (m_fLoadManagedAppError)
    //    {
    //        hr = E_FAIL;
    //    }

    //    goto Finished;
    //}

    // Set up stdout redirect
    //SetStdOut();

    AcquireSRWLockExclusive(&m_srwLock);
    fLocked = TRUE;
    //if (m_fManagedAppLoaded || m_fLoadManagedAppError)
    //{
    //    if (m_fLoadManagedAppError)
    //    {
    //        hr = E_FAIL;
    //    }

    //    goto Finished;
    //}

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
        dwTimeout = m_pConfiguration->QueryStartupTimeLimitInMS();
    }

    const HANDLE pHandles[2]{ m_hThread, m_pInitalizeEvent };

    // Wait on either the thread to complete or the event to be set
    dwResult = WaitForMultipleObjects(2, pHandles, FALSE, dwTimeout);

    // It all timed out
    if (dwResult == WAIT_TIMEOUT)
    {
        // kill the backend thread as loading dotnet timedout
        TerminateThread(m_hThread, 0);
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

    //m_fManagedAppLoaded = TRUE;

Finished:

    if (FAILED(hr))
    {
        // Question: in case of application loading failure, should we allow retry on 
        // following request or block the activation at all
        //m_fLoadManagedAppError = TRUE; // m_hThread != NULL ?

                                       // TODO
                                       //if (SUCCEEDED(strEventMsg.SafeSnwprintf(
                                       //    ASPNETCORE_EVENT_LOAD_CLR_FALIURE_MSG,
                                       //    m_pConfiguration->QueryApplicationPath()->QueryStr(),
                                       //    m_pConfiguration->QueryApplicationPhysicalPath()->QueryStr(),
                                       //    hr)))
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
                                       //            ASPNETCORE_EVENT_LOAD_CLR_FALIURE,
                                       //            NULL,
                                       //            1,
                                       //            0,
                                       //            apsz,
                                       //            NULL);
                                       //    }
                                       //}
    }

    if (fLocked)
    {
        ReleaseSRWLockExclusive(&m_srwLock);
    }

    return hr;
}

HRESULT
APPLICATION_INFO::ExecuteApplication(
    VOID
)
{
    HRESULT             hr = S_OK;
    HMODULE             hModule;
    hostfxr_main_fn     pProc;

    // should be a redudant call here, but we will be safe and call it twice.
    // TODO AV here on m_pHostFxrParameters being null
    hModule = LoadLibraryW(m_pHostFxrParameters->QueryHostfxrLocation()->QueryStr());

    if (hModule == NULL)
    {
        // .NET Core not installed (we can log a more detailed error message here)
        hr = ERROR_BAD_ENVIRONMENT;
        goto Finished;
    }

    // Get the entry point for main
    pProc = (hostfxr_main_fn)GetProcAddress(hModule, "hostfxr_main_with_callback");
    if (pProc == NULL)
    {
        hr = ERROR_BAD_ENVIRONMENT;
        goto Finished;
    }

    // There can only ever be a single instance of .NET Core
    // loaded in the process but we need to get config information to boot it up in the
    // first place. This is happening in an execute request handler and everyone waits
    // until this initialization is done.

    // We set a static so that managed code can call back into this instance and
    // set the callbacks

    RunDotnetApplication(*m_pHostFxrParameters->QueryArgc(), *m_pHostFxrParameters->QueryArguments(), pProc);

Finished:
    //
    // this method is called by the background thread and should never exit unless shutdown
    //
    return hr;
}


//
// Calls hostfxr_main with the hostfxr and application as arguments.
// Method should be called with only 
// Need to have __try / __except in methods that require unwinding.
// Note, this will not 
// 
HRESULT
APPLICATION_INFO::RunDotnetApplication(CONST DWORD argc, PCWSTR* argv, hostfxr_main_fn pProc)
{
    HRESULT hr = S_OK;
    __try
    {
        pProc(argc, argv, CallbackFromHostfxr);
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
APPLICATION_INFO::FilterException(unsigned int, struct _EXCEPTION_POINTERS*)
{
    // We assume that any exception is a failure as the applicaiton didn't start or there was a startup error.
    // TODO, log error based on exception code.
    return EXCEPTION_EXECUTE_HANDLER;
}

int 
APPLICATION_INFO::CallbackFromHostfxr(const int argc, const WCHAR* argv[])
{
    HRESULT hr = S_OK;
    STRU struNativeSearchPaths;
    STRU nativeDllLocation;
    DWORD index = 0;
    DWORD prevIndex = 0;
    HANDLE nativeRequestHandlerHandle = INVALID_HANDLE_VALUE;
    BOOL fFound = FALSE;
    if (argc <= 2)
    {
        return 1;
    }

    struNativeSearchPaths.Copy(argv[2]);
    while ((index = struNativeSearchPaths.IndexOf(L";", prevIndex)) != -1)
    {
        if (FAILED(hr = nativeDllLocation.Copy(struNativeSearchPaths.QueryStr(), index - prevIndex)))
        {
            goto Finished;
        }
        if (!nativeDllLocation.EndsWith(L"\\"))
        {
            hr = nativeDllLocation.Append(L"\\");
            if (FAILED(hr))
            {
                goto Finished;
            }
        }

        hr = nativeDllLocation.Append(g_pwzAspnetcoreRequestHandlerName);
        if (FAILED(hr))
        {
            goto Finished;
        }
        nativeRequestHandlerHandle = UTILITY::CheckIfFileExists(&nativeDllLocation);

        if (nativeRequestHandlerHandle != INVALID_HANDLE_VALUE)
        {
            fFound = TRUE;
            CloseHandle(nativeRequestHandlerHandle);
            break;
        }
        prevIndex = index + 1;
    }

    if (!fFound)
    {
        hr = FindNativeAssemblyFromGlobalLocation(&nativeDllLocation);
        if (FAILED(hr))
        {
            goto Finished;
        }
    }

    g_hAspnetCoreRH = LoadLibraryW(nativeDllLocation.QueryStr());
    if (g_hAspnetCoreRH == NULL)
    {
        hr = HRESULT_FROM_WIN32(GetLastError());
        goto Finished;
    }

    g_pfnAspNetCoreCreateApplication = (PFN_ASPNETCORE_CREATE_APPLICATION)
        GetProcAddress(g_hAspnetCoreRH, "CreateApplication");
    if (g_pfnAspNetCoreCreateApplication == NULL)
    {
        hr = HRESULT_FROM_WIN32(GetLastError());
        goto Finished;
    }

    g_pfnAspNetCoreCreateRequestHandler = (PFN_ASPNETCORE_CREATE_REQUEST_HANDLER)
        GetProcAddress(g_hAspnetCoreRH, "CreateRequestHandler");
    if (g_pfnAspNetCoreCreateRequestHandler == NULL)
    {
        hr = HRESULT_FROM_WIN32(GetLastError());
        goto Finished;
    }
    g_fAspnetcoreRHAssemblyLoaded = TRUE;


Finished:
    //
    // Question: we remember the load failure so that we will not try again.
    // User needs to check whether the fuction pointer is NULL 
    //
    /*if (FAILED(hr))
    {
        if (m_pHostFxrParameters != NULL)
        {
            delete m_pHostFxrParameters;
        }
    }*/
    if (!g_fAspnetcoreRHLoadedError && FAILED(hr))
    {
        g_fAspnetcoreRHLoadedError = TRUE;
    }

    ReleaseSRWLockExclusive(&g_srwLock);
    return hr;
}

// static
VOID
APPLICATION_INFO::ExecuteAspNetCoreProcess(
    _In_ LPVOID pContext
)
{
    HRESULT hr = S_OK;
    APPLICATION_INFO *pApplication = (APPLICATION_INFO*)pContext;
    DBG_ASSERT(pApplication != NULL);
    hr = pApplication->ExecuteApplication();
    //
    // no need to log the error here as if error happened, the thread will exit
    // the error will ba catched by caller LoadManagedApplication which will log an error
    //

}

