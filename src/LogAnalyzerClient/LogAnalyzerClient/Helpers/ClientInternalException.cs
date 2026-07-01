using System;

namespace LogAnalyzerClient.Helpers
{
    internal class ClientInternalException : Exception
    {
        public ClientInternalException(string message) : base(message)
        {
        }

        public ClientInternalException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}
