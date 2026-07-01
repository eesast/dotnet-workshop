using System;
using System.Collections.Generic;
using System.Text;

namespace LogAnalyzerClient.Services
{
    public static class AppService
    {
        public static IClientFactory ClientFactory { get; set; } = new NullClientFactory();
    }
}
