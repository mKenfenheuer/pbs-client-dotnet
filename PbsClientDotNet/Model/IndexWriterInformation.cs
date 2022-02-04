using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace PbsClientDotNet.Model
{
    internal class IndexWriterInformation
    {
        public BackupFile FileInfo { get; set; }
        public string Name { get; set; }
        public UInt64 Size { get; set; } = 0;
        public UInt64 Chunks { get; set; } = 0;
        public List<string> Digests { get; set; } = new List<string>();
        public List<UInt64> Offsets { get; set; } = new List<UInt64>();
        public List<UInt64> EndOffsets { get; set; } = new List<UInt64>();


        public List<string> UnappendedDigests { get; set; } = new List<string>();
        public List<UInt64> UnappendedOffsets { get; set; } = new List<UInt64>();

        public void AppendChunkToHash(IDataBlob blob)
        {
            Offsets.Add(Size);
            UnappendedOffsets.Add(Size);
            Chunks++;
            Size += (UInt64)blob.GetDataLength();
            EndOffsets.Add(Size);
            Digests.Add(blob.GetCsum());
            UnappendedDigests.Add(blob.GetCsum());
        }
    }
}
