using System;
using System.Threading;
using Hangfire;
using VirtoCommerce.CoreModule.Data.Indexing;
using VirtoCommerce.Domain.Search;
using VirtoCommerce.Platform.Core.Web.Jobs;

namespace VirtoCommerce.CoreModule.Web.BackgroundJobs
{
    /// <summary>
    /// Scales out indexation work through Hangfire.
    /// Throttles queueing so that indexation job doesn't go way faster than action indexation work.
    /// </summary>
    public class HangfireIndexingWorker : IIndexingWorker
    {
        public const string NearRealTimeQueueName = "NearRealTimeIndexing";
        public const string BackgroundQueueName = "BackgroundIndexing";

        public int ThrottleQueueCount { get; set; } = 10;
        public int SleepTimeMs { get; set; } = 100;

        public void IndexDocuments(string documentType, string[] documentIds,
            IndexingPriority priority = IndexingPriority.Default)
        {
            ThrottleByQueueCount(priority, ThrottleQueueCount);

            IndexingJobs.EnqueueIndexDocuments(documentType, documentIds, IndexingPriorityToJobPriority(priority));
        }

        public void DeleteDocuments(string documentType, string[] documentIds,
            IndexingPriority priority = IndexingPriority.Default)
        {
            ThrottleByQueueCount(priority, ThrottleQueueCount);

            IndexingJobs.EnqueueDeleteDocuments(documentType, documentIds, IndexingPriorityToJobPriority(priority));
        }

        protected virtual void ThrottleByQueueCount(IndexingPriority priority, int maxQueueCount)
        {
            string queue = null;
            switch (priority)
            {
                case IndexingPriority.NearRealTime:
                    queue = JobPriority.High;
                    break;
                case IndexingPriority.Background:
                    queue = JobPriority.Low;
                    break;
                default:
                    throw new ArgumentException($"Unkown priority: {priority}");
            }

            var monitoringApi = JobStorage.Current.GetMonitoringApi();
            long queued = 0;
            while (true)
            {
                queued = monitoringApi.EnqueuedCount(queue);
                if (queued <= maxQueueCount)
                {
                    // Check fetched and processing jobs as well.
                    queued += monitoringApi.FetchedCount(queue);
                    if (queued <= maxQueueCount) return;
                }

                Thread.Sleep(SleepTimeMs);
            } 
        }

        protected virtual string IndexingPriorityToJobPriority(IndexingPriority priority)
        {
            switch (priority)
            {
                case IndexingPriority.NearRealTime:
                    return JobPriority.High;

                case IndexingPriority.Background:
                    return JobPriority.Low;

                default:
                    throw new ArgumentException($"Unkown priority: {priority}");
            }
        }
    }
}
