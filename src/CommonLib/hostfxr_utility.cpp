// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

#include "stdafx.h"

HOSTFXR_UTILITY::HOSTFXR_UTILITY()
{
}

HOSTFXR_UTILITY::~HOSTFXR_UTILITY()
{
}

//
// Runs a standalone appliction.
// The folder structure looks like this:
// Application/
//   hostfxr.dll
//   Application.exe
//   Application.dll
//   etc.
// We get the full path to hostfxr.dll and Application.dll and run hostfxr_main,
// passing in Application.dll.
// Assuming we don't need Application.exe as the dll is the actual application.
//
HRESULT
HOSTFXR_UTILITY::GetStandaloneHostfxrParameters(
    HOSTFXR_PARAMETERS* pHostfxrParameters,
    ASPNETCORE_CONFIG *pConfig
)
{
    HRESULT             hr = S_OK;
    HANDLE              hFileHandle = INVALID_HANDLE_VALUE;
    STRU                struHostfxrPath;
    STRU                struExePath;
    STRU                struArguments;
    DWORD               dwPosition;

    pHostfxrParameters->QueryHostfxrLocation()->Copy(struHostfxrPath);

    // Get application path and exe path
    UTILITY::ConvertPathToFullPath(pConfig->QueryProcessPath()->QueryStr(),
        pConfig->QueryApplicationPhysicalPath()->QueryStr(),
        &struExePath);

    // Change .exe to .dll and check if file exists
    dwPosition = struExePath.LastIndexOf(L'.', 0);
    if (dwPosition == -1)
    {
        hr = ERROR_BAD_ENVIRONMENT;
        goto Finished;
    }

    struExePath.QueryStr()[dwPosition] = L'\0';

    if (FAILED(hr = struExePath.SyncWithBuffer())
        || FAILED(hr = struExePath.Append(L".dll")))
    {
        goto Finished;
    }
    
    hFileHandle = UTILITY::CheckIfFileExists(&struExePath);
    if (hFileHandle == INVALID_HANDLE_VALUE)
    {
        // Treat access issue as File not found
        hr = ERROR_FILE_NOT_FOUND;
        goto Finished;
    }

    CloseHandle(hFileHandle);

    struArguments.Copy(struExePath);
    struArguments.Append(L" ");
    struArguments.Append(pConfig->QueryArguments());

    if (FAILED(hr = GetArguments(&struArguments, &struExePath, pHostfxrParameters)))
    {
        goto Finished;
    }

Finished:
    return hr;
}

HRESULT
HOSTFXR_UTILITY::GetHostFxrParameters(
    HOSTFXR_PARAMETERS* pHostFxrParameters,
    ASPNETCORE_CONFIG *pConfig
)
{
    HRESULT hr = S_OK;

    STRU                        struSystemPathVariable;
    STRU                        struHostFxrPath;
    STRU                        strDotnetExeLocation;
    STRU                        strHostFxrSearchExpression;
    STRU                        strHighestDotnetVersion;
    HANDLE                      hFileHandle = INVALID_HANDLE_VALUE;
    std::vector<std::wstring>   vVersionFolders;
    DWORD                       dwPosition;
    DWORD                       dwLength;
    WCHAR                       pszDotnetLocation[MAX_PATH];

    // Check if the process path is an absolute path
    // then check well known locations
    // Split on ';', checking to see if dotnet.exe exists in any folders

    if ((hFileHandle = UTILITY::CheckIfFileExists(pConfig->QueryProcessPath())) != INVALID_HANDLE_VALUE)
    {
        // Done, find hostfxr.dll 
        // first check if hostfxr exists in the same folder
        UTILITY::ConvertPathToFullPath(L"hostfxr.dll", pConfig->QueryApplicationPath()->QueryStr(), &struHostFxrPath);

        if ((hFileHandle = UTILITY::CheckIfFileExists(&struHostFxrPath)) != INVALID_HANDLE_VALUE)
        {
            CloseHandle(hFileHandle);
            if (FAILED(hr = pHostFxrParameters->QueryHostfxrLocation()->Copy(struHostFxrPath)))
            {
                goto Finished;
            }

            GetStandaloneHostfxrParameters(pHostFxrParameters, pConfig);
            goto Finished;
        }
        else
        {
            UTILITY::ConvertPathToFullPath(pConfig->QueryProcessPath()->QueryStr(), pConfig->QueryApplicationPath()->QueryStr(), &strDotnetExeLocation);
        }
    }
    else if ((dwLength = SearchPath(NULL, L"dotnet", L".exe", MAX_PATH, pszDotnetLocation, NULL)) == 0)
    {
        hr = E_FAIL;
        // TODO log "Could not find dotnet. Please specify....
        goto Finished;
    }

    if (FAILED(hr = strDotnetExeLocation.Copy(pszDotnetLocation))
        || FAILED(hr = struHostFxrPath.Copy(strDotnetExeLocation)))
    {
        goto Finished;
    }

    dwPosition = struHostFxrPath.LastIndexOf(L'\\', 0);
    if (dwPosition == -1)
    {
        hr = E_FAIL;
        goto Finished;
    }

    struHostFxrPath.QueryStr()[dwPosition] = L'\0';

    if (FAILED(hr = struHostFxrPath.SyncWithBuffer())
        || FAILED(hr = struHostFxrPath.Append(L"\\")))
    {
        goto Finished;
    }

    hr = struHostFxrPath.Append(L"host\\fxr");
    if (FAILED(hr))
    {
        goto Finished;
    }

    if (!UTILITY::DirectoryExists(&struHostFxrPath))
    {
        // error, not found the folder
        hr = ERROR_BAD_ENVIRONMENT;
        goto Finished;
    }

    // Find all folders under host\\fxr\\ for version numbers.
    hr = strHostFxrSearchExpression.Copy(struHostFxrPath);
    if (FAILED(hr))
    {
        goto Finished;
    }

    hr = strHostFxrSearchExpression.Append(L"\\*");
    if (FAILED(hr))
    {
        goto Finished;
    }

    // As we use the logic from core-setup, we are opting to use std here.
    // TODO remove all uses of std?
    UTILITY::FindDotNetFolders(strHostFxrSearchExpression.QueryStr(), &vVersionFolders);

    if (vVersionFolders.size() == 0)
    {
        // no core framework was found
        hr = ERROR_BAD_ENVIRONMENT;
        goto Finished;
    }

    hr = UTILITY::FindHighestDotNetVersion(vVersionFolders, &strHighestDotnetVersion);
    if (FAILED(hr))
    {
        goto Finished;
    }

    if (FAILED(hr = struHostFxrPath.Append(L"\\"))
        || FAILED(hr = struHostFxrPath.Append(strHighestDotnetVersion.QueryStr()))
        || FAILED(hr = struHostFxrPath.Append(L"\\hostfxr.dll")))
    {
        goto Finished;
    }

    hFileHandle = UTILITY::CheckIfFileExists(&struHostFxrPath);

    if (hFileHandle == INVALID_HANDLE_VALUE)
    {
        hr = ERROR_FILE_INVALID;
        goto Finished;
    }
    
    if (FAILED(hr = GetArguments(pConfig->QueryArguments(), &strDotnetExeLocation, pHostFxrParameters)))
    {
        goto Finished;
    }

    if (FAILED(pHostFxrParameters->QueryHostfxrLocation()->Copy(struHostFxrPath)))
    {
        goto Finished;
    }

Finished:
    return hr;
}

HRESULT
HOSTFXR_UTILITY::GetArguments(STRU* struArguments, STRU* pstruExePath, HOSTFXR_PARAMETERS* pHostFxrParameters)
{
    HRESULT     hr = S_OK;
    INT         argc = 0;
    PCWSTR*     argv = NULL;
    LPWSTR*     pwzArgs = NULL;

    // First parameter to hostfxr is the exe activating it.
    pwzArgs = CommandLineToArgvW(struArguments->QueryStr(), &argc);

    argv = new PCWSTR[argc + 2];
    if (argv == NULL)
    {
        hr = E_OUTOFMEMORY;
        goto Finished;
    }

    argv[0] = SysAllocString(pstruExePath->QueryStr());
    argv[1] = SysAllocString(L"exec");
    for (INT i = 0; i < argc; i++)
    {
        argv[i + 2] = SysAllocString(pwzArgs[i]);
    }

    *pHostFxrParameters->QueryArgc() = argc + 2;
    *pHostFxrParameters->QueryArguments() = argv;

Finished:
    if (pwzArgs != NULL)
    {
        LocalFree(pwzArgs);
    }
    return hr;
}