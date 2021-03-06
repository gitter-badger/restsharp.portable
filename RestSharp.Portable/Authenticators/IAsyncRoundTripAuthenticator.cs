﻿using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;

namespace RestSharp.Portable.Authenticators
{
    /// <summary>
    /// This authenticator can handle 401 responses and modify the Authentication behavior/result.
    /// </summary>
    public interface IAsyncRoundTripAuthenticator : IAsyncAuthenticator
    {
        /// <summary>
        /// Gets all the status codes where a round trip is allowed
        /// </summary>
        IEnumerable<HttpStatusCode> StatusCodes { get; }

        /// <summary>
        /// Will be called when the authentication failed
        /// </summary>
        /// <param name="client">Client executing this request</param>
        /// <param name="request">Request to authenticate</param>
        /// <param name="response">Response of the failed request</param>
        /// <returns>Task where the handler for a failed authentication gets executed</returns>
        Task AuthenticationFailed(IRestClient client, IRestRequest request, IRestResponse response);
    }
}
