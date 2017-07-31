using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace SampleServer
{
    public unsafe partial class RequestHeaders : IHeaderDictionary
    {
        private IDictionary<string, StringValues> _extra;
        private readonly HttpApi.HTTP_REQUEST_V2* _pHttpRequest;

        internal RequestHeaders(HttpApi.HTTP_REQUEST_V2* pHttpRequest)
        {
            _pHttpRequest = pHttpRequest;
        }

        private IDictionary<string, StringValues> Extra
        {
            get
            {
                if (_extra == null)
                {
                    var newDict = new Dictionary<string, StringValues>(StringComparer.OrdinalIgnoreCase);
                    GetUnknownHeaders(newDict);
                    Interlocked.CompareExchange(ref _extra, newDict, null);
                }
                return _extra;
            }
        }

        StringValues IDictionary<string, StringValues>.this[string key]
        {
            get
            {
                if (PropertiesTryGetValue(key, out var value))
                {
                    return value;
                }

                if (Extra.TryGetValue(key, out value))
                {
                    return value;
                }

                return StringValues.Empty;
            }
            set
            {
                if (!PropertiesTrySetValue(key, value))
                {
                    Extra[key] = value;
                }
            }
        }

        StringValues IHeaderDictionary.this[string key]
        {
            get
            {
                if (PropertiesTryGetValue(key, out var value))
                {
                    return value;
                }

                if (Extra.TryGetValue(key, out value))
                {
                    return value;
                }
                return StringValues.Empty;
            }
            set
            {
                if (!PropertiesTrySetValue(key, value))
                {
                    Extra[key] = value;
                }
            }
        }

        private string GetKnownHeader(HttpSysRequestHeader header)
        {
            int headerIndex = (int)header;
            string value = null;

            unsafe
            {
                HttpApi.HTTP_KNOWN_HEADER* pKnownHeader = (&_pHttpRequest->Request.Headers.KnownHeaders) + headerIndex;
                // For known headers, when header value is empty, RawValueLength will be 0 and
                // pRawValue will point to empty string ("\0")
                if (pKnownHeader->pRawValue != null)
                {
                    value = Encoding.ASCII.GetString(pKnownHeader->pRawValue, pKnownHeader->RawValueLength);
                }
                return value;
            }
        }

        private void GetUnknownHeaders(IDictionary<string, StringValues> unknownHeaders)
        {
            var requestHeaders = _pHttpRequest->Request.Headers;
            if (requestHeaders.UnknownHeaderCount != 0)
            {
                unsafe
                {
                    var pUnknownHeader = requestHeaders.pUnknownHeaders;
                    for (int index = 0; index < requestHeaders.UnknownHeaderCount; index++)
                    {
                        // For unknown headers, when header value is empty, RawValueLength will be 0 and
                        // pRawValue will be null.
                        if (pUnknownHeader->pName != null && pUnknownHeader->NameLength > 0)
                        {
                            // REVIEW: UTF8 or ASCII?
                            var headerName = Encoding.ASCII.GetString(pUnknownHeader->pName, pUnknownHeader->NameLength);
                            string headerValue;
                            if (pUnknownHeader->pRawValue != null && pUnknownHeader->RawValueLength > 0)
                            {
                                headerValue = Encoding.ASCII.GetString(pUnknownHeader->pRawValue, pUnknownHeader->RawValueLength);
                            }
                            else
                            {
                                headerValue = string.Empty;
                            }
                            // Note that Http.Sys currently collapses all headers of the same name to a single coma separated string,
                            // so we can just call Set.
                            unknownHeaders[headerName] = headerValue;
                        }
                        pUnknownHeader++;
                    }
                }
            }
        }

        void IDictionary<string, StringValues>.Add(string key, StringValues value)
        {
            if (!PropertiesTrySetValue(key, value))
            {
                Extra.Add(key, value);
            }
        }

        public bool ContainsKey(string key)
        {
            return PropertiesContainsKey(key) || Extra.ContainsKey(key);
        }

        public ICollection<string> Keys
        {
            get { return PropertiesKeys().Concat(Extra.Keys).ToArray(); }
        }

        ICollection<StringValues> IDictionary<string, StringValues>.Values
        {
            get { return PropertiesValues().Concat(Extra.Values).ToArray(); }
        }

        public int Count
        {
            get { return PropertiesKeys().Count() + Extra.Count; }
        }

        public bool Remove(string key)
        {
            // Although this is a mutating operation, Extra is used instead of StrongExtra,
            // because if a real dictionary has not been allocated the default behavior of the
            // nil dictionary is perfectly fine.
            return PropertiesTryRemove(key) || Extra.Remove(key);
        }

        public bool TryGetValue(string key, out StringValues value)
        {
            return PropertiesTryGetValue(key, out value) || Extra.TryGetValue(key, out value);
        }

        void ICollection<KeyValuePair<string, StringValues>>.Add(KeyValuePair<string, StringValues> item)
        {
            ((IDictionary<string, object>)this).Add(item.Key, item.Value);
        }

        void ICollection<KeyValuePair<string, StringValues>>.Clear()
        {
            foreach (var key in PropertiesKeys())
            {
                PropertiesTryRemove(key);
            }
            Extra.Clear();
        }

        bool ICollection<KeyValuePair<string, StringValues>>.Contains(KeyValuePair<string, StringValues> item)
        {
            object value;
            return ((IDictionary<string, object>)this).TryGetValue(item.Key, out value) && Object.Equals(value, item.Value);
        }

        void ICollection<KeyValuePair<string, StringValues>>.CopyTo(KeyValuePair<string, StringValues>[] array, int arrayIndex)
        {
            PropertiesEnumerable().Concat(Extra).ToArray().CopyTo(array, arrayIndex);
        }

        bool ICollection<KeyValuePair<string, StringValues>>.IsReadOnly
        {
            get { return false; }
        }

        // TODO: Set and get known header directly
        long? IHeaderDictionary.ContentLength
        {
            get
            {
                return StringValues.IsNullOrEmpty(ContentLength) ? (long?)null : long.Parse(ContentLength);
            }
            set
            {
                ContentLength = value.ToString();
            }
        }

        bool ICollection<KeyValuePair<string, StringValues>>.Remove(KeyValuePair<string, StringValues> item)
        {
            return ((IDictionary<string, StringValues>)this).Contains(item) &&
                ((IDictionary<string, StringValues>)this).Remove(item.Key);
        }

        IEnumerator<KeyValuePair<string, StringValues>> IEnumerable<KeyValuePair<string, StringValues>>.GetEnumerator()
        {
            return PropertiesEnumerable().Concat(Extra).GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return ((IDictionary<string, StringValues>)this).GetEnumerator();
        }
    }
}
