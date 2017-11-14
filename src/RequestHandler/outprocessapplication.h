#pragma once

class OUT_OF_PROCESS_APPLICATION : public APPLICATION
{
public:
    OUT_OF_PROCESS_APPLICATION(IHttpServer* pHttpServer, ASPNETCORE_CONFIG  *pConfig);

    ~OUT_OF_PROCESS_APPLICATION();

    __override
    VOID
    ShutDown();
};
