﻿//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace Microsoft.Azure.Cosmos.Routing
{
    using System;
    using System.Collections.Generic;
    using System.Collections.ObjectModel;
    using System.Collections.Specialized;
    using System.Diagnostics.CodeAnalysis;
    using System.Net;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Azure.Cosmos.Common;
    using Microsoft.Azure.Documents;
    using Microsoft.Azure.Documents.Client;

    /// <summary>
    /// AddressCache implementation for client SDK. Supports cross region address routing based on 
    /// availability and preference list.
    /// </summary>
    /// Marking it as non-sealed in order to unit test it using Moq framework
    internal class GlobalEndpointManager : IDisposable
    {
        private const int DefaultBackgroundRefreshLocationTimeIntervalInMS = 5 * 60 * 1000;

        private const string BackgroundRefreshLocationTimeIntervalInMS = "BackgroundRefreshLocationTimeIntervalInMS";
        private readonly CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
        private readonly LocationCache locationCache;
        private readonly Uri defaultEndpoint;
        private readonly ConnectionPolicy connectionPolicy;
        private readonly IDocumentClientInternal owner;
        private readonly object refreshLock;
        private readonly AsyncCache<string, AccountProperties> databaseAccountCache;
        private int backgroundRefreshLocationTimeIntervalInMS = GlobalEndpointManager.DefaultBackgroundRefreshLocationTimeIntervalInMS;
        private bool isRefreshing;

        public GlobalEndpointManager(IDocumentClientInternal owner, ConnectionPolicy connectionPolicy)
        {
            this.locationCache = new LocationCache(
                new ReadOnlyCollection<string>(connectionPolicy.PreferredLocations),
                owner.ServiceEndpoint,
                connectionPolicy.EnableEndpointDiscovery,
                connectionPolicy.MaxConnectionLimit,
                connectionPolicy.UseMultipleWriteLocations);

            this.owner = owner;
            this.defaultEndpoint = owner.ServiceEndpoint;
            this.connectionPolicy = connectionPolicy;
            this.databaseAccountCache = new AsyncCache<string, AccountProperties>();

            this.connectionPolicy.PreferenceChanged += this.OnPreferenceChanged;

            this.isRefreshing = false;
            this.refreshLock = new object();
#if !(NETSTANDARD15 || NETSTANDARD16)
            string backgroundRefreshLocationTimeIntervalInMSConfig = System.Configuration.ConfigurationManager.AppSettings[GlobalEndpointManager.BackgroundRefreshLocationTimeIntervalInMS];
            if (!string.IsNullOrEmpty(backgroundRefreshLocationTimeIntervalInMSConfig))
            {
                if (!int.TryParse(backgroundRefreshLocationTimeIntervalInMSConfig, out this.backgroundRefreshLocationTimeIntervalInMS))
                {
                    this.backgroundRefreshLocationTimeIntervalInMS = GlobalEndpointManager.DefaultBackgroundRefreshLocationTimeIntervalInMS;
                }
            }
#endif
        }

        public ReadOnlyCollection<Uri> ReadEndpoints
        {
            get
            {
                return this.locationCache.ReadEndpoints;
            }
        }

        public ReadOnlyCollection<Uri> WriteEndpoints
        {
            get
            {
                return this.locationCache.WriteEndpoints;
            }
        }

        public static async Task<AccountProperties> GetDatabaseAccountFromAnyLocationsAsync(
            Uri defaultEndpoint, IList<string> locations, Func<Uri, Task<AccountProperties>> getDatabaseAccountFn)
        {
            try
            {
                AccountProperties databaseAccount = await getDatabaseAccountFn(defaultEndpoint);
                return databaseAccount;
            }
            catch (Exception e)
            {
                DefaultTrace.TraceInformation("Fail to reach global gateway {0}, {1}", defaultEndpoint, e.ToString());
                if (locations.Count == 0)
                    throw;
            }

            for (int index = 0; index < locations.Count; index++)
            {
                try
                {
                    AccountProperties databaseAccount = await getDatabaseAccountFn(LocationHelper.GetLocationEndpoint(defaultEndpoint, locations[index]));
                    return databaseAccount;
                }
                catch (Exception e)
                {
                    DefaultTrace.TraceInformation("Fail to reach location {0}, {1}", locations[index], e.ToString());
                    if (index == locations.Count - 1)
                    {
                        // if is the last one, throw exception
                        throw;
                    }
                }
            }

            // we should never reach here. Make compiler happy
            throw new Exception();
        }

        public virtual Uri ResolveServiceEndpoint(DocumentServiceRequest request)
        {
            return this.locationCache.ResolveServiceEndpoint(request);
        }

        /// <summary>
        /// Returns location corresponding to the endpoint
        /// </summary>
        public string GetLocation(Uri endpoint)
        {
            return this.locationCache.GetLocation(endpoint);
        }

        public void MarkEndpointUnavailableForRead(Uri endpoint)
        {
            DefaultTrace.TraceInformation("Marking endpoint {0} unavailable for read", endpoint);

            this.locationCache.MarkEndpointUnavailableForRead(endpoint);
        }

        public void MarkEndpointUnavailableForWrite(Uri endpoint)
        {
            DefaultTrace.TraceInformation("Marking endpoint {0} unavailable for Write", endpoint);

            this.locationCache.MarkEndpointUnavailableForWrite(endpoint);
        }

        public bool CanUseMultipleWriteLocations(DocumentServiceRequest request)
        {
            return this.locationCache.CanUseMultipleWriteLocations(request);
        }

        public void Dispose()
        {
            this.connectionPolicy.PreferenceChanged -= this.OnPreferenceChanged;
            if (!this.cancellationTokenSource.IsCancellationRequested)
            {
                // This can cause task canceled exceptions if the user disposes of the object while awaiting an async call.
                this.cancellationTokenSource.Cancel();
                // The background timer task can hit a ObjectDisposedException but it's an async background task
                // that is never awaited on so it will not be thrown back to the caller.
                this.cancellationTokenSource.Dispose();
            }
        }

        public async Task RefreshLocationAsync(AccountProperties databaseAccount, bool forceRefresh = false)
        {
            if (this.cancellationTokenSource.IsCancellationRequested)
            {
                return;
            }

            if (forceRefresh)
            {
                AccountProperties refreshedDatabaseAccount = await this.RefreshDatabaseAccountInternalAsync();

                this.locationCache.OnDatabaseAccountRead(refreshedDatabaseAccount);
                return;
            }

            lock (this.refreshLock)
            {
                if (this.isRefreshing) return;

                this.isRefreshing = true;
            }

            try
            {
                await this.RefreshLocationPrivateAsync(databaseAccount);
            }
            catch
            {
                this.isRefreshing = false;
                throw;
            }
        }

        private async Task RefreshLocationPrivateAsync(AccountProperties databaseAccount)
        {
            if (this.cancellationTokenSource.IsCancellationRequested)
            {
                return;
            }

            DefaultTrace.TraceInformation("RefreshLocationAsync() refreshing locations");

            if (databaseAccount != null)
            {
                this.locationCache.OnDatabaseAccountRead(databaseAccount);
            }

            bool canRefreshInBackground = false;
            if (this.locationCache.ShouldRefreshEndpoints(out canRefreshInBackground))
            {
                if (databaseAccount == null && !canRefreshInBackground)
                {
                    databaseAccount = await this.RefreshDatabaseAccountInternalAsync();

                    this.locationCache.OnDatabaseAccountRead(databaseAccount);
                }

                this.StartRefreshLocationTimer();
            }
            else
            {
                this.isRefreshing = false;
            }
        }

#pragma warning disable VSTHRD100 // Avoid async void methods
        private async void StartRefreshLocationTimer()
#pragma warning restore VSTHRD100 // Avoid async void methods
        {
            if (this.cancellationTokenSource.IsCancellationRequested)
            {
                return;
            }

            try
            {
                await Task.Delay(this.backgroundRefreshLocationTimeIntervalInMS, this.cancellationTokenSource.Token);

                DefaultTrace.TraceInformation("StartRefreshLocationTimerAsync() - Invoking refresh");

                AccountProperties databaseAccount = await this.RefreshDatabaseAccountInternalAsync();

                await this.RefreshLocationPrivateAsync(databaseAccount);
            }
            catch (Exception ex)
            {
                if (this.cancellationTokenSource.IsCancellationRequested && (ex is TaskCanceledException || ex is ObjectDisposedException))
                {
                    return;
                }

                DefaultTrace.TraceCritical("StartRefreshLocationTimerAsync() - Unable to refresh database account from any location. Exception: {0}", ex.ToString());

                this.StartRefreshLocationTimer();
            }
        }

        private Task<AccountProperties> GetDatabaseAccountAsync(Uri serviceEndpoint)
        {
            return this.owner.GetDatabaseAccountInternalAsync(serviceEndpoint, this.cancellationTokenSource.Token);
        }

        private void OnPreferenceChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            this.locationCache.OnLocationPreferenceChanged(new ReadOnlyCollection<string>(
                this.connectionPolicy.PreferredLocations));
        }

        private Task<AccountProperties> RefreshDatabaseAccountInternalAsync()
        {
            return this.databaseAccountCache.GetAsync(
                string.Empty,
                null,
                () => GlobalEndpointManager.GetDatabaseAccountFromAnyLocationsAsync(this.defaultEndpoint, this.connectionPolicy.PreferredLocations, this.GetDatabaseAccountAsync),
                this.cancellationTokenSource.Token,
                forceRefresh: true);
        }
    }
}
