using System;
using System.Collections;
using System.Collections.Generic;
using Microsoft.AspNetCore.Http.Features;

namespace WebApplication26
{
    public class IISHttpContext : IFeatureCollection
    {
        private readonly IntPtr _pHttpContext;
        private readonly FeatureCollection _features = new FeatureCollection();

        public IISHttpContext(IntPtr pHttpContext)
        {
            _pHttpContext = pHttpContext;

            var request = new HttpRequestFeature();
            request.Body = new IISHttpRequestBody(pHttpContext);
            Set<IHttpRequestFeature>(request);
            var response = new HttpResponseFeature();
            response.Body = new IISHttpResponseBody(pHttpContext);
            Set<IHttpResponseFeature>(response);
        }

        public object this[Type key] { get => _features[key]; set => _features[key] = value; }

        public bool IsReadOnly => _features.IsReadOnly;

        public int Revision => _features.Revision;

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

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}
