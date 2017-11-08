// This indexing jobs implementation allows only one job to perform indexing.
// If some job is started successfully, all other jobs will terminate with "Indexation is already in progress" error until the first job is finished.
// The synchronization is done by using the Hangfire distributed lock infrastructure.
// This supports scaled-out scenario's.
//
// This class also supports queueing index batches through the Hangfire job scheduler,
// so that we can spread the indexation work over the entire web farm.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Hangfire;
using Hangfire.Common;
using Microsoft.Practices.ObjectBuilder2;
using VirtoCommerce.CoreModule.Web.Model.PushNotifcations;
using VirtoCommerce.Domain.Search;
using VirtoCommerce.Platform.Core.Common;
using VirtoCommerce.Platform.Core.Settings;
using VirtoCommerce.Platform.Core.Web.Jobs;

namespace VirtoCommerce.CoreModule.Web.BackgroundJobs
{
    public sealed class IndexingJobs
    {
        private static readonly MethodInfo _indexChangesJobMethod;
        private static readonly MethodInfo _manualIndexAllJobMethod;

        private readonly IndexDocumentConfiguration[] _documentsConfigs;
        private readonly IIndexingManager _indexingManager;
        private readonly ISettingsManager _settingsManager;
        private readonly IndexProgressHandler _progressHandler;
        private readonly IIndexingInterceptor[] _interceptors;

        static IndexingJobs()
        {
            _indexChangesJobMethod = typeof(IndexingJobs).GetMethod("IndexChangesJob");
            _manualIndexAllJobMethod = typeof(IndexingJobs).GetMethod("IndexAllDocumentsJob");
        }

        public IndexingJobs(IndexDocumentConfiguration[] documentsConfigs, IIndexingManager indexingManager, ISettingsManager settingsManager,
            IndexProgressHandler progressHandler, IIndexingInterceptor[] interceptors = null)
        {
            _documentsConfigs = documentsConfigs;
            _indexingManager = indexingManager;
            _settingsManager = settingsManager;
            _progressHandler = progressHandler;
            _interceptors = interceptors;
        }

        // Enqueue a background job with single notification object for all given options
        public static IndexProgressPushNotification Enqueue(string currentUserName, IndexingOptions[] options)
        {
            var notification = IndexProgressHandler.CreateNotification(currentUserName, null);

            // Hangfire will set cancellation token.
            BackgroundJob.Enqueue<IndexingJobs>(j =>
                j.IndexAllDocumentsJob(currentUserName, notification.Id, options, JobCancellationToken.Null));

            return notification;
        }

        // Cancel current indexation if there is one
        public static void CancelIndexation()
        {
            var processingJob = JobStorage.Current.GetMonitoringApi().ProcessingJobs(0, int.MaxValue)
                .FirstOrDefault(x => x.Value.Job.Method == _indexChangesJobMethod || x.Value.Job.Method == _manualIndexAllJobMethod);
            
            if (string.IsNullOrEmpty(processingJob.Key)) return;

            try
            {
                BackgroundJob.Delete(processingJob.Key);
            }
            catch
            {
                // Ignore concurrency exceptions, when somebody else cancelled it as well.
            }
        }

        // One-time job for manual indexation
        [Queue(JobPriority.Normal)]
        public async Task IndexAllDocumentsJob(string userName, string notificationId, IndexingOptions[] options,
            IJobCancellationToken cancellationToken)
        {
            await WithInterceptorsAsync(options, async o =>
            {
                try
                {
                    if (await RunIndexJobAsync(userName, notificationId, false, o, IndexAllDocumentsAsync, cancellationToken))
                    {
                        // Indexation manager might re-use the jobs to scale out indexation.
                        // Wait for all indexing jobs to complete, before telling interceptors we're ready.
                        // This method is running as a job as well, so skip this job.
                        // Scale out background indexation jobs are scheduled as low.
                        await WaitForIndexationJobsToBeReadyAsync(JobPriority.Low,
                            x => x.Method != _manualIndexAllJobMethod && x.Method != _indexChangesJobMethod);
                    }
                }
                finally
                {
                    // Report indexation summary
                    _progressHandler.Finish();
                }
            });
        }

        // Recurring job for automatic changes indexation.
        // It should push separate notification for each document type if any changes were indexed for this type
        [Queue(JobPriority.Normal)]
        public async Task IndexChangesJob(string documentType, IJobCancellationToken cancellationToken)
        {
            var allOptions = GetAllIndexingOptions(documentType);
            await WithInterceptorsAsync(allOptions, async o =>
            {
                try
                {
                    // Create different notification for each option (document type)
                    var executed = true;
                    foreach (var options in o)
                    {
                        executed = executed && await RunIndexJobAsync(null, null, true, new[] {options},
                                       IndexChangesAsync, cancellationToken);
                    }

                    if (executed)
                    {
                        // Indexation manager might re-use the jobs to scale out indexation.
                        // Wait for all indexing jobs to complete, before telling interceptors we're ready.
                        // This method is running as a job as well, so skip this job.
                        // Scale out background indexation jobs are scheduled as low.
                        await WaitForIndexationJobsToBeReadyAsync(JobPriority.Low,
                            x => x.Method != _manualIndexAllJobMethod && x.Method != _indexChangesJobMethod);
                    }
                }
                finally
                {
                    // Report indexation summary
                    _progressHandler.Finish();
                }
            });
        }

        #region Scale-out indexation actions for indexing worker

        public static void EnqueueIndexDocuments(string documentType, string[] documentIds, string priority = JobPriority.Normal)
        {
            switch (priority)
            {
                case JobPriority.High:
                    BackgroundJob.Enqueue<IndexingJobs>(x => x.IndexDocumentsHighPrio(documentType, documentIds));
                    break;
                case JobPriority.Normal:
                    BackgroundJob.Enqueue<IIndexingManager>(x => x.IndexDocumentsAsync(documentType, documentIds));
                    break;
                case JobPriority.Low:
                    BackgroundJob.Enqueue<IndexingJobs>(x => x.IndexDocumentsLowPrio(documentType, documentIds));
                    break;
                default:
                    throw new ArgumentException($"Unkown priority: {priority}", nameof(priority));
            }
        }

        public static void EnqueueDeleteDocuments(string documentType, string[] documentIds, string priority = JobPriority.Normal)
        {
            switch (priority)
            {
                case JobPriority.High:
                    BackgroundJob.Enqueue<IndexingJobs>(x => x.DeleteDocumentsHighPrio(documentType, documentIds));
                    break;
                case JobPriority.Normal:
                    BackgroundJob.Enqueue<IIndexingManager>(x => x.DeleteDocumentsAsync(documentType, documentIds));
                    break;
                case JobPriority.Low:
                    BackgroundJob.Enqueue<IndexingJobs>(x => x.DeleteDocumentsLowPrio(documentType, documentIds));
                    break;
                default:
                    throw new ArgumentException($"Unkown priority: {priority}", nameof(priority));
            }
        }

        // Use hard-code methods to easily set queue for Hangfire.

        [Queue(JobPriority.High)]
        public void IndexDocumentsHighPrio(string documentType, string[] documentIds)
        {
            _indexingManager.IndexDocumentsAsync(documentType, documentIds);
        }

        [Queue(JobPriority.Low)]
        public void IndexDocumentsLowPrio(string documentType, string[] documentIds)
        {
            _indexingManager.IndexDocumentsAsync(documentType, documentIds);
        }

        [Queue(JobPriority.High)]
        public void DeleteDocumentsHighPrio(string documentType, string[] documentIds)
        {
            _indexingManager.DeleteDocumentsAsync(documentType, documentIds);
        }

        [Queue(JobPriority.Low)]
        public void DeleteDocumentsLowPrio(string documentType, string[] documentIds)
        {
            _indexingManager.DeleteDocumentsAsync(documentType, documentIds);
        }

        #endregion

        private Task<bool> RunIndexJobAsync(string currentUserName, string notificationId, bool suppressInsignificantNotifications,
            IEnumerable<IndexingOptions> allOptions, Func<IndexingOptions, ICancellationToken, Task> indexationFunc,
            IJobCancellationToken cancellationToken)
        {
            // Reset progress handler to initial state
            _progressHandler.Start(currentUserName, notificationId, suppressInsignificantNotifications);

            // Make sure only one indexation job can run in the cluster.
            // CAUTION: locking mechanism assumes single threaded execution.
            IDisposable lck = null;
            try
            {
                lck = JobStorage.Current.GetConnection().AcquireDistributedLock("IndexationJob", TimeSpan.Zero);
            }
            catch
            {
                // todo: Check wait in calling method
                _progressHandler.AlreadyInProgress();
                return Task.FromResult(false);
            }

            // Begin indexation
            try
            {
                foreach (var options in allOptions)
                {
                    indexationFunc(options, new JobCancellationTokenWrapper(cancellationToken)).Wait();
                }

                return Task.FromResult(true);
            }
            catch (OperationCanceledException)
            {
                _progressHandler.Cancel();
                return Task.FromResult(false);
            }
            catch (Exception ex)
            {
                _progressHandler.Exception(ex);
                return Task.FromResult(false);
            }
            finally
            {
                // Release the syncronization object.
                lck.Dispose();
            }
        }

        private async Task IndexAllDocumentsAsync(IndexingOptions options, ICancellationToken cancellationToken)
        {
            var oldIndexationDate = GetLastIndexationDate(options.DocumentType);
            var newIndexationDate = DateTime.UtcNow;

            await _indexingManager.IndexAsync(options, _progressHandler.Progress, cancellationToken);

            // Save indexation date to prevent changes from being indexed again
            SetLastIndexationDate(options.DocumentType, oldIndexationDate, newIndexationDate);
        }

        private async Task IndexChangesAsync(IndexingOptions options, ICancellationToken cancellationToken)
        {
            var oldIndexationDate = options.StartDate;
            var newIndexationDate = DateTime.UtcNow;

            options.EndDate = oldIndexationDate == null ? null : (DateTime?)newIndexationDate;

            await _indexingManager.IndexAsync(options, _progressHandler.Progress, cancellationToken);

            // Save indexation date. It will be used as a start date for the next indexation
            SetLastIndexationDate(options.DocumentType, oldIndexationDate, newIndexationDate);
        }

        private async Task WithInterceptorsAsync(ICollection<IndexingOptions> options, Func<ICollection<IndexingOptions>, Task> action)
        {
            try
            {
                _interceptors?.ForEach(x => x.OnBegin(options.ToArray()));

                await action(options);

                _interceptors?.ForEach(x => x.OnEnd(options.ToArray()));
            }
            catch (Exception ex)
            {
                _interceptors?.ForEach(x => x.OnEnd(options.ToArray(), ex));
            }
        }

        private IList<IndexingOptions> GetAllIndexingOptions(string documentType)
        {
            var configs = _documentsConfigs.AsQueryable();

            if (!string.IsNullOrEmpty(documentType))
            {
                configs = configs.Where(c => c.DocumentType.EqualsInvariant(documentType));
            }

            var result = configs.Select(c => GetIndexingOptions(c.DocumentType)).ToList();
            return result;
        }

        private IndexingOptions GetIndexingOptions(string documentType)
        {
            return new IndexingOptions
            {
                DocumentType = documentType,
                DeleteExistingIndex = false,
                StartDate = GetLastIndexationDate(documentType),
                BatchSize = GetBatchSize(),
            };
        }

        private DateTime? GetLastIndexationDate(string documentType)
        {
            return _settingsManager.GetValue(GetLastIndexationDateName(documentType), (DateTime?)null);
        }

        private void SetLastIndexationDate(string documentType, DateTime? oldValue, DateTime newValue)
        {
            var currentValue = GetLastIndexationDate(documentType);
            if (currentValue == oldValue)
            {
                _settingsManager.SetValue(GetLastIndexationDateName(documentType), newValue);
            }
        }

        private static string GetLastIndexationDateName(string documentType)
        {
            return $"VirtoCommerce.Search.IndexingJobs.IndexationDate.{documentType}";
        }

        private int GetBatchSize()
        {
            return _settingsManager.GetValue("VirtoCommerce.Search.IndexPartitionSize", 50);
        }

        private static async Task WaitForIndexationJobsToBeReadyAsync(string queue,
            Func<Job, bool> jobPredicate, int maxQueueCount = 0)
        {
            var monitoringApi = JobStorage.Current.GetMonitoringApi();
            while (true)
            {
                var hasQueuedIndexingJobs = monitoringApi.Queues()
                    .FirstOrDefault(x => x.Name.Equals(queue, StringComparison.OrdinalIgnoreCase))
                    ?.FirstJobs
                    .Where(x => jobPredicate == null || jobPredicate(x.Value.Job))
                    .Where(x => x.Value.Job.Method.DeclaringType == typeof(IndexingJobs))
                    .Any();
                if (!hasQueuedIndexingJobs.GetValueOrDefault())
                {
                    var hasFetchedIndexingJobs = monitoringApi.FetchedJobs(queue, 0, int.MaxValue)
                        ?.Where(x => jobPredicate == null || jobPredicate(x.Value.Job))
                        .Where(x => x.Value.Job.Method.DeclaringType == typeof(IndexingJobs))
                        .Any();
                    if (!hasFetchedIndexingJobs.GetValueOrDefault())
                    {
                        var hasProcessingIndexingJobs = monitoringApi.ProcessingJobs(0, int.MaxValue)
                            ?.Where(x => jobPredicate == null || jobPredicate(x.Value.Job))
                            .Where(x => x.Value.Job.Method.DeclaringType == typeof(IndexingJobs))
                            .Any();
                        if (!hasProcessingIndexingJobs.GetValueOrDefault())
                            return;
                    }
                }

                await Task.Delay(100);
            }
        }
    }
}
