using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PbsClientDotNet.Model
{
    internal class WriterResponse
    {
        [JsonProperty("data")]
        public int Data { get; set; }
    }
}
