using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using OpenFTTH.SearchIndexer.Consumer;
using System;

namespace OpenFTTH.SearchIndexer
{
    public class Startup : IHostedService
    {
        private readonly IHostApplicationLifetime _applicationLifetime;
        private IAddressConsumer _consumer;

        public Startup(IHostApplicationLifetime applicationLifetime, IAddressConsumer consumer)
        {
            _applicationLifetime = applicationLifetime;
            _consumer = consumer;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _applicationLifetime.ApplicationStarted.Register(OnStarted);
            _applicationLifetime.ApplicationStopping.Register(OnStopped);

            MarkAsReady();
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        private void MarkAsReady()
        {
            File.Create("/tmp/healthy");
        }

        private void OnStarted()
        {
            // TODO use enviroment variable
            var bulkSetup = true;
            if (bulkSetup)
            {
                _consumer.SubscribeBulk();
                while (!_consumer.IsBulkFinished())
                    Thread.Sleep(1);

                _consumer.Dispose();
            }

            // Call interface that creates the schemas.
            _consumer.Subscribe();
            // Start the kafka consumer
        }

        private void OnStopped()
        {
            _consumer.Dispose();
            // Dispose
        }
    }
}