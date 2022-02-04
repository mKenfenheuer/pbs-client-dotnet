using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PbsClientDotNet.Model
{
    internal class AppendRequest
    {
        [JsonProperty("digest-list")]
        public List<string> DigestList;

        [JsonProperty("offset-list")]
        public List<UInt64> OffsetList;

        [JsonProperty("wid")]
        public int Wid;
    }
}
