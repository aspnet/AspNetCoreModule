// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

#pragma once
#pragma warning( disable : 4091)

//
// System related headers
//
#define _WINSOCKAPI_

#define NTDDI_VERSION 0x06010000
#define WINVER 0x0601
#define _WIN32_WINNT 0x0601

#include <windows.h>
#include <atlbase.h>
#include <pdh.h>
#include <vector>

#include <Shlobj.h>
#include <httpserv.h>
#include "..\CommonLib\requesthandler.h"
#include "..\CommonLib\aspnetcoreconfig.h"
#include"..\CommonLib\application.h"
#include "InProcessHandler.h"
#include "forwardinghandler.h"
#include "inprocessapplication.h"
#include "outprocessapplication.h"
