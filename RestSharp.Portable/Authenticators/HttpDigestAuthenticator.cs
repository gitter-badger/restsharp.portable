﻿using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace RestSharp.Portable.Authenticators
{
    /// <summary>
    /// HTTP Digest authenticator
    /// </summary>
    /// <remarks>
    /// Code was taken from http://www.ifjeffcandoit.com/2013/05/16/digest-authentication-with-restsharp/
    /// </remarks>
    [SuppressMessage("StyleCop.CSharp.DocumentationRules", "SA1650:ElementDocumentationMustBeSpelledCorrectly", Justification = "Misspelled text is an URL.")]
    public class HttpDigestAuthenticator : IRoundTripAuthenticator
    {
        private static readonly IEnumerable<HttpStatusCode> _statusCodes = new List<HttpStatusCode>
        {
            HttpStatusCode.Unauthorized,
        };

        private readonly ICredentials _credentials;

        private string _realm;

        private string _nonce;

        private QualityOfProtection _qop = QualityOfProtection.Undefined;

        private string _cnonce;

        private string _opaque;

        private Algorithm _algorithm = Algorithm.Undefined;

        private DateTime _cnonceDate;

        private int _nc;

        /// <summary>
        /// Initializes a new instance of the <see cref="HttpDigestAuthenticator" /> class.
        /// </summary>
        /// <param name="credentials">The authentication credentials</param>
        public HttpDigestAuthenticator(ICredentials credentials)
        {
            _credentials = credentials;
        }

        [Flags]
        private enum QualityOfProtection
        {
            Undefined = 0,
            Auth = 1,
            AuthInt = 2,
        }

        private enum Algorithm
        {
            Undefined = 0,
            MD5,
            // ReSharper disable once InconsistentNaming
            MD5sess,
        }

        /// <summary>
        /// Gets all the status codes where a round trip is allowed
        /// </summary>
        public IEnumerable<HttpStatusCode> StatusCodes
        {
            get { return _statusCodes; }
        }

        private bool HasDigestHeader
        {
            get
            {
                return !string.IsNullOrEmpty(_cnonce) && DateTime.Now.Subtract(_cnonceDate).TotalHours < 1.0;
            }
        }

        /// <summary>
        /// Modifies the request to ensure that the authentication requirements are met.
        /// </summary>
        /// <param name="client">Client executing this request</param>
        /// <param name="request">Request to authenticate</param>
        public void Authenticate(IRestClient client, IRestRequest request)
        {
            if (HasDigestHeader)
            {
                var digestHeader = GetDigestHeader(client, request);
                request.AddParameter("Authorization", digestHeader, ParameterType.HttpHeader);
            }
        }

        /// <summary>
        /// Will be called when the authentication failed
        /// </summary>
        /// <param name="client">Client executing this request</param>
        /// <param name="request">Request to authenticate</param>
        /// <param name="response">Response of the failed request</param>
        public void AuthenticationFailed(IRestClient client, IRestRequest request, IRestResponse response)
        {
            ParseResponseHeader(response);
        }

        private static string CalculateMd5Hash(string input)
        {
            var inputBytes = Encoding.UTF8.GetBytes(input);
            return CalculateMd5Hash(inputBytes);
        }

        private static string CalculateMd5Hash(byte[] inputBytes)
        {
            byte[] hash;
            using (var digest = MD5.Create())
                hash = digest.ComputeHash(inputBytes);

            var sb = new StringBuilder();
            foreach (var b in hash)
                sb.Append(b.ToString("x2"));
            return sb.ToString();
        }

        private static string GrabHeaderVar(string varName, string header, string defaultValue = null)
        {
            var regHeader = new Regex(string.Format(@"{0}\s*=\s*((""(?<qval>[^""]*)"")|(?<val>[^,]+))", varName));
            var matchHeader = regHeader.Match(header);
            if (matchHeader.Success)
            {
                var qval = matchHeader.Groups["qval"];
                if (qval.Success)
                    return qval.Value;
                var val = matchHeader.Groups["val"];
                return val.Value.Trim();
            }

            if (defaultValue == null)
                throw new WebException(string.Format("Header {0} not found", varName), WebExceptionStatus.UnknownError);
            return defaultValue;
        }

        private string GetDigestHeader(IRestClient client, IRestRequest restRequest)
        {
            _nc = _nc + 1;

            var uri = client.BuildUri(restRequest);
            var credential = _credentials.GetCredential(uri, "Digest");

            var pathAndQuery = uri.GetComponents(UriComponents.PathAndQuery, UriFormat.SafeUnescaped);

            if (_algorithm == Algorithm.Undefined)
                throw new InvalidOperationException("Algorithm not set");

            string ha2, digestResponse;

            var ha1 = CalculateMd5Hash(string.Format("{0}:{1}:{2}", credential.UserName, _realm, credential.Password));

            string algorithm;
            switch (_algorithm)
            {
                case Algorithm.MD5sess:
                    ha1 = CalculateMd5Hash(string.Format("{0}:{1}:{2}", ha1, _nonce, _cnonce));
                    algorithm = "MD5-sess";
                    break;
                default:
                    algorithm = "MD5";
                    break;
            }

            string qop;
            switch (_qop)
            {
                case QualityOfProtection.Auth:
                    qop = "auth";
                    break;
                case QualityOfProtection.AuthInt:
                    qop = "auth-int";
                    break;
                default:
                    qop = null;
                    break;
            }

            switch (_qop)
            {
                case QualityOfProtection.AuthInt:
                    {
                        var content = client.GetContent(restRequest);
                        byte[] entityBody;
                        if (content == null)
                        {
                            entityBody = new byte[0];
                        }
                        else
                        {
                            var readTask = content.ReadAsByteArrayAsync();
                            readTask.Wait();
                            entityBody = readTask.Result;
                        }

                        ha2 = CalculateMd5Hash(entityBody);
                    }

                    ha2 = CalculateMd5Hash(string.Format("{0}:{1}:{2}", client.GetEffectiveHttpMethod(restRequest).Method, pathAndQuery, ha2));
                    break;
                default:
                    ha2 = CalculateMd5Hash(string.Format("{0}:{1}", client.GetEffectiveHttpMethod(restRequest).Method, pathAndQuery));
                    break;
            }

            switch (_qop)
            {
                case QualityOfProtection.AuthInt:
                case QualityOfProtection.Auth:
                    digestResponse = CalculateMd5Hash(string.Format("{0}:{1}:{2:D8}:{3}:{4}:{5}", ha1, _nonce, _nc, _cnonce, qop, ha2));
                    break;
                default:
                    digestResponse = CalculateMd5Hash(string.Format("{0}:{1}:{2}", ha1, _nonce, ha2));
                    break;
            }

            var result = new StringBuilder();
            result
                .AppendFormat("Digest username=\"{0}\"", credential.UserName)
                .AppendFormat(", realm=\"{0}\"", _realm)
                .AppendFormat(", nonce=\"{0}\"", _nonce)
                .AppendFormat(", uri=\"{0}\"", pathAndQuery)
                .AppendFormat(", nc={0:D08}", _nc);
            if (algorithm != "MD5")
                result.AppendFormat(", algorithm=\"{0}\"", algorithm);
            if (!string.IsNullOrEmpty(qop))
            {
                result
                    .AppendFormat(", cnonce=\"{0}\"", _cnonce)
                    .AppendFormat(", qop={0}", qop);
            }

            if (!string.IsNullOrEmpty(_opaque))
                result
                    .AppendFormat(", opaque=\"{0}\"", _opaque);
            result
                .AppendFormat(", response=\"{0}\"", digestResponse);
            return result.ToString();
        }

        private void ParseResponseHeader(IRestResponse response)
        {
            var wwwAuthenticateHeader = response.Headers.GetValues("WWW-Authenticate").First();
            _realm = GrabHeaderVar("realm", wwwAuthenticateHeader);
            _nonce = GrabHeaderVar("nonce", wwwAuthenticateHeader);

            var algorithm = GrabHeaderVar("algorithm", wwwAuthenticateHeader, "MD5");
            switch (algorithm.ToLower())
            {
                case "md5":
                    _algorithm = Algorithm.MD5;
                    break;
                case "md5-sess":
                    _algorithm = Algorithm.MD5sess;
                    break;
                default:
                    throw new NotSupportedException(string.Format("Unsupported algorithm {0}", algorithm));
            }

            var qopParts = GrabHeaderVar("qop", wwwAuthenticateHeader, string.Empty)
                .Split(',');
            _qop = QualityOfProtection.Undefined;
            foreach (var qopPart in qopParts.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim().ToLower()))
            {
                switch (qopPart)
                {
                    case "auth":
                        _qop |= QualityOfProtection.Auth;
                        break;
                    case "auth-int":
                        _qop |= QualityOfProtection.AuthInt;
                        break;
                    default:
                        throw new NotSupportedException(string.Format("Unsupported QOP {0}", qopPart));
                }
            }

            _nc = 0;
            _opaque = GrabHeaderVar("opaque", wwwAuthenticateHeader, string.Empty);
            _cnonce = new Random().Next(123400, 9999999).ToString(CultureInfo.InvariantCulture);
            _cnonceDate = DateTime.Now;
        }
    }
}
