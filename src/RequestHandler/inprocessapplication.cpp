#include "precomp.hxx"

IN_PROCESS_APPLICATION::IN_PROCESS_APPLICATION(
    IHttpServer*        pHttpServer, 
    ASPNETCORE_CONFIG*  pConfig) :
    APPLICATION(pHttpServer, pConfig)
{
    //todo
}

IN_PROCESS_APPLICATION::~IN_PROCESS_APPLICATION()
{
    // todo
}

__override
VOID
IN_PROCESS_APPLICATION::ShutDown()
{
    //todo
}