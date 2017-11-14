#include "stdafx.h"
#include "requesthandler.h"

REQUEST_HANDLER::REQUEST_HANDLER(
    _In_ IHttpContext *pW3Context,
    _In_ APPLICATION  *pApplication)
    : m_cRefs(1)
{
    //InitializeSRWLock(&m_srwLock);
}


REQUEST_HANDLER::~REQUEST_HANDLER()
{
}