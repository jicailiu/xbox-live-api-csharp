// Copyright (c) Microsoft Corporation
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Microsoft.Xbox.Services.Stats.Manager
{
    using global::System;
    using global::System.Collections.Generic;
    using global::System.Linq;

    using Microsoft.Xbox.Services.Leaderboard;
    using Microsoft.Xbox.Services.Shared;

    public class StatsManager : IStatsManager
    {
        private static IStatsManager instance;
        private static readonly TimeSpan TimePerCall = new TimeSpan(0, 0, 5);

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
            this.statTimer.TimerCompleteEvent += this.CallBufferTimerCallback;

            this.statPriorityTimer = new CallBufferTimer(TimePerCall);
            this.statPriorityTimer.TimerCompleteEvent += this.CallBufferTimerCallback;

            XboxLiveContextSettings settings = new XboxLiveContextSettings();
            this.statsService = new StatsService(settings, XboxLiveAppConfiguration.Instance);
            this.leaderboardService = new LeaderboardService(settings, XboxLiveAppConfiguration.Instance);
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
                if (!statsValueDocTask.IsFaulted && user.IsSignedIn)
                {
                    lock (this.userDocumentMap)
                    {
                        if (this.userDocumentMap.ContainsKey(xboxUserId))
                        {
                            StatsValueDocument document = statsValueDocTask.Result;
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
            if (svd.IsDirty)
            {
                svd.DoWork();
                //var serializedSVD = svd.Serialize();  // write offline
                this.statsService.UpdateStatsValueDocument(user, svd).ContinueWith((continuationTask) =>
                {
                    if (this.ShouldWriteOffline(continuationTask.Exception))
                    {
                        // write offline
                    }

                    this.AddEvent(new StatEvent(StatEventType.LocalUserRemoved, user, continuationTask.Exception, new StatEventArgs()));
                });
            }
            else
            {
                this.AddEvent(new StatEvent(StatEventType.LocalUserRemoved, user, null, new StatEventArgs()));
            }

            this.userDocumentMap.Remove(xboxUserId);
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
            this.RequestFlushToService(user);
        }

        public void SetStatAsInteger(XboxLiveUser user, string statName, Int64 value)
        {
            this.CheckUserValid(user);

            if (statName == null)
            {
                throw new ArgumentException("statName");
            }

            this.userDocumentMap[user.XboxUserId].SetStat(statName, value);
            this.RequestFlushToService(user);
        }

        public void SetStatAsString(XboxLiveUser user, string statName, string value)
        {
            this.CheckUserValid(user);

            if (statName == null)
            {
                throw new ArgumentException("statName");
            }

            this.userDocumentMap[user.XboxUserId].SetStat(statName, value);
            this.RequestFlushToService(user);
        }

        public void RequestFlushToService(XboxLiveUser user, bool isHighPriority = false)
        {
            this.CheckUserValid(user);
            this.userDocumentMap[user.XboxUserId].DoWork();

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

        private void FlushToService(XboxLiveUser user, StatsValueDocument document)
        {
            //var serializedSVD = statsUserContext.statsValueDocument.Serialize();
            this.statsService.UpdateStatsValueDocument(user, document).ContinueWith((continuationTask) =>
            {
                if (continuationTask.IsFaulted)
                {
                    if (this.ShouldWriteOffline(continuationTask.Exception))
                    {
                        //WriteOffline(statsUserContext, serializedSVD);    // todo: add offline support
                    }
                    else
                    {
                        // log error
                    }
                }

                this.AddEvent(new StatEvent(StatEventType.StatUpdateComplete, user, continuationTask.Exception, new StatEventArgs()));
            });
        }

        internal void AddEvent(StatEvent statEvent)
        {
            lock (this.eventList)
            {
                this.eventList.Add(statEvent);
            }
        }

        private void CallBufferTimerCallback(object caller, CallBufferReturnObject returnObject)
        {
            if (returnObject.UserList.Count != 0)
            {
                this.FlushToServiceCallback(returnObject.UserList[0]);
            }
        }

        private void FlushToServiceCallback(XboxLiveUser user)
        {
            StatsValueDocument document;
            if (this.userDocumentMap.TryGetValue(user.XboxUserId, out document) && document.IsDirty)
            {
                document.DoWork();
                document.ClearDirtyState();
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