// Copyright (c) Microsoft Corporation
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Microsoft.Xbox.Services.Stats.Manager
{
    using global::System;
    using global::System.Collections.Generic;
    using global::System.Linq;
    using global::System.Threading.Tasks;

    using Microsoft.Xbox.Services.Leaderboard;
    using Microsoft.Xbox.Services.Shared;

    public class StatsManager : IStatsManager
    {
        private static readonly TimeSpan TimePerCall = new TimeSpan(0, 0, 30);
        private static readonly TimeSpan StatsPollTime = new TimeSpan(0, 5, 0);
        private static IStatsManager instance;

        private readonly Dictionary<string, StatsValueDocument> userDocumentMap;
        private readonly List<StatEvent> eventList;
        private readonly CallBufferTimer statTimer;
        private readonly CallBufferTimer statPriorityTimer;

        private readonly StatsService statsService;
        private readonly LeaderboardService leaderboardService;

        private void CheckUserValid(XboxLiveUser user)
        {
            if (user == null || user.XboxUserId == null || !this.userDocumentMap.ContainsKey(user.XboxUserId))
            {
                throw new ArgumentException("user");
            }
        }

        public static IStatsManager Instance
        {
            get
            {
                return instance ?? (instance = XboxLiveContext.UseMockServices ? new MockStatsManager() : (IStatsManager)new StatsManager());
            }
        }

        private StatsManager()
        {
            this.userDocumentMap = new Dictionary<string, StatsValueDocument>();
            this.eventList = new List<StatEvent>();

            this.statTimer = new CallBufferTimer(TimePerCall);
            this.statTimer.TimerCompleteEvent += this.TimerCompleteCallback;

            this.statPriorityTimer = new CallBufferTimer(TimePerCall);
            this.statPriorityTimer.TimerCompleteEvent += this.TimerCompleteCallback;

            XboxLiveContextSettings settings = new XboxLiveContextSettings();
            this.statsService = new StatsService(settings, XboxLiveAppConfiguration.Instance);
            this.leaderboardService = new LeaderboardService(settings, XboxLiveAppConfiguration.Instance);

            RunFlushTimer();
        }

        public void AddLocalUser(XboxLiveUser user)
        {
            if (user == null)
            {
                throw new ArgumentException("user");
            }

            string xboxUserId = user.XboxUserId;
            if (this.userDocumentMap.ContainsKey(xboxUserId))
            {
                throw new ArgumentException("User already in map");
            }

            this.userDocumentMap.Add(xboxUserId, new StatsValueDocument(null));

            this.statsService.GetStatsValueDocument(user).ContinueWith(statsValueDocTask =>
            {
                if (user.IsSignedIn)
                {
                    lock (this.userDocumentMap)
                    {
                        if (this.userDocumentMap.ContainsKey(xboxUserId))
                        {
                            StatsValueDocument document = statsValueDocTask.Result;
                            if (statsValueDocTask.IsFaulted)    // if there was an error, but the user is signed in, we assume offline sign in
                            {
                                this.userDocumentMap[xboxUserId].State = StatsValueDocument.StatValueDocumentState.OfflineNotLoaded;
                            }
                            else
                            {
                                this.userDocumentMap[xboxUserId].MergeStatDocument(document);
                            }

                            document.FlushEvent += (sender, e) =>
                            {
                                if (this.userDocumentMap.ContainsKey(xboxUserId))
                                {
                                    this.FlushToService(user, document);
                                }
                            };

                            this.userDocumentMap[xboxUserId] = document;
                        }
                    }
                }

                this.AddEvent(new StatEvent(StatEventType.LocalUserAdded, user, statsValueDocTask.Exception, new StatEventArgs()));
            });
        }

        public void RemoveLocalUser(XboxLiveUser user)
        {
            this.CheckUserValid(user);
            var xboxUserId = user.XboxUserId;
            var svd = this.userDocumentMap[xboxUserId];

            svd.DoWork();   // before removing the user, apply any pending changes for this user.
            if (svd.IsDirty)
            {
                this.statsService.UpdateStatsValueDocument(user, svd).ContinueWith((continuationTask) =>
                {
                    lock (this.userDocumentMap)
                    {
                        if (this.userDocumentMap.ContainsKey(xboxUserId))
                        {
                            if (continuationTask.IsFaulted && this.ShouldWriteOffline(continuationTask.Exception))
                            {
                                this.WriteOffline(user, svd);
                            }

                            this.AddEvent(new StatEvent(StatEventType.LocalUserRemoved, user, continuationTask.Exception, new StatEventArgs()));
                            this.userDocumentMap.Remove(xboxUserId);
                        }
                    }
                });
            }
            else
            {
                this.AddEvent(new StatEvent(StatEventType.LocalUserRemoved, user, null, new StatEventArgs()));
                lock (this.userDocumentMap)
                {
                    this.userDocumentMap.Remove(xboxUserId);
                }
            }
        }

        public StatValue GetStat(XboxLiveUser user, string statName)
        {
            this.CheckUserValid(user);
            if (statName == null)
            {
                throw new ArgumentException("statName");
            }

            return this.userDocumentMap[user.XboxUserId].GetStat(statName);
        }

        public List<string> GetStatNames(XboxLiveUser user)
        {
            this.CheckUserValid(user);
            return this.userDocumentMap[user.XboxUserId].GetStatNames();
        }

        public void SetStatAsNumber(XboxLiveUser user, string statName, double value)
        {
            this.CheckUserValid(user);

            if (statName == null)
            {
                throw new ArgumentException("statName");
            }

            this.userDocumentMap[user.XboxUserId].SetStat(statName, value);
        }

        public void SetStatAsInteger(XboxLiveUser user, string statName, Int64 value)
        {
            this.CheckUserValid(user);

            if (statName == null)
            {
                throw new ArgumentException("statName");
            }

            this.userDocumentMap[user.XboxUserId].SetStat(statName, value);
        }

        public void SetStatAsString(XboxLiveUser user, string statName, string value)
        {
            this.CheckUserValid(user);

            if (statName == null)
            {
                throw new ArgumentException("statName");
            }

            this.userDocumentMap[user.XboxUserId].SetStat(statName, value);
        }

        public void DeleteStat(XboxLiveUser user, string statName)
        {
            this.CheckUserValid(user);

            if (statName == null)
            {
                throw new ArgumentException("statName");
            }

            this.userDocumentMap[user.XboxUserId].DeleteStat(statName);
        }

        public void RequestFlushToService(XboxLiveUser user, bool isHighPriority = false)
        {
            this.CheckUserValid(user);

            List<XboxLiveUser> userVec = new List<XboxLiveUser>(1) { user };
            if (isHighPriority)
            {
                this.statPriorityTimer.Fire(userVec);
            }
            else
            {
                this.statTimer.Fire(userVec);
            }
        }

        public List<StatEvent> DoWork()
        {
            lock (this.userDocumentMap)
            {
                var copyList = this.eventList.ToList();
                foreach (var userContextPair in this.userDocumentMap)
                {
                    userContextPair.Value.DoWork();
                }

                this.eventList.Clear();
                return copyList;
            }
        }

        private bool ShouldWriteOffline(AggregateException exception)
        {
            return false; // offline not implemented yet
        }

        private void WriteOffline(XboxLiveUser user, StatsValueDocument document)
        {
            // TODO: implement
        }

        private void FlushToService(XboxLiveUser user, StatsValueDocument document)
        {
            if (user == null)
            {
                // User could have been removed.
                return;
            }

            document.ClearDirtyState();
            if (document.State != StatsValueDocument.StatValueDocumentState.Loaded)   // if not loaded, try and get the SVD from the service
            {
                this.statsService.GetStatsValueDocument(user).ContinueWith((continuationTask) =>
                {
                    lock (this.userDocumentMap)
                    {
                        if (this.userDocumentMap.ContainsKey(user.XboxUserId))
                        {
                            if (!continuationTask.IsFaulted)
                            {
                                var updatedSvd = continuationTask.Result;
                                this.userDocumentMap[user.XboxUserId].MergeStatDocument(updatedSvd);
                                UpdateStatsValueDocument(user, updatedSvd);
                            }

                            // How do you handle if this failed?
                        }
                        else
                        {
                            // log error: User not found in flush_to_service lambda
                        }
                    }
                });
            }
            else
            {
                UpdateStatsValueDocument(user, document);
            }
        }
        private void UpdateStatsValueDocument(XboxLiveUser user, StatsValueDocument document)
        {
            if (user == null)
            {
                // User could have been removed.
                return;
            }

            this.statsService.UpdateStatsValueDocument(user, document).ContinueWith((continuationTask) =>
            {
                lock (this.userDocumentMap)
                {
                    if (this.userDocumentMap.ContainsKey(user.XboxUserId))
                    {
                        if (continuationTask.IsFaulted)
                        {
                            if (this.ShouldWriteOffline(continuationTask.Exception))
                            {
                                var userSvd = this.userDocumentMap[user.XboxUserId];
                                if (userSvd.State == StatsValueDocument.StatValueDocumentState.Loaded)
                                {
                                    userSvd.State = StatsValueDocument.StatValueDocumentState.OfflineLoaded;
                                }

                                this.WriteOffline(user, userSvd);
                            }
                            else
                            {
                                // log error: Stats manager could not write stats value document
                            }
                        }

                        this.AddEvent(new StatEvent(StatEventType.StatUpdateComplete, user, continuationTask.Exception, new StatEventArgs()));
                    }
                }
            });
        }

        internal void AddEvent(StatEvent statEvent)
        {
            lock (this.eventList)
            {
                this.eventList.Add(statEvent);
            }
        }

        private void RunFlushTimer()
        {
            // Setup another refresh for the future.
            Task.Delay(StatsPollTime).ContinueWith(
                delayTask =>
                {
                    try
                    {
                        lock (this.userDocumentMap)
                        {
                            foreach (var statValueDoc in this.userDocumentMap.Values)
                            {
                                if (statValueDoc.IsDirty)
                                {
                                    this.FlushToService(statValueDoc.User, statValueDoc);
                                }
                            }
                        }
                    }
                    finally
                    {
                        this.RunFlushTimer();
                    }
                });
        }

        private void TimerCompleteCallback(object caller, CallBufferReturnObject returnObject)
        {
            if (returnObject.UserList.Count != 0)
            {
                this.RequestFlushToServiceCallback(returnObject.UserList[0]);
            }
        }

        private void RequestFlushToServiceCallback(XboxLiveUser user)
        {
            StatsValueDocument document;
            if (this.userDocumentMap.TryGetValue(user.XboxUserId, out document) && document.IsDirty)
            {
                document.DoWork();
                this.FlushToService(user, document);
            }
        }

        public void GetLeaderboard(XboxLiveUser user, LeaderboardQuery query)
        {
            this.CheckUserValid(user);
            this.leaderboardService.GetLeaderboardAsync(user, query).ContinueWith(responseTask =>
            {
                this.AddEvent(
                    new StatEvent(StatEventType.GetLeaderboardComplete,
                        user,
                        responseTask.Exception,
                        new LeaderboardResultEventArgs(responseTask.Result)
                    ));
            });
        }
    }
}