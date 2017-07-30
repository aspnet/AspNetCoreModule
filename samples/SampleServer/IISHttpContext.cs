using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http.Features;

namespace SampleServer
{
    public class IISHttpContext : IFeatureCollection, IHttpUpgradeFeature
    {
        private readonly IntPtr _pHttpContext;
        private readonly FeatureCollection _features = new FeatureCollection();

        public IISHttpContext(IntPtr pHttpContext)
        {
            _pHttpContext = pHttpContext;

            unsafe
            {
                var pHttpRequest = NativeMethods.http_get_raw_request(pHttpContext);
                var pHttpResponse = NativeMethods.http_get_raw_request(pHttpContext);
            }

            var request = new HttpRequestFeature();
            request.Body = new IISHttpRequestBody(pHttpContext);
            Set<IHttpRequestFeature>(request);
            Set<IHttpUpgradeFeature>(this);
            var response = new HttpResponseFeature();
            response.Body = new IISHttpResponseBody(pHttpContext);
            Set<IHttpResponseFeature>(response);
        }

        public object this[Type key] { get => _features[key]; set => _features[key] = value; }

        public bool IsReadOnly => _features.IsReadOnly;

        public int Revision => _features.Revision;

        public bool IsUpgradableRequest => throw new NotImplementedException();

        public TFeature Get<TFeature>()
        {
            return _features.Get<TFeature>();
        }

        public IEnumerator<KeyValuePair<Type, object>> GetEnumerator()
        {
            return _features.GetEnumerator();
        }

        public void Set<TFeature>(TFeature instance)
        {
            _features.Set(instance);
        }

        public async Task<Stream> UpgradeAsync()
        {
            var response = Get<IHttpResponseFeature>();
            response.StatusCode = 101;

            await response.Body.FlushAsync();

            // Send 101
            return new DuplexStream(Get<IHttpRequestFeature>().Body, response.Body);
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}
