#include "precomp.hxx"

FORWARDING_HANDLER::FORWARDING_HANDLER(
    _In_ IHttpContext *pW3Context,
    _In_ APPLICATION  *pApplication
) : REQUEST_HANDLER(pW3Context, pApplication)
{
    //todo
}

FORWARDING_HANDLER::~FORWARDING_HANDLER(
)
{
    //todo
}

__override
REQUEST_NOTIFICATION_STATUS
FORWARDING_HANDLER::OnExecuteRequestHandler()
{
    //todo
    return RQ_NOTIFICATION_CONTINUE;
}

__override
REQUEST_NOTIFICATION_STATUS
FORWARDING_HANDLER::OnAsyncCompletion(
    DWORD           cbCompletion,
    HRESULT         hrCompletionStatus
)
{
    //todo
    return RQ_NOTIFICATION_CONTINUE;
}