using System;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json;

namespace PbsClientDotNet.Model
{
    internal class LoginData
    {
        public string CSRFPreventionToken { get; private set; }
        public string Ticket { get; set; }
        public string Username { get; private set; }
        public int NeedTFA { get; private set; }

        public LoginData([JsonProperty("CSRFPreventionToken")] string cSRFPreventionToken, string ticket, string username, [JsonProperty("NeedTFA")] int needTFA)
        {
            CSRFPreventionToken = cSRFPreventionToken;
            Ticket = ticket;
            Username = username;
            NeedTFA = needTFA;
        }
    }
}
