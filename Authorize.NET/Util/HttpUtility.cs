namespace AuthorizeNet.Util
{
    using System;
    using System.IO;
    using System.Net;
    using System.Net.Http;
    using System.Net.Http.Headers;
    using System.Text;
    using System.Threading;
    using System.Xml;
    using System.Xml.Serialization;
    using AuthorizeNet.Api.Contracts.V1;
    using AuthorizeNet.Api.Controllers.Bases;

#pragma warning disable 1591
    public static class HttpUtility {

        //Max response size allowed: 64 MB
        private const int MaxResponseLength = 67108864;
	    private static readonly Log Logger = LogFactory.getLog(typeof(HttpUtility));

        static readonly bool UseProxy = AuthorizeNet.Environment.getBooleanProperty(Constants.HttpsUseProxy);
        static readonly String ProxyHost = AuthorizeNet.Environment.GetProperty(Constants.HttpsProxyHost);
        static readonly int ProxyPort = AuthorizeNet.Environment.getIntProperty(Constants.HttpsProxyPort);

        private static readonly HttpClient SharedClient = CreateSharedHttpClient();

        private static HttpClient CreateSharedHttpClient()
        {
            var handler = new HttpClientHandler();
            if (UseProxy)
            {
                var proxyUri = new Uri(string.Format("{0}://{1}:{2}", Constants.ProxyProtocol, ProxyHost, ProxyPort));
                Logger.info(string.Format("Setting up proxy to URL: '{0}'", proxyUri));
                handler.Proxy = new WebProxy(proxyUri)
                {
                    UseDefaultCredentials = true,
                    BypassProxyOnLocal = true,
                };
                handler.UseProxy = true;
            }

            // Per-request timeouts are enforced via CancellationToken; HttpClient.Timeout is the overall fallback.
            return new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        }

        private static Uri GetPostUrl(AuthorizeNet.Environment env)
	    {
		    var postUrl = new Uri(env.getXmlBaseUrl() + "/xml/v1/request.api");
            Logger.debug(string.Format("Creating PostRequest Url: '{0}'", postUrl));

		    return postUrl;
	    }

        public static ANetApiResponse PostData<TQ, TS>(AuthorizeNet.Environment env, TQ request)
            where TQ : ANetApiRequest
            where TS : ANetApiResponse
        {
            ANetApiResponse response = null;
            if (null == request)
            {
                throw new ArgumentNullException("request");
            }

            var postUrl = GetPostUrl(env);

            // The original implementation set Timeout (connect+overall) and ReadWriteTimeout (per-stream-op)
            // separately on HttpWebRequest. HttpClient has only an overall request timeout, so honor the
            // larger of the two configured values to avoid prematurely cancelling slow responses.
            var httpConnectionTimeout = AuthorizeNet.Environment.getIntProperty(Constants.HttpConnectionTimeout);
            var connectionTimeoutMs = httpConnectionTimeout != 0 ? httpConnectionTimeout : Constants.HttpConnectionDefaultTimeout;
            var httpReadWriteTimeout = AuthorizeNet.Environment.getIntProperty(Constants.HttpReadWriteTimeout);
            var readWriteTimeoutMs = httpReadWriteTimeout != 0 ? httpReadWriteTimeout : Constants.HttpReadWriteDefaultTimeout;
            var overallTimeoutMs = Math.Max(connectionTimeoutMs, readWriteTimeoutMs);

            var requestType = typeof(TQ);
            var serializer = new XmlSerializer(requestType);
            byte[] requestBytes;
            using (var memoryStream = new MemoryStream())
            {
                using (var writer = new XmlTextWriter(memoryStream, Encoding.UTF8))
                {
                    serializer.Serialize(writer, request);
                    writer.Flush();
                }
                requestBytes = memoryStream.ToArray();
            }

            String responseAsString = null;
            Logger.debug(string.Format("Retreiving Response from Url: '{0}'", postUrl));

            using (var cts = new CancellationTokenSource(overallTimeoutMs))
            using (var httpRequest = new HttpRequestMessage(HttpMethod.Post, postUrl))
            using (var content = new ByteArrayContent(requestBytes))
            {
                content.Headers.ContentType = new MediaTypeHeaderValue("text/xml");
                httpRequest.Content = content;

                using (var httpResponse = SharedClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, cts.Token).GetAwaiter().GetResult())
                {
                    Logger.debug(string.Format("Received Response: '{0}'", httpResponse));

                    using (var responseStream = httpResponse.Content.ReadAsStreamAsync(cts.Token).GetAwaiter().GetResult())
                    {
                        if (null != responseStream)
                        {
                            var result = new StringBuilder();

                            using (var reader = new StreamReader(responseStream))
                            {
                                while (!reader.EndOfStream)
                                {
                                    try
                                    {
                                        result.Append((char)reader.Read());
                                    }
                                    catch (Exception)
                                    {
                                        throw new Exception("Cannot read response.");
                                    }

                                    if (result.Length >= MaxResponseLength)
                                    {
                                        throw new Exception("response is too long.");
                                    }
                                }

                                responseAsString = result.Length > 0 ? result.ToString() : null;
                            }
                            Logger.debug(string.Format("Response from Stream: '{0}'", responseAsString));
                        }
                    }
                }
            }

            if (null != responseAsString)
            {
                using (var memoryStreamForResponseAsString = new MemoryStream(Encoding.UTF8.GetBytes(responseAsString)))
                {
                    var responseType = typeof (TS);
                    var deSerializer = new XmlSerializer(responseType);

                    Object deSerializedObject;
                    try
                    {
                        // try deserializing to the expected response type
                        deSerializedObject = deSerializer.Deserialize(memoryStreamForResponseAsString);
                    }
                    catch (Exception)
                    {
                        // probably a bad response, try if this is an error response
                        memoryStreamForResponseAsString.Seek(0, SeekOrigin.Begin); //start from beginning of stream
                        var genericDeserializer = new XmlSerializer(typeof (ANetApiResponse));
                        deSerializedObject = genericDeserializer.Deserialize(memoryStreamForResponseAsString);
                    }

                    //if error response
                    if (deSerializedObject is ErrorResponse)
                    {
                        response = deSerializedObject as ErrorResponse;
                    }
                    else
                    {
                        //actual response of type expected
                        if (deSerializedObject is TS)
                        {
                            response = deSerializedObject as TS;
                        }
                        else if (deSerializedObject is ANetApiResponse) //generic response
                        {
                            response = deSerializedObject as ANetApiResponse;
                        }
                    }
                }
            }

            return response;
	    }
    }


#pragma warning restore 1591
}
