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
    // First get the in process Application
    HRESULT hr;
    hr = ((IN_PROCESS_APPLICATION*)m_pApplication)->LoadManagedApplication();
    if (FAILED(hr))
    {
        /*_com_error err(hr);
        if (ANCMEvents::ANCM_START_APPLICATION_FAIL::IsEnabled(m_pW3Context->GetTraceContext()))
        {
            ANCMEvents::ANCM_START_APPLICATION_FAIL::RaiseEvent(
                m_pW3Context->GetTraceContext(),
                NULL,
                err.ErrorMessage());
        }

        fInternalError = TRUE;
        goto Failure;*/
        return REQUEST_NOTIFICATION_STATUS::RQ_NOTIFICATION_FINISH_REQUEST;
    }

    // FREB log
   /* if (ANCMEvents::ANCM_START_APPLICATION_SUCCESS::IsEnabled(m_pW3Context->GetTraceContext()))
    {
        ANCMEvents::ANCM_START_APPLICATION_SUCCESS::RaiseEvent(
            m_pW3Context->GetTraceContext(),
            NULL,
            L"InProcess Application");
    }*/
    //SetHttpSysDisconnectCallback();
    return  ((IN_PROCESS_APPLICATION*)m_pApplication)->OnExecuteRequest(m_pW3Context);
}

__override
REQUEST_NOTIFICATION_STATUS
IN_PROCESS_HANDLER::OnAsyncCompletion(
    DWORD       cbCompletion,
    HRESULT     hrCompletionStatus
)
{
    HRESULT hr;
    if (FAILED(hrCompletionStatus))
    {
        return RQ_NOTIFICATION_FINISH_REQUEST;
    }
    else
    {
        // For now we are assuming we are in our own self contained box. 
        // TODO refactor Finished and Failure sections to handle in process and out of process failure.
        // TODO verify that websocket's OnAsyncCompletion is not calling this.
        IN_PROCESS_APPLICATION* application = (IN_PROCESS_APPLICATION*)m_pApplication;
        if (application == NULL)
        {
            hr = E_FAIL;
            return RQ_NOTIFICATION_FINISH_REQUEST;
        }

        return application->OnAsyncCompletion(m_pW3Context, cbCompletion, hrCompletionStatus);
    }
}

