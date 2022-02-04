﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PbsClientDotNet.Internal
{
    internal static class HttpRequestExtensions
    {
        public static async Task<string> ToRawString(this HttpRequestMessage request)
        {
            var sb = new StringBuilder();

            var line1 = $"{request.Method} {request.RequestUri} HTTP/{request.Version}";
            sb.AppendLine(line1);

            foreach (var (key, value) in request.Headers)
                foreach (var val in value)
                {
                    var header = $"{key}: {val}";
                    sb.AppendLine(header);
                }

            if (request.Content?.Headers != null)
            {
                foreach (var (key, value) in request.Content.Headers)
                    foreach (var val in value)
                    {
                        var header = $"{key}: {val}";
                        sb.AppendLine(header);
                    }
            }
            sb.AppendLine();

            var body = await (request.Content?.ReadAsStringAsync() ?? Task.FromResult<string>(null));
            if (body != null)
                sb.AppendLine(body);

            return sb.ToString();
        }

        public static async Task<string> ToRawString(this HttpResponseMessage response)
        {
            var sb = new StringBuilder();

            var statusCode = (int)response.StatusCode;
            var line1 = $"HTTP/{response.Version} {statusCode} {response.ReasonPhrase}";
            sb.AppendLine(line1);

            foreach (var (key, value) in response.Headers)
                foreach (var val in value)
                {
                    var header = $"{key}: {val}";
                    sb.AppendLine(header);
                }

            foreach (var (key, value) in response.Content.Headers)
                foreach (var val in value)
                {
                    var header = $"{key}: {val}";
                    sb.AppendLine(header);
                }
            sb.AppendLine();

            var body = await response.Content.ReadAsStringAsync();
            if (!string.IsNullOrWhiteSpace(body))
                sb.AppendLine(body);

            return sb.ToString();
        }
    }
}
