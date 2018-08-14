namespace AggregateMailService
{
    public class SmtpServer
    {
        public SmtpServer(string host, int port, string username, string password)
        {
            Host = host;
            Port = port;
            UserName = username;
            Password = password;
        }

        public string Host { get; }
        public int Port { get; }
        public string UserName { get; }
        public string Password { get; }
    }
}