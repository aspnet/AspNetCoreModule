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
// Determines where hostfxr is and the parameters needed 
// For a portable application:
//  hostfxr location = in the dotnet folder
//      arg[0] = path to .exe (found when finding hostfxr)
//      arg[1] = application .dll and other arguments
HRESULT
HOSTFXR_UTILITY::GetHostFxrParameters(
    HOSTFXR_PARAMETERS* pHostFxrParameters,
    ASPNETCORE_CONFIG *pConfig
)
{
    HRESULT hr = S_OK;

    // If the process path isn't dotnet, assume we are a standalone appliction.
    // TODO: this should be a path equivalent check
        // hostfxr is in the same folder, parse and use it.
    hr = GetPortableHostfxrParameters(pHostFxrParameters, pConfig);

    return hr;
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
    DWORD               dwPosition;

    pHostfxrParameters->QueryHostfxrLocation()->Copy(struHostfxrPath);

    // Get application path and exe path
    UTILITY::ConvertPathToFullPath(pConfig->QueryProcessPath()->QueryStr(),
        pConfig->QueryApplicationPhysicalPath()->QueryStr(),
        &struExePath);

  
    if (FAILED(hr = pHostfxrParameters->QueryExePath()->Copy(struExePath)))
    {
        goto Finished;
    }

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
    else
    {
        CloseHandle(hFileHandle);
        pHostfxrParameters->QueryArguments()->Copy(struExePath);
        pHostfxrParameters->QueryArguments()->Append(L" ");
        pHostfxrParameters->QueryArguments()->Append(pConfig->QueryArguments());
    }
Finished:
    return hr;
}

HRESULT
HOSTFXR_UTILITY::GetPortableHostfxrParameters(
    HOSTFXR_PARAMETERS* pHostFxrParameters,
    ASPNETCORE_CONFIG *pConfig
)
{
    HRESULT hr = S_OK;

    STRU                        struSystemPathVariable;
    STRU                        struHostfxrPath;
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
        UTILITY::ConvertPathToFullPath(L"hostfxr.dll", pConfig->QueryApplicationPath()->QueryStr(), &struHostfxrPath);

        if ((hFileHandle = UTILITY::CheckIfFileExists(&struHostfxrPath)) != INVALID_HANDLE_VALUE)
        {
            CloseHandle(hFileHandle);
            if (FAILED(hr = pHostFxrParameters->QueryHostfxrLocation()->Copy(struHostfxrPath)))
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
        || FAILED(hr = struHostfxrPath.Copy(strDotnetExeLocation)))
    {
        goto Finished;
    }

    dwPosition = struHostfxrPath.LastIndexOf(L'\\', 0);
    if (dwPosition == -1)
    {
        hr = E_FAIL;
        goto Finished;
    }

    struHostfxrPath.QueryStr()[dwPosition] = L'\0';

    if (FAILED(hr = struHostfxrPath.SyncWithBuffer()) ||
        FAILED(hr = struHostfxrPath.Append(L"\\")))
    {
        goto Finished;
    }

    hr = struHostfxrPath.Append(L"host\\fxr");
    if (FAILED(hr))
    {
        goto Finished;
    }

    if (!UTILITY::DirectoryExists(&struHostfxrPath))
    {
        // error, not found the folder
        hr = ERROR_BAD_ENVIRONMENT;
        goto Finished;
    }

    // Find all folders under host\\fxr\\ for version numbers.
    hr = strHostFxrSearchExpression.Copy(struHostfxrPath);
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

    if (FAILED(hr = struHostfxrPath.Append(L"\\"))
        || FAILED(hr = struHostfxrPath.Append(strHighestDotnetVersion.QueryStr()))
        || FAILED(hr = struHostfxrPath.Append(L"\\hostfxr.dll")))
    {
        goto Finished;
    }

    hFileHandle = UTILITY::CheckIfFileExists(&struHostfxrPath);

    if (hFileHandle == INVALID_HANDLE_VALUE)
    {
        hr = ERROR_FILE_INVALID;
        goto Finished;
    }

    if (FAILED(pHostFxrParameters->QueryHostfxrLocation()->Copy(struHostfxrPath))
        || FAILED(pHostFxrParameters->QueryExePath()->Copy(strDotnetExeLocation))
        || FAILED(pHostFxrParameters->QueryArguments()->Copy(pConfig->QueryArguments())))
    {
        goto Finished;
    }

Finished:
    return hr;
}