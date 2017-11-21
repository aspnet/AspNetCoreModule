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
#include <winhttp.h>
#include <httptrace.h>
#include <cstdlib>
#include <reftrace.h>
#include <wchar.h>

#include <iiswebsocket.h>
//#include "..\IISLib\dbgutil.h"
//#include "..\IISLib\ahutil.h"
#include "..\IISLib\multisz.h"
#include "..\IISLib\multisza.h"
#include "..\IISLib\base64.h"
#include "..\CommonLib\requesthandler.h"
#include "..\CommonLib\aspnetcoreconfig.h"
#include "..\CommonLib\utility.h"
#include "..\CommonLib\application.h"
#include ".\inprocess\InProcessHandler.h"
#include ".\inprocess\inprocessapplication.h"
#include ".\inprocess\fx_ver.h"
#include ".\outofprocess\protocolconfig.h"
#include ".\outofprocess\forwarderconnection.h"
#include ".\outofprocess\serverprocess.h"
#include ".\outofprocess\processmanager.h"
#include ".\outofprocess\forwardinghandler.h"
#include ".\outofprocess\outprocessapplication.h"
//#include ".\outofprocess\websockethandler.h"

#include ".\outofprocess\sttimer.h"


#ifdef max
#undef max
template<typename T> inline T max(T a, T b)
{
    return a > b ? a : b;
}
#endif

#ifdef min
#undef min
template<typename T> inline T min(T a, T b)
{
    return a < b ? a : b;
}
#endif