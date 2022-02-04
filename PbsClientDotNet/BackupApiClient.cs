using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Http2;
using Http2.Hpack;
using PbsClientDotNet.Model;
using PbsClientDotNet.Internal;

namespace PbsClientDotNet
{
    public class BackupApiClient
    {
        Dictionary<int, IndexWriterInformation> _fixedIndexInformation = new Dictionary<int, IndexWriterInformation>();
        Dictionary<int, IndexWriterInformation> _dynamicIndexInformation = new Dictionary<int, IndexWriterInformation>();

        Connection _connection;
        BackupIndex _index;
        IStream _http2Stream;
        public string Fingerprint { get; set; }
        internal LoginData LoginData { get; set; }

        HttpClient _httpClient;

        public bool RemoteCertificateValidationCallback(object sender, X509Certificate? certificate, X509Chain? chain, SslPolicyErrors sslPolicyErrors) => ServerCertificateCustomValidationCallback(null, new X509Certificate2(certificate), chain, sslPolicyErrors);
        private Func<HttpRequestMessage, X509Certificate2, X509Chain, SslPolicyErrors, bool> ServerCertificateCustomValidationCallback { get; set; }

        public BackupApiClient(string url, string fingerprint)
        {
            Fingerprint = fingerprint;

            ServerCertificateCustomValidationCallback = (o, cert, chain, errors) =>
            {
                var data = SHA256.HashData(cert.GetRawCertData());
                string hexString = BitConverter.ToString(data).Replace("-", ":").ToLower();
                return hexString == Fingerprint;
            };

            _httpClient = new HttpClient(new HttpClientHandler()
            {
                ServerCertificateCustomValidationCallback = ServerCertificateCustomValidationCallback
            })
            {
                BaseAddress = new Uri(url),
            };
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "proxmox-backup-client/1.0");
        }


        #region login
        public async Task<ApiResponse<LoginResponse>> LoginAsync(string username, string password, string realm = null, string otp = null)
        {
            Dictionary<string, string> data = new Dictionary<string, string>()
            {
                { "username" , username },
                { "password" , password },
            };
            if (realm != null)
                data.Add("realm", realm);
            if (otp != null)
                data.Add("otp", otp);

            HttpResponseMessage response = await _httpClient.PostAsync($"/api2/json/access/ticket", new FormUrlEncodedContent(data));
            var payload = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                HttpApiResponse<LoginData>? apiResponse = JsonConvert.DeserializeObject<HttpApiResponse<LoginData>>(payload);

                if (apiResponse != null)
                    LoginData = apiResponse.Data;

                //Add authentication headers to HttpClient
                _httpClient.DefaultRequestHeaders.Add("CSRFPreventionToken", LoginData.CSRFPreventionToken);
                _httpClient.DefaultRequestHeaders.Add("Cookie", "PBSAuthCookie=" + LoginData.Ticket);

                return new ApiResponse<LoginResponse>((int)response.StatusCode, null, new LoginResponse(true, null, LoginData.NeedTFA == 1), response.IsSuccessStatusCode);
            }
            else
            {
                return new ApiResponse<LoginResponse>((int)response.StatusCode, response.ReasonPhrase + " " + payload, new LoginResponse(false, response.ReasonPhrase, false), response.IsSuccessStatusCode);
            }
        }

        public async Task<ApiResponse<bool>> ProvideTFAAsync(string code)
        {
            var data = new Dictionary<string, string>()
            {
                { "response" , code },
            };

            HttpResponseMessage response = await _httpClient.PostAsync($"/api2/json/access/tfa", new FormUrlEncodedContent(data)).TimeoutAfter(TimeSpan.FromSeconds(2));
            var payload = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                HttpApiResponse<TfaData> apiResponse = JsonConvert.DeserializeObject<HttpApiResponse<TfaData>>(payload);
                LoginData.Ticket = apiResponse.Data.Ticket;

                _httpClient.DefaultRequestHeaders.Remove("Cookie");
                _httpClient.DefaultRequestHeaders.Add("Cookie", "PVEAuthCookie=" + LoginData.Ticket);

                return new ApiResponse<bool>((int)response.StatusCode, null, true, response.IsSuccessStatusCode);
            }
            else
            {
                return new ApiResponse<bool>((int)response.StatusCode, response.ReasonPhrase + " " + payload, false, response.IsSuccessStatusCode);
            }
        }

        #endregion

        #region Upgrade
        private async Task<Connection> CreateUpgradeConnection(string id, string type, string store, DateTime date)
        {
            int time = (int)date.Subtract(DateTime.UnixEpoch).TotalSeconds;
            var uri = new Uri(_httpClient.BaseAddress, $"/api2/json/backup?backup-id={id}&backup-type={type}&store={store}&backup-time={time}");
            // HTTP/2 settings
            var config =
                new ConnectionConfigurationBuilder(false)
                .UseSettings(Settings.Default)
                .UseHuffmanStrategy(HuffmanStrategy.IfSmaller)
                .Build();

            // Prepare connection upgrade
            var upgrade =
                new ClientUpgradeRequestBuilder()
                .SetHttp2Settings(config.Settings)
                .Build();

            // Create a TCP connection
            var tcpClient = new TcpClient();
            await tcpClient.ConnectAsync(uri.Host, uri.Port);
            tcpClient.Client.NoDelay = true;

            // Create HTTP/2 stream abstraction on top of the socket
            SslStream stream = new SslStream(tcpClient.GetStream(), true, RemoteCertificateValidationCallback);
            await stream.AuthenticateAsClientAsync(uri.Host);

            var wrappedStreams = stream.CreateStreams();
            var upgradeReadStream = new UpgradeReadStream(wrappedStreams.ReadableStream);

            var needExplicitStreamClose = true;
            try
            {
                // Send a HTTP/1.1 upgrade request with the necessary fields
                var upgradeHeader =
                    $"GET {uri.PathAndQuery} HTTP/1.1\r\n" +
                    $"Host: {uri.Host}\r\n" +
                    "Connection: Upgrade, HTTP2-Settings\r\n" +
                    "HTTP2-Settings: " + upgrade.Base64EncodedSettings + "\r\n" +
                    "Upgrade: proxmox-backup-protocol-v1\r\n";

                foreach (var header in _httpClient.DefaultRequestHeaders)
                    upgradeHeader += $"{header.Key}: {String.Join(',', header.Value)}\r\n";

                upgradeHeader += "\r\n";

                var encodedHeader = Encoding.ASCII.GetBytes(upgradeHeader);
                await wrappedStreams.WriteableStream.WriteAsync(
                    new ArraySegment<byte>(encodedHeader));

                // Wait for the upgrade response
                await upgradeReadStream.WaitForHttpHeader();
                var headerBytes = upgradeReadStream.HeaderBytes;

                // Try to parse the upgrade response as HTTP/1 status and check whether
                // the upgrade was successful.
                var response = Http1Response.ParseFrom(
                    Encoding.ASCII.GetString(
                        headerBytes.Array, headerBytes.Offset, headerBytes.Count - 4));
                // Mark the HTTP/1.1 bytes as read
                upgradeReadStream.ConsumeHttpHeader();

                if (response.StatusCode != "101")
                    throw new Exception("Upgrade failed");
                if (!response.Headers.Any(hf => hf.Key == "upgrade" && hf.Value == "proxmox-backup-protocol-v1"))
                    throw new Exception("Upgrade failed");


                // If we get here then the connection will be reponsible for closing
                // the stream
                needExplicitStreamClose = false;

                // Build a HTTP connection on top of the stream abstraction
                var conn = new Connection(
                    config, upgradeReadStream, wrappedStreams.WriteableStream,
                    options: new Connection.Options
                    {
                        ClientUpgradeRequest = upgrade,
                    });

                // Retrieve the response stream for the connection upgrade.
                _http2Stream = await upgrade.UpgradeRequestStream;
                // As we made the upgrade via a dummy OPTIONS request we are not
                // really interested in the result of the upgrade request

                return conn;
            }
            finally
            {
                if (needExplicitStreamClose)
                {
                    await wrappedStreams.WriteableStream.CloseAsync();
                }
            }
        }
        public async Task<bool> StartBackupProtocol(string id, string type, string store, DateTime date)
        {
            int time = (int)date.Subtract(DateTime.UnixEpoch).TotalSeconds;
            _connection = await CreateUpgradeConnection(id, type, store, date);

            _index = new BackupIndex()
            {
                BackupId = id,
                BackupTime = time,
                BackupType = type,
                Files = new List<BackupFile>(),
            };

            return true;
        }
        #endregion

        #region Blob
        public async Task<bool> UploadBlobFile(string name, IDataBlob blob)
        {
            byte[] content = blob.RawByteFormat();

            if (name.EndsWith(".blob"))
                name.Substring(0, name.Length - 5);

            var response = await MakeHttp2RequestAsync("POST", $"/blob?file-name={name}.blob&encoded-size={content.Length}", content);

            if (!response.Success)
                throw new HttpRequestException("Could not create index! Response: \r\n" + response.ToString());

            BackupFile file = new BackupFile()
            {
                Filename = name + ".blob",
                CryptMode = blob.GetCryptMode(),
                Size = (UInt64)blob.GetDataLength(),
                Csum = blob.GetCsum(),
            };

            _index.Files.Add(file);

            return response.Success;
        }
        #endregion

        #region FixedIndex
        public async Task<int> CreateFixedIndex(string name, UInt64 size, bool reusecsum = false)
        {
            if (name.EndsWith(".fidx"))
                name.Substring(0, name.Length - 5);

            var response = await MakeHttp2RequestAsync("POST", $"/fixed_index?archive-name={name}.fidx&size={size}");

            if (!response.Success)
                throw new HttpRequestException("Could not create index! Response: \r\n" + response.ToString());

            var data = JsonConvert.DeserializeObject<WriterResponse>(response.GetContentAsString());

            IndexWriterInformation info = new IndexWriterInformation()
            {
                FileInfo = new BackupFile()
                {
                    Filename = name + ".fidx",
                    CryptMode = "none",
                    Size = size,
                    Csum = null,
                },
            };

            _fixedIndexInformation[data.Data] = info;

            return data.Data;
        }


        public async Task<string> UploadFixedChunk(int writer_id, byte[] data, bool lastChunk = false)
        {
            UnencryptedDataBlob blob = new UnencryptedDataBlob(data);

            var _blobData = blob.RawByteFormat();
            var digest = BitConverter.ToString(SHA256.HashData(data)).Replace("-", "").ToLower();

            var response = await MakeHttp2RequestAsync("POST", $"/fixed_chunk?digest={digest}&encoded-size={_blobData.Length}&size={data.Length}&wid={writer_id}", _blobData);

            if (!response.Success)
                throw new HttpRequestException("Could not upload chunk! Response: \r\n" + response.ToString());

            var resp = JsonConvert.DeserializeObject<ChunkResponse>(response.GetContentAsString());

            var info = _fixedIndexInformation[writer_id];
            info.AppendChunkToHash(blob);

            return resp.Data;
        }

        public async Task<bool> CloseFixedIndex(int writer_id)
        {
            var info = _fixedIndexInformation[writer_id];

            byte[] digests = info.Digests.SelectMany(d => StringToByteArray(d)).ToArray();
            var digest = BitConverter.ToString(SHA256.HashData(digests)).Replace("-", "").ToLower();

            var response = await MakeHttp2RequestAsync("POST", $"/fixed_close?chunk-count={info.Chunks}&csum={digest}&size={info.Size}&wid={writer_id}");

            if (!response.Success)
                throw new HttpRequestException("Could not close index! Response: \r\n" + response.ToString());

            info.FileInfo.Csum = digest;
            info.FileInfo.Size = info.Size;
            _index.Files.Add(info.FileInfo);

            _fixedIndexInformation.Remove(writer_id);

            return response.Success;
        }
        public async Task<bool> AppendChunksToFixedIndex(int writer_id)
        {
            var info = _fixedIndexInformation[writer_id];

            HeaderField[] headers = new HeaderField[]
            {
                new HeaderField { Name = "content-type", Value = "application/json" },
            };

            var request = new AppendRequest()
            {
                DigestList = info.UnappendedDigests,
                OffsetList = info.UnappendedOffsets,
                Wid = writer_id,
            };

            if (request.DigestList.Count == 0 && request.OffsetList.Count == 0)
                return true;

            string json = JsonConvert.SerializeObject(request);

            // Wait for response headers
            var response = await MakeHttp2RequestAsync("PUT", "/fixed_index", headers, Encoding.UTF8.GetBytes(json));

            if (!response.Success)
                throw new HttpRequestException("Could not append chunks to index! Response: \r\n" + response.ToString());

            foreach (var digest in request.DigestList)
                info.UnappendedDigests.Remove(digest);

            foreach (var offset in request.OffsetList)
                info.UnappendedOffsets.Remove(offset);

            return response.Success;
        }
        #endregion

        #region DynamicIndex
        public async Task<int> CreateDynamicIndex(string name)
        {
            if (name.EndsWith(".didx"))
                name.Substring(0, name.Length - 5);

            var response = await MakeHttp2RequestAsync("POST", $"/dynamic_index?archive-name={name}.didx");

            if (!response.Success)
                throw new HttpRequestException("Could not create index! Response: \r\n" + response.ToString());

            var data = JsonConvert.DeserializeObject<WriterResponse>(response.GetContentAsString());

            IndexWriterInformation info = new IndexWriterInformation()
            {
                FileInfo = new BackupFile()
                {
                    Filename = name + ".didx",
                    CryptMode = "none",
                    Size = 0,
                    Csum = null,
                },
            };

            _dynamicIndexInformation[data.Data] = info;

            return data.Data;
        }
        public async Task<string> UploadDynamicChunk(int writer_id, byte[] data, bool lastChunk = false)
        {
            UnencryptedDataBlob blob = new UnencryptedDataBlob(data);

            var _blobData = blob.RawByteFormat();
            var digest = BitConverter.ToString(SHA256.HashData(data)).Replace("-", "").ToLower();

            var response = await MakeHttp2RequestAsync("POST", $"/dynamic_chunk?digest={digest}&encoded-size={_blobData.Length}&size={data.Length}&wid={writer_id}", _blobData);

            if (!response.Success)
                throw new HttpRequestException("Could not upload chunk! Response: \r\n" + response.ToString());

            var resp = JsonConvert.DeserializeObject<ChunkResponse>(response.GetContentAsString());

            var info = _dynamicIndexInformation[writer_id];
            info.AppendChunkToHash(blob);

            return resp.Data;
        }
        public async Task<bool> CloseDynamicIndex(int writer_id)
        {
            var info = _dynamicIndexInformation[writer_id];
            List<byte> digestData = new List<byte>();

            for (int i = 0; i < info.Digests.Count; i++)
            {
                var digest = StringToByteArray(info.Digests[i]);
                var offset = BitConverter.GetBytes(info.EndOffsets[i]);
                digestData.AddRange(offset);
                digestData.AddRange(digest);
            }

            string csum = BitConverter.ToString(SHA256.HashData(digestData.ToArray())).Replace("-", "").ToLower();

            var response = await MakeHttp2RequestAsync("POST", $"/dynamic_close?chunk-count={info.Chunks}&csum={csum}&size={info.Size}&wid={writer_id}");

            if (!response.Success)
                throw new HttpRequestException("Could not close index! Response: \r\n" + response.ToString());

            info.FileInfo.Csum = csum;
            info.FileInfo.Size = info.Size;
            _index.Files.Add(info.FileInfo);

            _dynamicIndexInformation.Remove(writer_id);

            return response.Success;
        }

        public async Task<bool> AppendChunksToDynamicIndex(int writer_id)
        {
            HeaderField[] headers = new HeaderField[]
            {
                new HeaderField { Name = "content-type", Value = "application/json" },
            };

            var info = _dynamicIndexInformation[writer_id];
            var request = new AppendRequest()
            {
                DigestList = info.UnappendedDigests,
                OffsetList = info.UnappendedOffsets,
                Wid = writer_id,
            };

            if (request.DigestList.Count == 0 && request.OffsetList.Count == 0)
                return true;

            string json = JsonConvert.SerializeObject(request);

            // Wait for response headers
            var response = await MakeHttp2RequestAsync("PUT", "/dynamic_index", headers, Encoding.UTF8.GetBytes(json));

            if (!response.Success)
                throw new HttpRequestException("Could not append chunks to index! Response: \r\n" + response.ToString());

            info.UnappendedDigests.Clear();
            info.UnappendedOffsets.Clear();

            return response.Success;
        }
        #endregion

        #region Finish
        public async Task<bool> FinishBackupProtocol()
        {
            string indexJson = JsonConvert.SerializeObject(_index);
            await UploadBlobFile("index.json", new UnencryptedDataBlob(Encoding.UTF8.GetBytes(indexJson)));

            var response = await MakeHttp2RequestAsync("POST", "/finish");

            if (!response.Success)
                throw new HttpRequestException("Could not finish backup! Response: \r\n" + response.ToString());

            return response.Success;
        }
        #endregion

        #region Streaming
        private const int MaxChunkSize = 4 * 1024 * 1024;
        public async Task<bool> UploadFixedIndexStream(string name, Stream stream, bool reusecsum = false)
        {
            UInt64 size = (UInt64)stream.Length;
            var wid = await CreateFixedIndex(name, size, reusecsum);

            while ((UInt64)stream.Position < (UInt64)stream.Length)
            {
                byte[] buffer = await ReadBytesFromStream(stream, Math.Min(MaxChunkSize, (int)(stream.Length - stream.Position)));
                await UploadFixedChunk(wid, buffer);
            }
            await AppendChunksToFixedIndex(wid);
            await CloseFixedIndex(wid);

            return true;
        }


        public async Task<bool> UploadDynamicIndexStream(string name, Stream stream, bool reusecsum = false)
        {
            var wid = await CreateDynamicIndex(name);

            while (!IsEndOfStream(stream))
            {
                byte[] buffer = await ReadBytesFromStream(stream, MaxChunkSize, false);
                await UploadDynamicChunk(wid, buffer);
            }
            await AppendChunksToDynamicIndex(wid);
            await CloseDynamicIndex(wid);

            return true;
        }

        #endregion

        #region ApiInternals
        private async Task<byte[]> ReadBytesFromStream(Stream stream, int count, bool exact = true)
        {
            byte[] data = new byte[count];
            int offset = 0;
            byte[] buffer = new byte[count];
            while (offset < count && (stream.CanRead || (!stream.CanRead && exact)) && !IsEndOfStream(stream))
            {
                if (!stream.CanRead && exact)
                    throw new IOException($"Cannot read from closed stream. I was able to read {offset} bytes total but you asked for {count}");

                int len = await stream.ReadAsync(buffer, 0, Math.Min(buffer.Length, count - offset));
                Array.Copy(buffer, 0, data, offset, len);
                offset += len;
            }
            return data.Take(offset).ToArray();
        }

        private bool IsEndOfStream(Stream stream)
        {
            try
            {
                return stream.Length == stream.Position;
            }
            catch { }
            return !stream.CanRead;
        }

        private byte[] StringToByteArray(string hex)
        {
            return Enumerable.Range(0, hex.Length)
                             .Where(x => x % 2 == 0)
                             .Select(x => Convert.ToByte(hex.Substring(x, 2), 16))
                             .ToArray();
        }
        private Task<Http2Response> MakeHttp2RequestAsync(string method, string pathAndQuery) => MakeHttp2RequestAsync(method, pathAndQuery, null, null);
        private Task<Http2Response> MakeHttp2RequestAsync(string method, string pathAndQuery, byte[] content) => MakeHttp2RequestAsync(method, pathAndQuery, null, content);
        private Task<Http2Response> MakeHttp2RequestAsync(string method, string pathAndQuery, IEnumerable<HeaderField> headerFields) => MakeHttp2RequestAsync(method, pathAndQuery, headerFields, null);
        private async Task<Http2Response> MakeHttp2RequestAsync(string method, string pathAndQuery, IEnumerable<HeaderField> headerFields, byte[] content)
        {
            HeaderField[] headers = new HeaderField[]
            {
                new HeaderField { Name = ":method", Value = method },
                new HeaderField { Name = ":scheme", Value = "http" },
                new HeaderField { Name = ":path", Value = pathAndQuery },
            };

            if (headerFields != null)
                headers = headers.Concat(headerFields).ToArray();

            var stream = await _connection.CreateStreamAsync(
                headers, endOfStream: content == null);

            if (content != null)
                await stream.WriteAsync(content, true);

            // Wait for response headers
            var reponseHeaders = await stream.ReadHeadersAsync();

            // Read response data
            List<byte> responseContent = new List<byte>();
            byte[] buf = new byte[8192];

            while (true)
            {
                var res = await stream.ReadAsync(new ArraySegment<byte>(buf));
                if (res.EndOfStream) break;
                responseContent.AddRange(buf.Take(res.BytesRead));
            }

            return new Http2Response()
            {
                Content = responseContent.ToArray(),
                Success = reponseHeaders.First(s => s.Name == ":status").Value == "200",
                Headers = reponseHeaders,
            };
        }
        #endregion
    }
}