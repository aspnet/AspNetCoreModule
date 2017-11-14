#include "precomp.hxx"

OUT_OF_PROCESS_APPLICATION::OUT_OF_PROCESS_APPLICATION(
    IHttpServer*        pHttpServer,
    ASPNETCORE_CONFIG*  pConfig) :
    APPLICATION(pHttpServer, pConfig)
{
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