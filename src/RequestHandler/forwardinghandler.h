#pragma once

class FORWARDING_HANDLER : public REQUEST_HANDLER
{
public:
    FORWARDING_HANDLER(
        _In_ IHttpContext *pW3Context,
        _In_ APPLICATION  *pApplication);

    ~FORWARDING_HANDLER();

    __override
    REQUEST_NOTIFICATION_STATUS
    OnExecuteRequestHandler();

    __override
    REQUEST_NOTIFICATION_STATUS
    OnAsyncCompletion(
        DWORD       cbCompletion,
        HRESULT     hrCompletionStatus
    );
};