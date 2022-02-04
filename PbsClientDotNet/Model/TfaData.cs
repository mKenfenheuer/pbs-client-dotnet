using System;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json;

namespace PbsClientDotNet.Model
{
    internal class TfaData
    {
        public string Ticket { get; private set; }

        public TfaData(string ticket)
        {
            Ticket = ticket;
        }
    }
}
