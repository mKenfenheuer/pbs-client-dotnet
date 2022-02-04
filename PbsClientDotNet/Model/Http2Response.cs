using Http2.Hpack;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PbsClientDotNet.Model
{
    internal class Http2Response
    {
        public bool Success { get; set; }   
        public IEnumerable<HeaderField> Headers { get; set; } = new List<HeaderField>();
        public byte[] Content { get; set; } = new byte[0];

        public string GetContentAsString()
        {
            return Encoding.UTF8.GetString(Content);
        }

        public string ToString()
        {
            return $"{Headers.Select(h => $"{h.Name}={h.Value}\r\n").Aggregate((h1, h2) => h1 + h2)}\r\n{GetContentAsString()}";
        }
    }
}
