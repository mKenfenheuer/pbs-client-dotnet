using System;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json;

namespace PbsClientDotNet.Model
{
    public class LoginResponse
    {
        public bool Successful { get; private set; }
        public string Message { get; private set; }
        public bool OtpRequired { get; private set; }

        public LoginResponse(bool successful, string message, bool otpRequired)
        {
            Successful = successful;
            Message = message;
            OtpRequired = otpRequired;
        }
    }
}
