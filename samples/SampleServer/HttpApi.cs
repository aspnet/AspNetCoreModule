using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace SampleServer
{
    public unsafe class HttpApi
    {
        public enum HTTP_API_VERSION
        {
            Invalid,
            Version10,
            Version20,
        }

        // see http.w for definitions
        public enum HTTP_SERVER_PROPERTY
        {
            HttpServerAuthenticationProperty,
            HttpServerLoggingProperty,
            HttpServerQosProperty,
            HttpServerTimeoutsProperty,
            HttpServerQueueLengthProperty,
            HttpServerStateProperty,
            HttpServer503VerbosityProperty,
            HttpServerBindingProperty,
            HttpServerExtendedAuthenticationProperty,
            HttpServerListenEndpointProperty,
            HttpServerChannelBindProperty,
            HttpServerProtectionLevelProperty,
        }

        // Currently only one request info type is supported but the enum is for future extensibility.

        public enum HTTP_REQUEST_INFO_TYPE
        {
            HttpRequestInfoTypeAuth,
            HttpRequestInfoTypeChannelBind,
            HttpRequestInfoTypeSslProtocol,
            HttpRequestInfoTypeSslTokenBinding
        }

        public enum HTTP_RESPONSE_INFO_TYPE
        {
            HttpResponseInfoTypeMultipleKnownHeaders,
            HttpResponseInfoTypeAuthenticationProperty,
            HttpResponseInfoTypeQosProperty,
        }

        public enum HTTP_TIMEOUT_TYPE
        {
            EntityBody,
            DrainEntityBody,
            RequestQueue,
            IdleConnection,
            HeaderWait,
            MinSendRate,
        }

        public const int MaxTimeout = 6;

        [StructLayout(LayoutKind.Sequential)]
        public struct HTTP_VERSION
        {
            public ushort MajorVersion;
            public ushort MinorVersion;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct HTTP_KNOWN_HEADER
        {
            public ushort RawValueLength;
            public sbyte* pRawValue;
        }

        [StructLayout(LayoutKind.Explicit)]
        public struct HTTP_DATA_CHUNK
        {
            [FieldOffset(0)]
            public HTTP_DATA_CHUNK_TYPE DataChunkType;

            [FieldOffset(8)]
            public FromMemory fromMemory;

            [FieldOffset(8)]
            public FromFileHandle fromFile;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct FromMemory
        {
            // 4 bytes for 32bit, 8 bytes for 64bit
            public IntPtr pBuffer;
            public uint BufferLength;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct FromFileHandle
        {
            public ulong offset;
            public ulong count;
            public IntPtr fileHandle;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct HTTPAPI_VERSION
        {
            public ushort HttpApiMajorVersion;
            public ushort HttpApiMinorVersion;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct HTTP_COOKED_URL
        {
            public ushort FullUrlLength;
            public ushort HostLength;
            public ushort AbsPathLength;
            public ushort QueryStringLength;
            public ushort* pFullUrl;
            public ushort* pHost;
            public ushort* pAbsPath;
            public ushort* pQueryString;
        }

        // Only cache unauthorized GETs + HEADs.
        [StructLayout(LayoutKind.Sequential)]
        public struct HTTP_CACHE_POLICY
        {
            public HTTP_CACHE_POLICY_TYPE Policy;
            public uint SecondsToLive;
        }

        public enum HTTP_CACHE_POLICY_TYPE : int
        {
            HttpCachePolicyNocache = 0,
            HttpCachePolicyUserInvalidates = 1,
            HttpCachePolicyTimeToLive = 2,
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct SOCKADDR
        {
            public ushort sa_family;
            public byte sa_data;
            public byte sa_data_02;
            public byte sa_data_03;
            public byte sa_data_04;
            public byte sa_data_05;
            public byte sa_data_06;
            public byte sa_data_07;
            public byte sa_data_08;
            public byte sa_data_09;
            public byte sa_data_10;
            public byte sa_data_11;
            public byte sa_data_12;
            public byte sa_data_13;
            public byte sa_data_14;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct HTTP_TRANSPORT_ADDRESS
        {
            public SOCKADDR* pRemoteAddress;
            public SOCKADDR* pLocalAddress;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct HTTP_SSL_CLIENT_CERT_INFO
        {
            public uint CertFlags;
            public uint CertEncodedSize;
            public byte* pCertEncoded;
            public void* Token;
            public byte CertDeniedByMapper;
        }

        public enum HTTP_SERVICE_BINDING_TYPE : uint
        {
            HttpServiceBindingTypeNone = 0,
            HttpServiceBindingTypeW,
            HttpServiceBindingTypeA
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct HTTP_SERVICE_BINDING_BASE
        {
            public HTTP_SERVICE_BINDING_TYPE Type;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct HTTP_REQUEST_CHANNEL_BIND_STATUS
        {
            public IntPtr ServiceName;
            public IntPtr ChannelToken;
            public uint ChannelTokenSize;
            public uint Flags;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct HTTP_UNKNOWN_HEADER
        {
            public ushort NameLength;
            public ushort RawValueLength;
            public sbyte* pName;
            public sbyte* pRawValue;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct HTTP_SSL_INFO
        {
            public ushort ServerCertKeySize;
            public ushort ConnectionKeySize;
            public uint ServerCertIssuerSize;
            public uint ServerCertSubjectSize;
            public sbyte* pServerCertIssuer;
            public sbyte* pServerCertSubject;
            public HTTP_SSL_CLIENT_CERT_INFO* pClientCertInfo;
            public uint SslClientCertNegotiated;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct HTTP_RESPONSE_HEADERS
        {
            public ushort UnknownHeaderCount;
            public HTTP_UNKNOWN_HEADER* pUnknownHeaders;
            public ushort TrailerCount;
            public HTTP_UNKNOWN_HEADER* pTrailers;
            public HTTP_KNOWN_HEADER KnownHeaders;
            public HTTP_KNOWN_HEADER KnownHeaders_02;
            public HTTP_KNOWN_HEADER KnownHeaders_03;
            public HTTP_KNOWN_HEADER KnownHeaders_04;
            public HTTP_KNOWN_HEADER KnownHeaders_05;
            public HTTP_KNOWN_HEADER KnownHeaders_06;
            public HTTP_KNOWN_HEADER KnownHeaders_07;
            public HTTP_KNOWN_HEADER KnownHeaders_08;
            public HTTP_KNOWN_HEADER KnownHeaders_09;
            public HTTP_KNOWN_HEADER KnownHeaders_10;
            public HTTP_KNOWN_HEADER KnownHeaders_11;
            public HTTP_KNOWN_HEADER KnownHeaders_12;
            public HTTP_KNOWN_HEADER KnownHeaders_13;
            public HTTP_KNOWN_HEADER KnownHeaders_14;
            public HTTP_KNOWN_HEADER KnownHeaders_15;
            public HTTP_KNOWN_HEADER KnownHeaders_16;
            public HTTP_KNOWN_HEADER KnownHeaders_17;
            public HTTP_KNOWN_HEADER KnownHeaders_18;
            public HTTP_KNOWN_HEADER KnownHeaders_19;
            public HTTP_KNOWN_HEADER KnownHeaders_20;
            public HTTP_KNOWN_HEADER KnownHeaders_21;
            public HTTP_KNOWN_HEADER KnownHeaders_22;
            public HTTP_KNOWN_HEADER KnownHeaders_23;
            public HTTP_KNOWN_HEADER KnownHeaders_24;
            public HTTP_KNOWN_HEADER KnownHeaders_25;
            public HTTP_KNOWN_HEADER KnownHeaders_26;
            public HTTP_KNOWN_HEADER KnownHeaders_27;
            public HTTP_KNOWN_HEADER KnownHeaders_28;
            public HTTP_KNOWN_HEADER KnownHeaders_29;
            public HTTP_KNOWN_HEADER KnownHeaders_30;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct HTTP_REQUEST_HEADERS
        {
            public ushort UnknownHeaderCount;
            public HTTP_UNKNOWN_HEADER* pUnknownHeaders;
            public ushort TrailerCount;
            public HTTP_UNKNOWN_HEADER* pTrailers;
            public HTTP_KNOWN_HEADER KnownHeaders;
            public HTTP_KNOWN_HEADER KnownHeaders_02;
            public HTTP_KNOWN_HEADER KnownHeaders_03;
            public HTTP_KNOWN_HEADER KnownHeaders_04;
            public HTTP_KNOWN_HEADER KnownHeaders_05;
            public HTTP_KNOWN_HEADER KnownHeaders_06;
            public HTTP_KNOWN_HEADER KnownHeaders_07;
            public HTTP_KNOWN_HEADER KnownHeaders_08;
            public HTTP_KNOWN_HEADER KnownHeaders_09;
            public HTTP_KNOWN_HEADER KnownHeaders_10;
            public HTTP_KNOWN_HEADER KnownHeaders_11;
            public HTTP_KNOWN_HEADER KnownHeaders_12;
            public HTTP_KNOWN_HEADER KnownHeaders_13;
            public HTTP_KNOWN_HEADER KnownHeaders_14;
            public HTTP_KNOWN_HEADER KnownHeaders_15;
            public HTTP_KNOWN_HEADER KnownHeaders_16;
            public HTTP_KNOWN_HEADER KnownHeaders_17;
            public HTTP_KNOWN_HEADER KnownHeaders_18;
            public HTTP_KNOWN_HEADER KnownHeaders_19;
            public HTTP_KNOWN_HEADER KnownHeaders_20;
            public HTTP_KNOWN_HEADER KnownHeaders_21;
            public HTTP_KNOWN_HEADER KnownHeaders_22;
            public HTTP_KNOWN_HEADER KnownHeaders_23;
            public HTTP_KNOWN_HEADER KnownHeaders_24;
            public HTTP_KNOWN_HEADER KnownHeaders_25;
            public HTTP_KNOWN_HEADER KnownHeaders_26;
            public HTTP_KNOWN_HEADER KnownHeaders_27;
            public HTTP_KNOWN_HEADER KnownHeaders_28;
            public HTTP_KNOWN_HEADER KnownHeaders_29;
            public HTTP_KNOWN_HEADER KnownHeaders_30;
            public HTTP_KNOWN_HEADER KnownHeaders_31;
            public HTTP_KNOWN_HEADER KnownHeaders_32;
            public HTTP_KNOWN_HEADER KnownHeaders_33;
            public HTTP_KNOWN_HEADER KnownHeaders_34;
            public HTTP_KNOWN_HEADER KnownHeaders_35;
            public HTTP_KNOWN_HEADER KnownHeaders_36;
            public HTTP_KNOWN_HEADER KnownHeaders_37;
            public HTTP_KNOWN_HEADER KnownHeaders_38;
            public HTTP_KNOWN_HEADER KnownHeaders_39;
            public HTTP_KNOWN_HEADER KnownHeaders_40;
            public HTTP_KNOWN_HEADER KnownHeaders_41;
        }

        public enum HTTP_VERB : int
        {
            HttpVerbUnparsed = 0,
            HttpVerbUnknown = 1,
            HttpVerbInvalid = 2,
            HttpVerbOPTIONS = 3,
            HttpVerbGET = 4,
            HttpVerbHEAD = 5,
            HttpVerbPOST = 6,
            HttpVerbPUT = 7,
            HttpVerbDELETE = 8,
            HttpVerbTRACE = 9,
            HttpVerbCONNECT = 10,
            HttpVerbTRACK = 11,
            HttpVerbMOVE = 12,
            HttpVerbCOPY = 13,
            HttpVerbPROPFIND = 14,
            HttpVerbPROPPATCH = 15,
            HttpVerbMKCOL = 16,
            HttpVerbLOCK = 17,
            HttpVerbUNLOCK = 18,
            HttpVerbSEARCH = 19,
            HttpVerbMaximum = 20,
        }

        public static readonly string[] HttpVerbs = new string[]
        {
                null,
                "Unknown",
                "Invalid",
                "OPTIONS",
                "GET",
                "HEAD",
                "POST",
                "PUT",
                "DELETE",
                "TRACE",
                "CONNECT",
                "TRACK",
                "MOVE",
                "COPY",
                "PROPFIND",
                "PROPPATCH",
                "MKCOL",
                "LOCK",
                "UNLOCK",
                "SEARCH",
        };

        public enum HTTP_DATA_CHUNK_TYPE : int
        {
            HttpDataChunkFromMemory = 0,
            HttpDataChunkFromFileHandle = 1,
            HttpDataChunkFromFragmentCache = 2,
            HttpDataChunkMaximum = 3,
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct HTTP_RESPONSE_INFO
        {
            public HTTP_RESPONSE_INFO_TYPE Type;
            public uint Length;
            public HTTP_MULTIPLE_KNOWN_HEADERS* pInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct HTTP_RESPONSE
        {
            public uint Flags;
            public HTTP_VERSION Version;
            public ushort StatusCode;
            public ushort ReasonLength;
            public sbyte* pReason;
            public HTTP_RESPONSE_HEADERS Headers;
            public ushort EntityChunkCount;
            public HTTP_DATA_CHUNK* pEntityChunks;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct HTTP_RESPONSE_V2
        {
            public HTTP_RESPONSE Response_V1;
            public ushort ResponseInfoCount;
            public HTTP_RESPONSE_INFO* pResponseInfo;
        }

        public enum HTTP_RESPONSE_INFO_FLAGS : uint
        {
            None = 0,
            PreserveOrder = 1,
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct HTTP_MULTIPLE_KNOWN_HEADERS
        {
            public HTTP_RESPONSE_HEADER_ID.Enum HeaderId;
            public HTTP_RESPONSE_INFO_FLAGS Flags;
            public ushort KnownHeaderCount;
            public HTTP_KNOWN_HEADER* KnownHeaders;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct HTTP_REQUEST_AUTH_INFO
        {
            public HTTP_AUTH_STATUS AuthStatus;
            public uint SecStatus;
            public uint Flags;
            public HTTP_REQUEST_AUTH_TYPE AuthType;
            public IntPtr AccessToken;
            public uint ContextAttributes;
            public uint PackedContextLength;
            public uint PackedContextType;
            public IntPtr PackedContext;
            public uint MutualAuthDataLength;
            public char* pMutualAuthData;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct HTTP_REQUEST_INFO
        {
            public HTTP_REQUEST_INFO_TYPE InfoType;
            public uint InfoLength;
            public HTTP_REQUEST_AUTH_INFO* pInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct HTTP_REQUEST
        {
            public uint Flags;
            public ulong ConnectionId;
            public ulong RequestId;
            public ulong UrlContext;
            public HTTP_VERSION Version;
            public HTTP_VERB Verb;
            public ushort UnknownVerbLength;
            public ushort RawUrlLength;
            public sbyte* pUnknownVerb;
            public sbyte* pRawUrl;
            public HTTP_COOKED_URL CookedUrl;
            public HTTP_TRANSPORT_ADDRESS Address;
            public HTTP_REQUEST_HEADERS Headers;
            public ulong BytesReceived;
            public ushort EntityChunkCount;
            public HTTP_DATA_CHUNK* pEntityChunks;
            public ulong RawConnectionId;
            public HTTP_SSL_INFO* pSslInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct HTTP_REQUEST_V2
        {
            public HTTP_REQUEST Request;
            public ushort RequestInfoCount;
            public HTTP_REQUEST_INFO* pRequestInfo;
        }

        public enum HTTP_AUTH_STATUS
        {
            HttpAuthStatusSuccess,
            HttpAuthStatusNotAuthenticated,
            HttpAuthStatusFailure,
        }

        public enum HTTP_REQUEST_AUTH_TYPE
        {
            HttpRequestAuthTypeNone = 0,
            HttpRequestAuthTypeBasic,
            HttpRequestAuthTypeDigest,
            HttpRequestAuthTypeNTLM,
            HttpRequestAuthTypeNegotiate,
            HttpRequestAuthTypeKerberos
        }

        public enum HTTP_QOS_SETTING_TYPE
        {
            HttpQosSettingTypeBandwidth,
            HttpQosSettingTypeConnectionLimit,
            HttpQosSettingTypeFlowRate
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct HTTP_SERVER_AUTHENTICATION_INFO
        {
            public HTTP_FLAGS Flags;
            public HTTP_AUTH_TYPES AuthSchemes;
            public bool ReceiveMutualAuth;
            public bool ReceiveContextHandle;
            public bool DisableNTLMCredentialCaching;
            public ulong ExFlags;
            HTTP_SERVER_AUTHENTICATION_DIGEST_PARAMS DigestParams;
            HTTP_SERVER_AUTHENTICATION_BASIC_PARAMS BasicParams;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct HTTP_SERVER_AUTHENTICATION_DIGEST_PARAMS
        {
            public ushort DomainNameLength;
            public char* DomainName;
            public ushort RealmLength;
            public char* Realm;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct HTTP_SERVER_AUTHENTICATION_BASIC_PARAMS
        {
            ushort RealmLength;
            char* Realm;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct HTTP_REQUEST_TOKEN_BINDING_INFO
        {
            public byte* TokenBinding;
            public uint TokenBindingSize;

            public byte* TlsUnique;
            public uint TlsUniqueSize;

            public char* KeyType;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct HTTP_TIMEOUT_LIMIT_INFO
        {
            public HTTP_FLAGS Flags;
            public ushort EntityBody;
            public ushort DrainEntityBody;
            public ushort RequestQueue;
            public ushort IdleConnection;
            public ushort HeaderWait;
            public uint MinSendRate;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct HTTP_BINDING_INFO
        {
            public HTTP_FLAGS Flags;
            public IntPtr RequestQueueHandle;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct HTTP_CONNECTION_LIMIT_INFO
        {
            public HTTP_FLAGS Flags;
            public uint MaxConnections;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct HTTP_QOS_SETTING_INFO
        {
            public HTTP_QOS_SETTING_TYPE QosType;
            public IntPtr QosSetting;
        }

        // see http.w for definitions
        [Flags]
        public enum HTTP_FLAGS : uint
        {
            NONE = 0x00000000,
            HTTP_RECEIVE_REQUEST_FLAG_COPY_BODY = 0x00000001,
            HTTP_RECEIVE_SECURE_CHANNEL_TOKEN = 0x00000001,
            HTTP_SEND_RESPONSE_FLAG_DISCONNECT = 0x00000001,
            HTTP_SEND_RESPONSE_FLAG_MORE_DATA = 0x00000002,
            HTTP_SEND_RESPONSE_FLAG_BUFFER_DATA = 0x00000004,
            HTTP_SEND_RESPONSE_FLAG_RAW_HEADER = 0x00000004,
            HTTP_SEND_REQUEST_FLAG_MORE_DATA = 0x00000001,
            HTTP_PROPERTY_FLAG_PRESENT = 0x00000001,
            HTTP_INITIALIZE_SERVER = 0x00000001,
            HTTP_INITIALIZE_CBT = 0x00000004,
            HTTP_SEND_RESPONSE_FLAG_OPAQUE = 0x00000040,
        }

        [Flags]
        public enum HTTP_AUTH_TYPES : uint
        {
            NONE = 0x00000000,
            HTTP_AUTH_ENABLE_BASIC = 0x00000001,
            HTTP_AUTH_ENABLE_DIGEST = 0x00000002,
            HTTP_AUTH_ENABLE_NTLM = 0x00000004,
            HTTP_AUTH_ENABLE_NEGOTIATE = 0x00000008,
            HTTP_AUTH_ENABLE_KERBEROS = 0x00000010,
        }

        public static class HTTP_RESPONSE_HEADER_ID
        {
            private static string[] _strings =
            {
                    "Cache-Control",
                    "Connection",
                    "Date",
                    "Keep-Alive",
                    "Pragma",
                    "Trailer",
                    "Transfer-Encoding",
                    "Upgrade",
                    "Via",
                    "Warning",

                    "Allow",
                    "Content-Length",
                    "Content-Type",
                    "Content-Encoding",
                    "Content-Language",
                    "Content-Location",
                    "Content-MD5",
                    "Content-Range",
                    "Expires",
                    "Last-Modified",

                    "Accept-Ranges",
                    "Age",
                    "ETag",
                    "Location",
                    "Proxy-Authenticate",
                    "Retry-After",
                    "Server",
                    "Set-Cookie",
                    "Vary",
                    "WWW-Authenticate",
                };

            private static Dictionary<string, int> _lookupTable = CreateLookupTable();

            private static Dictionary<string, int> CreateLookupTable()
            {
                Dictionary<string, int> lookupTable = new Dictionary<string, int>((int)Enum.HttpHeaderResponseMaximum, StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < (int)Enum.HttpHeaderResponseMaximum; i++)
                {
                    lookupTable.Add(_strings[i], i);
                }
                return lookupTable;
            }

            public static int IndexOfKnownHeader(string HeaderName)
            {
                int index;
                return _lookupTable.TryGetValue(HeaderName, out index) ? index : -1;
            }

            public enum Enum
            {
                HttpHeaderCacheControl = 0,    // general-header [section 4.5]
                HttpHeaderConnection = 1,    // general-header [section 4.5]
                HttpHeaderDate = 2,    // general-header [section 4.5]
                HttpHeaderKeepAlive = 3,    // general-header [not in rfc]
                HttpHeaderPragma = 4,    // general-header [section 4.5]
                HttpHeaderTrailer = 5,    // general-header [section 4.5]
                HttpHeaderTransferEncoding = 6,    // general-header [section 4.5]
                HttpHeaderUpgrade = 7,    // general-header [section 4.5]
                HttpHeaderVia = 8,    // general-header [section 4.5]
                HttpHeaderWarning = 9,    // general-header [section 4.5]

                HttpHeaderAllow = 10,   // entity-header  [section 7.1]
                HttpHeaderContentLength = 11,   // entity-header  [section 7.1]
                HttpHeaderContentType = 12,   // entity-header  [section 7.1]
                HttpHeaderContentEncoding = 13,   // entity-header  [section 7.1]
                HttpHeaderContentLanguage = 14,   // entity-header  [section 7.1]
                HttpHeaderContentLocation = 15,   // entity-header  [section 7.1]
                HttpHeaderContentMd5 = 16,   // entity-header  [section 7.1]
                HttpHeaderContentRange = 17,   // entity-header  [section 7.1]
                HttpHeaderExpires = 18,   // entity-header  [section 7.1]
                HttpHeaderLastModified = 19,   // entity-header  [section 7.1]

                // Response Headers

                HttpHeaderAcceptRanges = 20,   // response-header [section 6.2]
                HttpHeaderAge = 21,   // response-header [section 6.2]
                HttpHeaderEtag = 22,   // response-header [section 6.2]
                HttpHeaderLocation = 23,   // response-header [section 6.2]
                HttpHeaderProxyAuthenticate = 24,   // response-header [section 6.2]
                HttpHeaderRetryAfter = 25,   // response-header [section 6.2]
                HttpHeaderServer = 26,   // response-header [section 6.2]
                HttpHeaderSetCookie = 27,   // response-header [not in rfc]
                HttpHeaderVary = 28,   // response-header [section 6.2]
                HttpHeaderWwwAuthenticate = 29,   // response-header [section 6.2]

                HttpHeaderResponseMaximum = 30,

                HttpHeaderMaximum = 41
            }
        }
    }
}
