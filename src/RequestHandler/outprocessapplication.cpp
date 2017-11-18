#include "precomp.hxx"

OUT_OF_PROCESS_APPLICATION::OUT_OF_PROCESS_APPLICATION(
    IHttpServer*        pHttpServer,
    ASPNETCORE_CONFIG*  pConfig) :
    APPLICATION(pHttpServer, pConfig)
{
    m_status = APPLICATION_STATUS::RUNNING;
    //todo
}

OUT_OF_PROCESS_APPLICATION::~OUT_OF_PROCESS_APPLICATION()
{
    // todo
}

__override
VOID
OUT_OF_PROCESS_APPLICATION::ShutDown()
{
    //todo
}