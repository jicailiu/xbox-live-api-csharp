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

    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainPage : Page, INotifyPropertyChanged
    {
        private int jumps;
        private LeaderboardResult leaderboard;
        private readonly XboxLiveUser user;

        public MainPage()
        {
            this.InitializeComponent();
            this.user = new XboxLiveUser();
        }

        public LeaderboardResult Leaderboard
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

        private async void button_Click(object sender, RoutedEventArgs e)
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

        private void WriteStats_Click(object sender, RoutedEventArgs e)
        {
            if (!this.user.IsSignedIn) return;

            StatsManager.Instance.SetStatAsInteger(this.user, "headshots", this.jumps++);
        }

        private void ReadStats_Click(object sender, RoutedEventArgs e)
        {
            if (!this.user.IsSignedIn) return;

            var statNames = StatsManager.Instance.GetStatNames(this.user);
            this.StatsData.Text = string.Join(Environment.NewLine, statNames.Select(n => StatsManager.Instance.GetStat(this.user, n)).Select(s => $"{s.Name} ({s.Type}) = {s.Value}"));
        }

        private void StatsDoWork_Click(object sender, RoutedEventArgs e)
        {
            if (!this.user.IsSignedIn) return;

            List<StatEvent> events = StatsManager.Instance.DoWork();
            foreach (StatEvent ev in events)
            {
                if (ev.EventType == StatEventType.GetLeaderboardComplete)
                {
                    LeaderboardResult result = ((LeaderboardResultEventArgs)ev.EventArgs).Result;
                    this.Leaderboard = result;

                    if (result.HasNext)
                    {
                        StatsManager.Instance.GetLeaderboard(ev.LocalUser, result.NextQuery);
                    }
                }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}