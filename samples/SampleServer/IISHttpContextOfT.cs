using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting.Server;

namespace SampleServer
{
    public class IISHttpContextOfT<T> : IISHttpContext
    {
        private readonly IHttpApplication<T> _application;
        private readonly NativeMethods.request_handler_cb _pfnCompletionCallback;
        private readonly IntPtr _pvCompletionContext;

        public IISHttpContextOfT(IHttpApplication<T> application, IntPtr pHttpContext, NativeMethods.request_handler_cb pfnCompletionCallback, IntPtr pvCompletionContext)
            : base(pHttpContext)
        {
            _application = application;
            _pfnCompletionCallback = pfnCompletionCallback;
            _pvCompletionContext = pvCompletionContext;
        }

        public override async Task ProcessRequestAsync()
        {
            var context = _application.CreateContext(this);

            try
            {
                await _application.ProcessRequestAsync(context);
                _application.DisposeContext(context, exception: null);
                _pfnCompletionCallback(0, _pHttpContext, _pvCompletionContext);
            }
            catch (Exception ex)
            {
                _application.DisposeContext(context, ex);
                _pfnCompletionCallback(ex.HResult, _pHttpContext, _pvCompletionContext);
            }
        }
    }
}
