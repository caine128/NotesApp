using System;
using System.Collections.Generic;
using System.Text;

namespace NotesApp.Worker
{
    /// <summary>
    /// Temporary background worker that does nothing important yet.
    /// We'll replace this with our Outbox/AI/Notification processing later.
    /// </summary>
    public class Worker : BackgroundService
    {
        private readonly ILogger<Worker> _logger;

        public Worker(ILogger<Worker> logger)
        {
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // For now, just log every minute so we know the worker is alive.
            while (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation("Worker running at: {time}", DateTimeOffset.Now);
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
        }
    }
}
