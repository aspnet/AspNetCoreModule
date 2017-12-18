// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

#include "precomp.hxx"

ASPNETCORE_CONFIG::~ASPNETCORE_CONFIG()
{
    if (QueryHostingModel() == HOSTING_IN_PROCESS &&
        !g_fRecycleProcessCalled &&
        (g_pHttpServer->GetAdminManager() != NULL))
    {
        // There is a bug in IHttpServer::RecycleProcess. It will hit AV when worker process
        // has already been in recycling state.
        // To workaround, do null check on GetAdminManager(). If it is NULL, worker process is in recycling
        // Do not call RecycleProcess again

        // RecycleProcess can olny be called once
        // In case of configuration change for in-process app
        // We want notify IIS first to let new request routed to new worker process

        g_pHttpServer->RecycleProcess(L"AspNetCore Recycle Process on Configuration Change");
    }

    // It's safe for us to set this g_fRecycleProcessCalled
    // as in_process scenario will always recycle the worker process for configuration change
    g_fRecycleProcessCalled = TRUE;

    m_struApplicationFullPath.Reset();
    if (m_pEnvironmentVariables != NULL)
    {
        m_pEnvironmentVariables->Clear();
        delete m_pEnvironmentVariables;
        m_pEnvironmentVariables = NULL;
    }

    if (!m_struApplication.IsEmpty())
    {
        APPLICATION_MANAGER::GetInstance()->RecycleApplication(m_struApplication.QueryStr());
    }

    if (QueryHostingModel() == HOSTING_IN_PROCESS && 
        g_pHttpServer->IsCommandLineLaunch())
    {
        // IISExpress scenario, only option is to call exit in case configuration change
        // as CLR or application may change
        exit(0);
    }
}

HRESULT
ASPNETCORE_CONFIG::CreateLogFile(
    _In_  BOOL    fIgnoreDisableInConfig,
    _Out_ STRU*   pstruFullFileName,
    _Out_ HANDLE* pHandle
)
{
    HRESULT hr = HRESULT_FROM_NT(ERROR_NOT_SUPPORTED); // Assume log is not enabled
    SECURITY_ATTRIBUTES     saAttr = { 0 };
    SYSTEMTIME              systemTime;
    STRU    struPath;

    DBG_ASSERT(pstruFullFileName);
    DBG_ASSERT(pHandle);

    *pHandle = INVALID_HANDLE_VALUE;
    if (fIgnoreDisableInConfig || m_fStdoutLogEnabled)
    {
        STRU   struTmp;
        STRU   struLogPath;
        WCHAR* strSegment = NULL;
        WCHAR* strRestSegments = NULL;
        hr = struTmp.Copy(m_struStdoutLogFile.QueryStr());
        if (FAILED(hr))
        {
            goto Finished;
        }

        //
        // Check whether the (first) segment exists based on configuration element 'stdoutLogFile'
        // If not, create that folder. We only want make the default '.\logs\stdout' work.
        // No recursive folder creation
        //
        strSegment = wcstok_s(struTmp.QueryStr(), L"\\", &strRestSegments);
        if (strSegment != NULL && (strSegment[0] == L'.') && strSegment[1] == L'\0')
        {
            struLogPath.Append(L".\\");
            strSegment = wcstok_s(NULL, L"\\", &strRestSegments);
            if (strSegment != NULL)
            {
                struLogPath.Append(strSegment);
            }
            strSegment = struLogPath.QueryStr();
        }
        if (strSegment != NULL)
        {
            hr = PATH::ConvertPathToFullPath(
                strSegment,
                m_struApplicationFullPath.QueryStr(),
                &struPath);
            if (FAILED(hr))
            {
                goto Finished;
            }
            if (!CreateDirectory(struPath.QueryStr(), NULL) &&
                ERROR_ALREADY_EXISTS != GetLastError())
            {
                hr = HRESULT_FROM_WIN32(GetLastError());
                goto Finished;
            }
        }
        struPath.Reset();
        hr = PATH::ConvertPathToFullPath(
            m_struStdoutLogFile.QueryStr(),
            m_struApplicationFullPath.QueryStr(),
            &struPath);
        if (FAILED(hr))
        {
            goto Finished;
        }

        GetSystemTime(&systemTime);
        hr = pstruFullFileName->SafeSnwprintf(L"%s_%d%02d%02d%02d%02d%02d_%d.log",
                struPath.QueryStr(),
                systemTime.wYear,
                systemTime.wMonth,
                systemTime.wDay,
                systemTime.wHour,
                systemTime.wMinute,
                systemTime.wSecond,
                GetCurrentProcessId());
        if (FAILED(hr))
        {
           goto Finished;
        }

        saAttr.nLength = sizeof(SECURITY_ATTRIBUTES);
        saAttr.bInheritHandle = TRUE;
        saAttr.lpSecurityDescriptor = NULL;

        *pHandle = CreateFileW(pstruFullFileName->QueryStr(),
                FILE_WRITE_DATA,
                FILE_SHARE_READ,
                &saAttr,
                CREATE_ALWAYS,
                FILE_ATTRIBUTE_NORMAL,
                NULL);
        if (*pHandle == INVALID_HANDLE_VALUE)
        {
            hr = HRESULT_FROM_WIN32(GetLastError());
            goto Finished;
        }
        hr = S_OK;
    }
Finished:
    return hr;
}

HRESULT
ASPNETCORE_CONFIG::GetConfig(
    _In_  IHttpContext            *pHttpContext,
    _Out_ ASPNETCORE_CONFIG     **ppAspNetCoreConfig
)
{
    HRESULT                 hr = S_OK;
    IHttpApplication       *pHttpApplication = pHttpContext->GetApplication();
    ASPNETCORE_CONFIG      *pAspNetCoreConfig = NULL;

    if (ppAspNetCoreConfig == NULL)
    {
        hr = E_INVALIDARG;
        goto Finished;
    }

    *ppAspNetCoreConfig = NULL;

    // potential bug if user sepcific config at virtual dir level
    pAspNetCoreConfig = (ASPNETCORE_CONFIG*)
        pHttpApplication->GetModuleContextContainer()->GetModuleContext(g_pModuleId);

    if (pAspNetCoreConfig != NULL)
    {
        *ppAspNetCoreConfig = pAspNetCoreConfig;
        pAspNetCoreConfig = NULL;
        goto Finished;
    }

    pAspNetCoreConfig = new ASPNETCORE_CONFIG;
    if (pAspNetCoreConfig == NULL)
    {
        hr = E_OUTOFMEMORY;
        goto Finished;
    }

    hr = pAspNetCoreConfig->Populate(pHttpContext);
    if (FAILED(hr))
    {
        goto Finished;
    }

    hr = pHttpApplication->GetModuleContextContainer()->
        SetModuleContext(pAspNetCoreConfig, g_pModuleId);
    if (FAILED(hr))
    {
        if (hr == HRESULT_FROM_WIN32(ERROR_ALREADY_ASSIGNED))
        {
            delete pAspNetCoreConfig;

            pAspNetCoreConfig = (ASPNETCORE_CONFIG*)pHttpApplication->
                                 GetModuleContextContainer()->
                                 GetModuleContext(g_pModuleId);

            _ASSERT(pAspNetCoreConfig != NULL);

            hr = S_OK;
        }
        else
        {
            goto Finished;
        }
    }
    else
    {
        // set appliction info here instead of inside Populate()
        // as the destructor will delete the backend process
        hr = pAspNetCoreConfig->QueryApplicationPath()->Copy(pHttpApplication->GetApplicationId());
        if (FAILED(hr))
        {
            goto Finished;
        }
    }

    *ppAspNetCoreConfig = pAspNetCoreConfig;
    pAspNetCoreConfig = NULL;

Finished:

    if (pAspNetCoreConfig != NULL)
    {
        delete pAspNetCoreConfig;
        pAspNetCoreConfig = NULL;
    }

    return hr;
}

HRESULT
ASPNETCORE_CONFIG::Populate(
    IHttpContext   *pHttpContext
)
{
    HRESULT                         hr = S_OK;
    STACK_STRU(strSiteConfigPath, 256);
    STRU                            strEnvName;
    STRU                            strEnvValue;
    STRU                            strExpandedEnvValue;
    STRU                            strApplicationFullPath;
    IAppHostAdminManager           *pAdminManager = NULL;
    IAppHostElement                *pAspNetCoreElement = NULL;
    IAppHostElement                *pWindowsAuthenticationElement = NULL;
    IAppHostElement                *pBasicAuthenticationElement = NULL;
    IAppHostElement                *pAnonymousAuthenticationElement = NULL;
    IAppHostElement                *pEnvVarList = NULL;
    IAppHostElement                *pEnvVar = NULL;
    IAppHostElementCollection      *pEnvVarCollection = NULL;
    ULONGLONG                       ullRawTimeSpan = 0;
    ENUM_INDEX                      index;
    ENVIRONMENT_VAR_ENTRY*          pEntry = NULL;
    DWORD                           dwCounter = 0;
    DWORD                           dwPosition = 0;
    WCHAR*                          pszPath = NULL;

    m_pEnvironmentVariables = new ENVIRONMENT_VAR_HASH();
    if (m_pEnvironmentVariables == NULL)
    {
        hr = E_OUTOFMEMORY;
        goto Finished;
    }
    if (FAILED(hr = m_pEnvironmentVariables->Initialize(37 /*prime*/)))
    {
        delete m_pEnvironmentVariables;
        m_pEnvironmentVariables = NULL;
        goto Finished;
    }

    pAdminManager = g_pHttpServer->GetAdminManager();
    hr = strSiteConfigPath.Copy(pHttpContext->GetApplication()->GetAppConfigPath());
    if (FAILED(hr))
    {
        goto Finished;
    }

    hr = m_struApplicationFullPath.Copy(pHttpContext->GetApplication()->GetApplicationPhysicalPath());
    if (FAILED(hr))
    {
        goto Finished;
    }

    pszPath = strSiteConfigPath.QueryStr();
    while (pszPath[dwPosition] != NULL)
    {
        if (pszPath[dwPosition] == '/')
        {
            dwCounter++;
            if (dwCounter == 4)
                break;
        }
        dwPosition++;
    }

    if (dwCounter == 4)
    {
        hr = m_struApplicationVirtualPath.Copy(pszPath + dwPosition);
    }
    else
    {
        hr = m_struApplicationVirtualPath.Copy(L"/");
    }

    // Will setup the application virtual path.
    if (FAILED(hr))
    {
        goto Finished;
    }

    hr = pAdminManager->GetAdminSection(CS_WINDOWS_AUTHENTICATION_SECTION,
        strSiteConfigPath.QueryStr(),
        &pWindowsAuthenticationElement);
    if (FAILED(hr))
    {
        // assume the corresponding authen was not enabled
        // as the section may get deleted by user in some HWC case
        // ToDo: log a warning to event log
        m_fWindowsAuthEnabled = FALSE;
    }
    else
    {
        hr = GetElementBoolProperty(pWindowsAuthenticationElement,
            CS_AUTHENTICATION_ENABLED,
            &m_fWindowsAuthEnabled);
        if (FAILED(hr))
        {
            goto Finished;
        }
    }

    hr = pAdminManager->GetAdminSection(CS_BASIC_AUTHENTICATION_SECTION,
        strSiteConfigPath.QueryStr(),
        &pBasicAuthenticationElement);
    if (FAILED(hr))
    {
        m_fBasicAuthEnabled = FALSE;
    }
    else
    {
        hr = GetElementBoolProperty(pBasicAuthenticationElement,
            CS_AUTHENTICATION_ENABLED,
            &m_fBasicAuthEnabled);
        if (FAILED(hr))
        {
            goto Finished;
        }
    }

    hr = pAdminManager->GetAdminSection(CS_ANONYMOUS_AUTHENTICATION_SECTION,
        strSiteConfigPath.QueryStr(),
        &pAnonymousAuthenticationElement);
    if (FAILED(hr))
    {
        m_fAnonymousAuthEnabled = FALSE;
    }
    else
    {
        hr = GetElementBoolProperty(pAnonymousAuthenticationElement,
            CS_AUTHENTICATION_ENABLED,
            &m_fAnonymousAuthEnabled);
        if (FAILED(hr))
        {
            goto Finished;
        }
    }

    hr = pAdminManager->GetAdminSection(CS_ASPNETCORE_SECTION,
        strSiteConfigPath.QueryStr(),
        &pAspNetCoreElement);
    if (FAILED(hr))
    {
        goto Finished;
    }

    hr = GetElementStringProperty(pAspNetCoreElement,
        CS_ASPNETCORE_PROCESS_EXE_PATH,
        &m_struProcessPath);
    if (FAILED(hr))
    {
        goto Finished;
    }

    hr = GetElementStringProperty(pAspNetCoreElement,
        CS_ASPNETCORE_HOSTING_MODEL,
        &m_strHostingModel);
    if (FAILED(hr))
    {
        // Swallow this error for backward compatability
        // Use default behavior for empty string
        hr = S_OK;
    }

    if (m_strHostingModel.IsEmpty() || m_strHostingModel.Equals(L"outofprocess", TRUE))
    {
        m_hostingModel = HOSTING_OUT_PROCESS;
    }
    else if (m_strHostingModel.Equals(L"inprocess", TRUE))
    {
        m_hostingModel = HOSTING_IN_PROCESS;
    }
    else
    {
        // block unknown hosting value
        hr = HRESULT_FROM_WIN32(ERROR_NOT_SUPPORTED);
        goto Finished;
    }

    hr = GetElementStringProperty(pAspNetCoreElement,
        CS_ASPNETCORE_PROCESS_ARGUMENTS,
        &m_struArguments);
    if (FAILED(hr))
    {
        goto Finished;
    }

    hr = GetElementDWORDProperty(pAspNetCoreElement,
        CS_ASPNETCORE_RAPID_FAILS_PER_MINUTE,
        &m_dwRapidFailsPerMinute);
    if (FAILED(hr))
    {
        goto Finished;
    }

    //
    // rapidFailsPerMinute cannot be greater than 100.
    //
    if (m_dwRapidFailsPerMinute > MAX_RAPID_FAILS_PER_MINUTE)
    {
        m_dwRapidFailsPerMinute = MAX_RAPID_FAILS_PER_MINUTE;
    }

    hr = GetElementDWORDProperty(pAspNetCoreElement,
        CS_ASPNETCORE_PROCESSES_PER_APPLICATION,
        &m_dwProcessesPerApplication);
    if (FAILED(hr))
    {
        goto Finished;
    }

    hr = GetElementDWORDProperty(
        pAspNetCoreElement,
        CS_ASPNETCORE_PROCESS_STARTUP_TIME_LIMIT,
        &m_dwStartupTimeLimitInMS
    );
    if (FAILED(hr))
    {
        goto Finished;
    }

    m_dwStartupTimeLimitInMS *= MILLISECONDS_IN_ONE_SECOND;

    hr = GetElementDWORDProperty(
        pAspNetCoreElement,
        CS_ASPNETCORE_PROCESS_SHUTDOWN_TIME_LIMIT,
        &m_dwShutdownTimeLimitInMS
    );
    if (FAILED(hr))
    {
        goto Finished;
    }
    m_dwShutdownTimeLimitInMS *= MILLISECONDS_IN_ONE_SECOND;

    hr = GetElementBoolProperty(pAspNetCoreElement,
        CS_ASPNETCORE_FORWARD_WINDOWS_AUTH_TOKEN,
        &m_fForwardWindowsAuthToken);
    if (FAILED(hr))
    {
        goto Finished;
    }

    hr = GetElementBoolProperty(pAspNetCoreElement,
        CS_ASPNETCORE_DISABLE_START_UP_ERROR_PAGE,
        &m_fDisableStartUpErrorPage);
    if (FAILED(hr))
    {
        goto Finished;
    }

    hr = GetElementRawTimeSpanProperty(
        pAspNetCoreElement,
        CS_ASPNETCORE_WINHTTP_REQUEST_TIMEOUT,
        &ullRawTimeSpan
    );
    if (FAILED(hr))
    {
        goto Finished;
    }

    m_dwRequestTimeoutInMS = (DWORD)TIMESPAN_IN_MILLISECONDS(ullRawTimeSpan);

    hr = GetElementBoolProperty(pAspNetCoreElement,
        CS_ASPNETCORE_STDOUT_LOG_ENABLED,
        &m_fStdoutLogEnabled);
    if (FAILED(hr))
    {
        goto Finished;
    }
	hr = GetElementStringProperty(pAspNetCoreElement,
		CS_ASPNETCORE_STDOUT_LOG_FILE,
		&m_struStdoutLogFile);
	if (FAILED(hr))
	{
		goto Finished;
	}

    hr = GetElementChildByName(pAspNetCoreElement,
        CS_ASPNETCORE_ENVIRONMENT_VARIABLES,
        &pEnvVarList);
    if (FAILED(hr))
    {
        goto Finished;
    }

    hr = pEnvVarList->get_Collection(&pEnvVarCollection);
    if (FAILED(hr))
    {
        goto Finished;
    }

    for (hr = FindFirstElement(pEnvVarCollection, &index, &pEnvVar);
        SUCCEEDED(hr);
        hr = FindNextElement(pEnvVarCollection, &index, &pEnvVar))
    {
        if (hr == S_FALSE)
        {
            hr = S_OK;
            break;
        }

        if (FAILED(hr = GetElementStringProperty(pEnvVar,
            CS_ASPNETCORE_ENVIRONMENT_VARIABLE_NAME,
            &strEnvName)) ||
            FAILED(hr = GetElementStringProperty(pEnvVar,
                CS_ASPNETCORE_ENVIRONMENT_VARIABLE_VALUE,
                &strEnvValue)) ||
            FAILED(hr = strEnvName.Append(L"=")) ||
            FAILED(hr = STRU::ExpandEnvironmentVariables(strEnvValue.QueryStr(), &strExpandedEnvValue)))
        {
            goto Finished;
        }

        pEntry = new ENVIRONMENT_VAR_ENTRY();
        if (pEntry == NULL)
        {
            hr = E_OUTOFMEMORY;
            goto Finished;
        }

        if (FAILED(hr = pEntry->Initialize(strEnvName.QueryStr(), strExpandedEnvValue.QueryStr())) ||
            FAILED(hr = m_pEnvironmentVariables->InsertRecord(pEntry)))
        {
            goto Finished;
        }
        strEnvName.Reset();
        strEnvValue.Reset();
        strExpandedEnvValue.Reset();
        pEnvVar->Release();
        pEnvVar = NULL;
        pEntry->Dereference();
        pEntry = NULL;
    }

Finished:

    if (pAspNetCoreElement != NULL)
    {
        pAspNetCoreElement->Release();
        pAspNetCoreElement = NULL;
    }

    if (pEnvVarList != NULL)
    {
        pEnvVarList->Release();
        pEnvVarList = NULL;
    }

    if (pEnvVar != NULL)
    {
        pEnvVar->Release();
        pEnvVar = NULL;
    }

    if (pEnvVarCollection != NULL)
    {
        pEnvVarCollection->Release();
        pEnvVarCollection = NULL;
    }

    if (pEntry != NULL)
    {
        pEntry->Dereference();
        pEntry = NULL;
    }

    return hr;
}