using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace vmPing.Classes
{
    public static class NotificationSender
    {
        public static void SendWebhook(string alertType, string hostname, string alias)
        {
            if (string.IsNullOrWhiteSpace(ApplicationOptions.WebhookUrl))
                return;

            Task.Run(() =>
            {
                try
                {
                    string message = $"[{alertType}] {hostname}";
                    if (!string.IsNullOrEmpty(alias))
                        message += $" ({alias})";

                    string json = $@"{{
                        ""text"": ""{message}"",
                        ""content"": ""{message}""
                    }}";

                    var request = (HttpWebRequest)WebRequest.Create(ApplicationOptions.WebhookUrl);
                    request.Method = "POST";
                    request.ContentType = "application/json";
                    
                    using (var streamWriter = new StreamWriter(request.GetRequestStream()))
                    {
                        streamWriter.Write(json);
                    }

                    using (var response = (HttpWebResponse)request.GetResponse())
                    {
                        // Dispose response
                    }
                }
                catch
                {
                    // Ignore errors for now
                }
            });
        }
    }
}
