using PbsClientDotNet;
using PbsClientDotNet.Model;
using System;
using System.Security.Cryptography;
using System.Text;

namespace SampleApp
{
    public class Program
    {
        [STAThread]
        public static void Main(string[] args)
        {
            Run().Wait();
        }

        private static async Task Run()
        {
            try
            {

                BackupApiClient backupApiClient = new BackupApiClient(
                        url: "https://192.168.1.1:8007/",
                        fingerprint: "<<fingerprint>>");
                await backupApiClient.LoginAsync(
                        username: "user@pbs",
                        password: "password");
                Console.WriteLine("Login Successful.");
                await backupApiClient.StartBackupProtocol("test", "host", "test", DateTime.Now);
                
                byte[] data = Encoding.UTF8.GetBytes("Hello, this is a test string!");

                string csum = BitConverter.ToString(SHA256.HashData(data)).Replace("-", "").ToLower();

                MemoryStream ms = new MemoryStream(data);

                await backupApiClient.UploadFixedIndexStream("test.txt", ms);
                ms.Position = 0;
                await backupApiClient.UploadDynamicIndexStream("test.txt", ms);

                await backupApiClient.FinishBackupProtocol();
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
            }
        }

    }
}
