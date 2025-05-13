using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Text.RegularExpressions;

class ProxyServer
{
    private const int Port = 8888;
    private static readonly string[] BlockedDomains = { "example.com", "google.com" };

    static void Main()
    {
        TcpListener listener = new TcpListener(IPAddress.Any, Port);
        listener.Start();
        Console.WriteLine($"Proxy server started on port {Port}");

        while (true)
        {
            TcpClient client = listener.AcceptTcpClient();
            Thread thread = new Thread(HandleClient);
            thread.Start(client);
        }
    }

    private static void HandleClient(object obj)
    {
        using (TcpClient client = (TcpClient)obj)
        using (NetworkStream clientStream = client.GetStream())
        {
            byte[] buffer = new byte[4096];
            int bytesRead = clientStream.Read(buffer, 0, buffer.Length);
            string request = Encoding.ASCII.GetString(buffer, 0, bytesRead);

            var match = Regex.Match(request, @"^(GET|POST|PUT|DELETE|HEAD|OPTIONS) (http://[^ ]+) HTTP/1.[01]");
            if (!match.Success) return;

            string method = match.Groups[1].Value;
            string fullUrl = match.Groups[2].Value;
            Uri uri = new Uri(fullUrl);

            if (IsBlocked(uri))
            {
                SendBlockedResponse(clientStream, fullUrl);
                return;
            }

            Console.WriteLine($"{fullUrl} - ...");

            string modifiedRequest = Regex.Replace(
                request,
                @"^(" + method + " )http://[^ ]+( HTTP/1.[01]\r\n)",
                "$1" + uri.PathAndQuery + "$2"
            );
            modifiedRequest = Regex.Replace(modifiedRequest, @"(Host: )" + uri.Host, "$1" + uri.Authority);

            using (TcpClient targetClient = new TcpClient(uri.Host, uri.Port))
            using (NetworkStream targetStream = targetClient.GetStream())
            {
                byte[] requestBytes = Encoding.ASCII.GetBytes(modifiedRequest);
                targetStream.Write(requestBytes, 0, requestBytes.Length);

                byte[] responseBuffer = new byte[4096];
                int targetBytesRead;
                bool statusCodeLogged = false;

                while ((targetBytesRead = targetStream.Read(responseBuffer, 0, responseBuffer.Length)) > 0)
                {
                    clientStream.Write(responseBuffer, 0, targetBytesRead);

                    if (!statusCodeLogged)
                    {
                        string response = Encoding.ASCII.GetString(responseBuffer, 0, targetBytesRead);
                        var responseMatch = Regex.Match(response, @"HTTP/1.[01] (\d{3})");
                        if (responseMatch.Success)
                        {
                            Console.WriteLine($"{fullUrl} - {responseMatch.Groups[1].Value}");
                            statusCodeLogged = true;
                        }
                    }
                }
            }
        }
    }

    private static bool IsBlocked(Uri uri)
    {
        foreach (string domain in BlockedDomains)
        {
            if (uri.Host.Contains(domain)) return true;
        }
        return false;
    }

    private static void SendBlockedResponse(NetworkStream stream, string url)
    {
        string response = "HTTP/1.1 403 Forbidden\r\n" +
                          "Content-Type: text/html\r\n\r\n" +
                          "<html><body><h1>Blocked</h1>" +
                          $"<p>Access to {url} is blocked</p></body></html>";
        byte[] bytes = Encoding.ASCII.GetBytes(response);
        stream.Write(bytes, 0, bytes.Length);
        Console.WriteLine($"{url} - 403 Blocked");
    }
}