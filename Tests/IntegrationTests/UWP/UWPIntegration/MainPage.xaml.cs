// Copyright (c) Microsoft Corporation
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
// 

namespace UWPIntegration
{
    using System;
    using System.Linq;

    using Windows.UI.Xaml;
    using Windows.UI.Xaml.Controls;

    using Microsoft.Xbox.Services;
    using Microsoft.Xbox.Services.Leaderboard;
    using Microsoft.Xbox.Services.Social.Manager;
    using Microsoft.Xbox.Services.Stats.Manager;
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.Runtime.CompilerServices;
    using System.Threading.Tasks;

    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainPage : Page, INotifyPropertyChanged
    {
        private int jumps;
        private int headshots;
        private LeaderboardResult leaderboard;
        private readonly XboxLiveUser user;

        public MainPage()
        {
            this.InitializeComponent();
            this.user = new XboxLiveUser();
            DoWork();
        }

        public LeaderboardResult LeaderboardResult
        {
            get
            {
                return this.leaderboard;
            }
            set
            {
                this.leaderboard = value;
                this.OnPropertyChanged();
            }
        }

        public XboxLiveUser User
        {
            get
            {
                return this.user;
            }
        }

        private async void SignInButton_Click(object sender, RoutedEventArgs e)
        {
            var signInResult = await this.user.SignInAsync();

            await Windows.ApplicationModel.Core.CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
            {
                if (signInResult.Status == SignInStatus.Success)
                {
                    this.OnPropertyChanged("User");
                    StatsManager.Instance.AddLocalUser(this.user);
                    SocialManager.Instance.AddLocalUser(this.user, SocialManagerExtraDetailLevel.None);
                }
            });
        }

        private void globalLeaderboardButton_Click(object sender, RoutedEventArgs e)
        {
            if (this.user.IsSignedIn)
            {
                LeaderboardQuery query = new LeaderboardQuery
                {
                    MaxItems = 3,
                    StatName = "jumps"
                };
                StatsManager.Instance.GetLeaderboard(this.user, query);
            }
        }

        private void socialLeaderboardButton_Click(object sender, RoutedEventArgs e)
        {
            if (this.user.IsSignedIn)
            {
                LeaderboardQuery query = new LeaderboardQuery
                {
                    MaxItems = 3,
                    SocialGroup = "all",
                    StatName = "headshots"
                };
                StatsManager.Instance.GetLeaderboard(this.user, query);
            }
        }

        private void WriteGlobalStats_Click(object sender, RoutedEventArgs e)
        {
            if (!this.user.IsSignedIn) return;
            StatsManager.Instance.SetStatAsInteger(this.user, "jumps", ++this.jumps);
        }

        private void WriteSocialStats_Click(object sender, RoutedEventArgs e)
        {
            if (!this.user.IsSignedIn) return;
            StatsManager.Instance.SetStatAsInteger(this.user, "headshots", ++this.headshots);
        }

        private void NextLb_Click(object sender, RoutedEventArgs e)
        {
            if (!this.user.IsSignedIn) return;

            if (this.LeaderboardResult.HasNext)
            {
                StatsManager.Instance.GetLeaderboard(this.user, this.LeaderboardResult.NextQuery);
            }
        }

        async void DoWork()
        {
            while (true)
            {
                if (this.user.IsSignedIn)
                {
                    // Perform the long running do work task on a background thread.
                    var doWorkTask = Task.Run(() => { return StatsManager.Instance.DoWork(); });

                    List<StatEvent> events = await doWorkTask;
                    foreach (StatEvent ev in events)
                    {
                        if (ev.EventType == StatEventType.GetLeaderboardComplete)
                        {
                            LeaderboardResult result = ((LeaderboardResultEventArgs)ev.EventArgs).Result;
                            this.LeaderboardResult = result;

                            NextLbBtn.IsEnabled = result.HasNext;
                        }
                    }

                    var statNames = StatsManager.Instance.GetStatNames(this.user);
                    if (statNames.Count > 0)
                    {
                        foreach (var stat in statNames)
                        {
                            if (string.Equals(stat, "headshots"))
                            {
                                this.headshots = StatsManager.Instance.GetStat(this.user, "headshots").AsInteger();
                            }
                            else if (string.Equals(stat, "jumps"))
                            {
                                this.jumps = StatsManager.Instance.GetStat(this.user, "jumps").AsInteger();
                            }
                        }
                        this.StatsData.Text = string.Join(Environment.NewLine, statNames.Select(n => StatsManager.Instance.GetStat(this.user, n)).Select(s => $"{s.Name} ({s.Type}) = {s.Value}"));
                    }
                }

                // don't run again for at least 200 milliseconds
                await Task.Delay(200);
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}