using Force.Crc32;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace PbsClientDotNet.Model
{
    internal class DataBlobMagic
    {
        public static byte[] UnencryptedUncompressed => new byte[] { 66, 171, 56, 7, 190, 131, 112, 161 };
        public static byte[] UnencryptedCompressed => new byte[] { 49, 185, 88, 66, 111, 182, 163, 127 };
        public static byte[] EncryptedUncompressed => new byte[] { 123, 103, 133, 190, 34, 45, 76, 240 };
        public static byte[] EncryptedCompressed => new byte[] { 230, 89, 27, 191, 11, 191, 216, 11 };
    }
    public interface IDataBlob
    {
        int GetDataLength();
        string GetCryptMode();
        string GetCsum();
        Task Compress();
        byte[] RawByteFormat();
    }

    public class UnencryptedDataBlob : IDataBlob
    {
        private byte[] _magic = DataBlobMagic.UnencryptedUncompressed;
        private byte[] _crc32 = new byte[4];
        private byte[] _data = new byte[0];

        public UnencryptedDataBlob(byte[] data)
        {
            _crc32 = BitConverter.GetBytes(Crc32Algorithm.Compute(data));
            _data = data;
        }

        public Task Compress()
        {
            throw new NotImplementedException();
        }

        public string GetCryptMode()
        {
            return "none";
        }

        public string GetCsum()
        {
            return BitConverter.ToString(SHA256.HashData(_data)).Replace("-", "").ToLower();
        }

        public int GetDataLength()
        {
            return _data.Length;
        }

        public byte[] RawByteFormat()
        {
            return DataBlobMagic.UnencryptedUncompressed.Concat(_crc32).Concat(_data).ToArray();
        }
    }


    public class EncryptedDataBlob : IDataBlob
    {
        private byte[] _magic = DataBlobMagic.EncryptedUncompressed;
        private byte[] _crc32 = new byte[4];
        private byte[] _iv = new byte[16];
        private byte[] _tag = new byte[16];
        private byte[] _data = new byte[0];

        public EncryptedDataBlob(byte[] data, byte[] iv, byte[] tag)
        {
            _crc32 = BitConverter.GetBytes(Crc32Algorithm.Compute(data));
            _data = data;
            _iv = iv;
            _tag = tag;
        }

        public EncryptedDataBlob(byte[] data, byte[] iv, byte[] tag, byte[] magic)
        {
            _data = data;
            _magic = magic;
            _iv = iv;
            _tag = tag;
        }

        public Task Compress()
        {
            throw new NotImplementedException();
        }

        public string GetCryptMode()
        {
            return "AES_256_GCM";
        }

        public string GetCsum()
        {
            return BitConverter.ToString(SHA256.HashData(_data)).Replace("-", "").ToLower();
        }

        public int GetDataLength()
        {
            return _data.Length;
        }

        public byte[] RawByteFormat()
        {
            return _magic.Concat(_crc32).Concat(_iv).Concat(_tag).Concat(_data).ToArray();
        }
    }
}
