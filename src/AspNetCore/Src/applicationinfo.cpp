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
        // sutdown the application
        m_pApplication->ShutDown();
        m_pApplication = NULL;
    }
}

HRESULT
APPLICATION_INFO::StartMonitoringAppOffline()
{
    HRESULT hr = S_OK;
    if (m_pFileWatcherEntry != NULL)
    {
        hr = m_pFileWatcherEntry->Create(m_pConfiguration->QueryApplicationFullPath()->QueryStr(), L"app_offline.htm", this, NULL);
    }
    return hr;
}

VOID
APPLICATION_INFO::UpdateAppOfflineFileHandle()
{
    STRU strFilePath;
    UTIL::ConvertPathToFullPath(L".\\app_offline.htm", m_pConfiguration->QueryApplicationFullPath()->QueryStr(), &strFilePath);
    APP_OFFLINE_HTM *pOldAppOfflineHtm = NULL;
    APP_OFFLINE_HTM *pNewAppOfflineHtm = NULL;

    if (INVALID_FILE_ATTRIBUTES == GetFileAttributes(strFilePath.QueryStr()) && GetLastError() == ERROR_FILE_NOT_FOUND)
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

        // OnAppOfflineHandleChange();
    }
}


HRESULT
APPLICATION_INFO::EnsureApplicationCreated()
{
    HRESULT hr = S_OK;
    BOOL    fLocked = FALSE;
    APPLICATION* pApplication = NULL;
    STACK_STRU(struFileName, 300);  // >MAX_PATH

    if (m_pApplication != NULL)
    {
        goto Finished;
    }

    // load assembly and create the application
    if (m_pConfiguration->QueryHostingModel() == APP_HOSTING_MODEL::HOSTING_IN_PROCESS)
    {
        hr = LoadAssemblyFromLocalBin();
        if (FAILED(hr))
        {
            goto Finished;
        }
    }
    else
    {
        hr = LoadAssemblyFromInetsrv();
        if (FAILED(hr))
        {
            goto Finished;
        }
    }

    if (m_pApplication == NULL)
    {
        AcquireSRWLockExclusive(&m_srwLock);
        fLocked = TRUE;
        if (m_pApplication != NULL)
        {
            goto Finished;
        }

        if (m_pfnAspNetCoreCreateApplication == NULL)
        {
            hr = HRESULT_FROM_WIN32(ERROR_INVALID_FUNCTION);
            goto Finished;
        }

        hr = m_pfnAspNetCoreCreateApplication(m_pServer, m_pConfiguration, &m_pApplication);
        if (FAILED(hr))
        {
            goto Finished;
        }
    }

Finished:
    if (fLocked)
    {
        ReleaseSRWLockExclusive(&m_srwLock);
    }
    return hr;
}

HRESULT
APPLICATION_INFO::LoadAssemblyFromInetsrv()
{
    HRESULT hr = S_OK;
    BOOL    fLocked = FALSE;
    STACK_STRU(struFileName, 256);

    if (!g_fAspnetcoreRHAssemblyLoaded)
    {
        AcquireSRWLockExclusive(&g_srwLock);
        fLocked = TRUE;
        if (g_fAspnetcoreRHAssemblyLoaded)
        {
            goto Finished;
        }

        DWORD dwSize = MAX_PATH;
        BOOL  fDone = FALSE;
        // Get the path of w3wp.exe which is in the same folder as gobal aspnetcorerh.dll
        while (!fDone)
        {
            DWORD dwReturnedSize = GetModuleFileName(NULL, struFileName.QueryStr(), dwSize);
            if (dwReturnedSize == 0)
            {
                hr = HRESULT_FROM_WIN32(GetLastError());
                fDone = TRUE;
            }
            else if ((dwReturnedSize == dwSize) && (GetLastError() == ERROR_INSUFFICIENT_BUFFER))
            {
                dwSize *= 2; // smaller buffer. increase the buffer and retry
                struFileName.Resize(dwSize + 20); // aspnetcorerh.dll
            }
            else
            {
                fDone = TRUE;
            }
        }
        hr = struFileName.Append(L"\\aspnetcorerh.dll");
        if (FAILED(hr))
        {
            goto Finished;
        }

        g_hAspnetCoreRH = GetModuleHandle(struFileName.QueryStr());
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
    }



Finished:
    //
    // Question: we remember the load failure so that we will not try again.
    // User needs to check whether the fuction pointer is NULL 
    //
    g_fAspnetcoreRHAssemblyLoaded = TRUE;
    m_pfnAspNetCoreCreateApplication = g_pfnAspNetCoreCreateApplication;
    m_pfnAspNetCoreCreateRequestHandler = g_pfnAspNetCoreCreateRequestHandler;

    if (fLocked)
    {
        ReleaseSRWLockExclusive(&g_srwLock);
    }
    return hr;
}

HRESULT
APPLICATION_INFO::LoadAssemblyFromLocalBin()
{
    HRESULT hr = S_OK;
    return hr;
}

