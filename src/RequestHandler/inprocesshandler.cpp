#include "precomp.hxx"

IN_PROCESS_HANDLER::IN_PROCESS_HANDLER(
    _In_ IHttpContext *pW3Context,
    _In_ APPLICATION  *pApplication
): REQUEST_HANDLER(pW3Context, pApplication)
{
}

IN_PROCESS_HANDLER::~IN_PROCESS_HANDLER()
{
    //todo
}

__override
REQUEST_NOTIFICATION_STATUS
IN_PROCESS_HANDLER::OnExecuteRequestHandler()
{
    //todo
    return RQ_NOTIFICATION_CONTINUE;
}

__override
REQUEST_NOTIFICATION_STATUS
IN_PROCESS_HANDLER::OnAsyncCompletion(
    DWORD           cbCompletion,
    HRESULT         hrCompletionStatus
)
{
    //todo
    return RQ_NOTIFICATION_CONTINUE;
}