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
    if (pConfig->QueryProcessPath()->Equals(L".\\dotnet")
        || pConfig->QueryProcessPath()->Equals(L"dotnet")
        || pConfig->QueryProcessPath()->Equals(L".\\dotnet.exe")
        || pConfig->QueryProcessPath()->Equals(L"dotnet.exe"))
    {
        // hostfxr is in the same folder, parse and use it.
        hr = GetPortableHostfxrParameters(pHostFxrParameters, pConfig);
    }
    else
    {
        // Check that there is a dll name with the same name as the application name.
        // QueryProcessPath == Dll name in application
        hr = GetStandaloneHostfxrParameters(pHostFxrParameters, pConfig);
    }

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

    // Get the full path to the exe and check if it exists
    if (FAILED(hr = UTILITY::ConvertPathToFullPath(L"\\hostfxr.dll",
        pConfig->QueryApplicationPhysicalPath()->QueryStr(),
        &struHostfxrPath)))
    {
        goto Finished;
    }

    hFileHandle = UTILITY::CheckIfFileExists(&struHostfxrPath);
    if (hFileHandle == INVALID_HANDLE_VALUE)
    {
        // Treat access issue as File not found
        hr = ERROR_FILE_NOT_FOUND;
        goto Finished;
    }
    else
    {
        CloseHandle(hFileHandle);
        pHostfxrParameters->QueryHostfxrLocation()->Copy(struHostfxrPath);
    }

    // Get application path and exe path
    UTILITY::ConvertPathToFullPath(pConfig->QueryProcessPath()->QueryStr(),
        pConfig->QueryApplicationPhysicalPath()->QueryStr(),
        &struExePath);

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
        pHostfxrParameters->QueryExePath()->Copy(struExePath);
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
    PWSTR                       pwzDelimeterContext = NULL;
    PCWSTR                      pszDotnetLocation = NULL;
    PCWSTR                      pszDotnetExeString(L"dotnet.exe");
    DWORD                       dwCopyLength;
    BOOL                        fFound = FALSE;
    HANDLE                      hFileHandle = INVALID_HANDLE_VALUE;
    std::vector<std::wstring>   vVersionFolders;

    DBG_ASSERT(pHostFxrParameters != NULL);

    // Get the System PATH value.
    if (!UTILITY::GetSystemPathVariable(L"PATH", &struSystemPathVariable))
    {
        hr = ERROR_BAD_ENVIRONMENT;
        goto Finished;
    }

    // Split on ';', checking to see if dotnet.exe exists in any folders.
    pszDotnetLocation = wcstok_s(struSystemPathVariable.QueryStr(), L";", &pwzDelimeterContext);
    while (pszDotnetLocation != NULL)
    {
        dwCopyLength = (DWORD)wcsnlen_s(pszDotnetLocation, 260);

        // We store both the exe and folder locations as we eventually need to check inside of host\\fxr
        // which doesn't need the dotnet.exe portion of the string
        hr = strDotnetExeLocation.Copy(pszDotnetLocation, dwCopyLength);
        if (FAILED(hr))
        {
            goto Finished;
        }

        if (dwCopyLength > 0 && pszDotnetLocation[dwCopyLength - 1] != L'\\')
        {
            hr = strDotnetExeLocation.Append(L"\\");
            if (FAILED(hr))
            {
                goto Finished;
            }
        }

        hr = struHostfxrPath.Copy(strDotnetExeLocation);
        if (FAILED(hr))
        {
            goto Finished;
        }

        hr = strDotnetExeLocation.Append(pszDotnetExeString);
        if (FAILED(hr))
        {
            goto Finished;
        }

        hFileHandle = UTILITY::CheckIfFileExists(&strDotnetExeLocation);
        if (hFileHandle != INVALID_HANDLE_VALUE)
        {
            // means we found the folder with a dotnet.exe inside of it.
            fFound = TRUE;
            CloseHandle(hFileHandle);
            break;
        }
        pszDotnetLocation = wcstok_s(NULL, L";", &pwzDelimeterContext);
    }

    if (!fFound)
    {
        // could not find dotnet.exe, error out
        hr = ERROR_BAD_ENVIRONMENT;
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


    hr = struHostfxrPath.Append(L"\\");
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
