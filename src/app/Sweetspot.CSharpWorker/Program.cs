using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Azure.ServiceBus;
using System.Threading;

namespace Sweetspot.CSharpWorker
{
    public class AppConfig
    {
        public string SubscriptionName { get; private set; }
        public string SubEndpoint { get; }

        public AppConfig(string subscriptionName, string subEndpoint)
        {
            SubscriptionName = subscriptionName;
            SubEndpoint = subEndpoint;
        }
    }

    public class Program
    {
        public static void Main(string[] args)
        {
            var appConfig = GetAppConfig();
            var subConnectionString = new ServiceBusConnectionStringBuilder(appConfig.SubEndpoint);
            var subClient = new Microsoft.Azure.ServiceBus.SubscriptionClient(subConnectionString, appConfig.SubscriptionName);
            subClient.RegisterMessageHandler(HandleMessage, ErrorHandler);
            CreateHostBuilder(args).Build().Run();
        }

        private static Task ErrorHandler(ExceptionReceivedEventArgs arg)
        {
            throw new NotImplementedException();
        }

        private static Task HandleMessage(Message arg1, CancellationToken arg2)
        {
            Console.WriteLine(System.Text.Encoding.UTF8.GetString(arg1.Body));
            return Task.CompletedTask;
        }

        private static AppConfig GetAppConfig()
        {
            var subEndpoint = System.Environment.GetEnvironmentVariable("SB_SAMPLE_ENDPOINT_SEND");
            var subName = System.Environment.GetEnvironmentVariable("SB_SAMPLE_SUBSCRIPTION");
            return new AppConfig(subName, subEndpoint);
        }

        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.UseStartup<Startup>();
                });
    }
}
