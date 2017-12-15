// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

#pragma once

#define DEFAULT_HASH_BUCKETS 293

//
// This class will manage the lifecycle of all Asp.Net Core applciation
// It should be global singleton.
// Should always call GetInstance to get the object instance
//
class APPLICATION_MANAGER
{
public:

    static
    APPLICATION_MANAGER*
    GetInstance(
        VOID
    )
    {
        if ( sm_pApplicationManager == NULL )
        {
            sm_pApplicationManager = new APPLICATION_MANAGER();
        }

        return sm_pApplicationManager;
    }

    static
    VOID
    Cleanup(
        VOID
    )
    {
        if(sm_pApplicationManager != NULL)
        {
            delete sm_pApplicationManager;
            sm_pApplicationManager = NULL;
        }
    }

    HRESULT
    GetApplicationInfo(
        _In_ IHttpServer*          pServer,
        _In_ ASPNETCORE_CONFIG*    pConfig,
        _Out_ APPLICATION_INFO **  ppApplicationInfo
    );

    HRESULT
    RecycleApplication(
        _In_ LPCWSTR pszApplicationId
    );

    VOID
    ShutDown();

    ~APPLICATION_MANAGER()
    {
        if(m_pApplicationInfoHash != NULL)
        {
            m_pApplicationInfoHash->Clear();
            delete m_pApplicationInfoHash;
            m_pApplicationInfoHash = NULL;
        }

        if( m_pFileWatcher!= NULL )
        {
            delete m_pFileWatcher;
            m_pFileWatcher = NULL;
        }
    }

    FILE_WATCHER*
    GetFileWatcher()
    {
        return m_pFileWatcher;
    }

    HRESULT Initialize()
    {
        HRESULT hr = S_OK;

        if(m_pApplicationInfoHash == NULL)
        {
            m_pApplicationInfoHash = new APPLICATION_INFO_HASH();
            if(m_pApplicationInfoHash == NULL)
            {
                hr = E_OUTOFMEMORY;
                goto Finished;
            }

            hr = m_pApplicationInfoHash->Initialize(DEFAULT_HASH_BUCKETS);
            if(FAILED(hr))
            {
                goto Finished;
            }
        }

        if( m_pFileWatcher == NULL )
        {
            m_pFileWatcher = new FILE_WATCHER;
            if(m_pFileWatcher == NULL)
            {
                hr = HRESULT_FROM_WIN32( ERROR_NOT_ENOUGH_MEMORY );
                goto Finished;
            }

            m_pFileWatcher->Create();
        }

    Finished:
        return hr;
    }

private:
    //
    // we currently limit the size of m_pstrErrorInfo to 5000, be careful if you want to change its payload
    // 
    APPLICATION_MANAGER() : m_pApplicationInfoHash(NULL), 
        m_pFileWatcher(NULL),
        m_hostingModel(HOSTING_UNKNOWN),
        m_fInShutdown(FALSE)
    {
        InitializeSRWLock(&m_srwLock);
    }

    FILE_WATCHER               *m_pFileWatcher;
    APPLICATION_INFO_HASH      *m_pApplicationInfoHash;
    static APPLICATION_MANAGER *sm_pApplicationManager;
    SRWLOCK                     m_srwLock;
    APP_HOSTING_MODEL          m_hostingModel;
    bool                       m_fInShutdown;
};