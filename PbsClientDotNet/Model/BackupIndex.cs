using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PbsClientDotNet.Model
{
    // Root myDeserializedClass = JsonConvert.DeserializeObject<Root>(myJsonResponse);
    internal class BackupFile
    {
        [JsonProperty("crypt-mode")]
        public string CryptMode;

        [JsonProperty("csum")]
        public string Csum;

        [JsonProperty("filename")]
        public string Filename;

        [JsonProperty("size")]
        public UInt64 Size;
    }

    internal class ChunkUploadStats
    {
        [JsonProperty("compressed_size")]
        public int CompressedSize;

        [JsonProperty("count")]
        public int Count;

        [JsonProperty("duplicates")]
        public int Duplicates;

        [JsonProperty("size")]
        public long Size;
    }

    internal class Unprotected
    {
        [JsonProperty("chunk_upload_stats")]
        public ChunkUploadStats ChunkUploadStats;
    }

    internal class BackupIndex
    {
        [JsonProperty("backup-id")]
        public string BackupId;

        [JsonProperty("backup-time")]
        public int BackupTime;

        [JsonProperty("backup-type")]
        public string BackupType;

        [JsonProperty("files")]
        public List<BackupFile> Files;

        [JsonProperty("signature")]
        public object Signature;

        [JsonProperty("unprotected")]
        public Unprotected Unprotected;
    }
}
