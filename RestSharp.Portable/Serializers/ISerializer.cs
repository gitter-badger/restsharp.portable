﻿using System.Net.Http.Headers;

namespace RestSharp.Portable.Serializers
{
    /// <summary>
    /// Serializer for a content type
    /// </summary>
    public interface ISerializer
    {
        /// <summary>
        /// Gets or sets the content type produced by the serializer
        /// </summary>
        MediaTypeHeaderValue ContentType { get; set; }

        /// <summary>
        /// Serialize the object into a byte array
        /// </summary>
        /// <param name="obj">Object to serialize</param>
        /// <returns>Byte array to send in the request body</returns>
        byte[] Serialize(object obj);
    }
}
