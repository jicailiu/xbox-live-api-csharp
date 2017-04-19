// Copyright (c) Microsoft Corporation
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Microsoft.Xbox.Services.System
{
    using Windows.Foundation;
    using Windows.Security.Authentication.Web.Core;
    using Windows.Security.Credentials;
    using Windows.System;
    using Windows.System.Threading;
    using Windows.UI.Core;

    using global::System;
    using global::System.Linq;
    using global::System.Text;
    using global::System.Threading.Tasks;
    using global::System.Collections.Concurrent;

    internal class UserImpl : IUserImpl
    {
        public event EventHandler SignInCompleted;
        public event EventHandler SignOutCompleted;

        private static bool? isMultiUserApplication;
        private static CoreDispatcher dispatcher;
        private static UserWatcher userWatcher;
        private static readonly ConcurrentDictionary<string, UserImpl> trackingUsers = new ConcurrentDictionary<string, UserImpl>();

        private readonly object userImplLock = new object();
        private WebAccountProvider provider;

        public bool IsSignedIn { get; private set; }
        public string XboxUserId { get; private set; }
        public string Gamertag { get; private set; }
        public string AgeGroup { get; private set; }
        public string Privileges { get; private set; }
        public string WebAccountId { get; private set; }
        public AuthConfig AuthConfig { get; private set; }
        public User CreationContext { get; private set; }

        public static CoreDispatcher Dispatcher
        {
            get
            {
                return dispatcher ?? (dispatcher = Windows.ApplicationModel.Core.CoreApplication.MainView.Dispatcher);
            }
        }

        private ThreadPoolTimer threadPoolTimer;

        public UserImpl(User systemUser)
        {
            if (IsMultiUserApplication)
            {
                if (systemUser == null)
                {
                    throw new XboxException("Xbox Live User object is required to be constructed by a Windows.System.User object for a multi-user application.");
                }

                //Initiate user watcher
                if (userWatcher == null)
                {
                    userWatcher = User.CreateWatcher();
                    userWatcher.Removed += UserWatcher_UserRemoved;
                }
            }

            this.CreationContext = systemUser;

            // TODO: This config is broken.
            var appConfig = XboxLiveAppConfiguration.Instance;
            this.AuthConfig = new AuthConfig
            {
                Sandbox = appConfig.Sandbox,
                EnvrionmentPrefix = appConfig.EnvironmentPrefix,
                Envrionment = appConfig.Environment,
                UseCompactTicket = appConfig.UseFirstPartyToken
            };
        }

        public async Task<SignInResult> SignInImpl(bool showUI, bool forceRefresh)
        {
            await this.InitializeProviderAsync();

            TokenAndSignatureResult result = await this.InternalGetTokenAndSignatureHelperAsync("GET", this.AuthConfig.XboxLiveEndpoint, "", null, showUI, false);
            SignInStatus status = ConvertWebTokenRequestStatus(result.TokenRequestResult.ResponseStatus);

            if (status != SignInStatus.Success)
            {
                return new SignInResult(status);
            }

            if (string.IsNullOrEmpty(result.Token))
            {
                // todo: set presence
            }

            this.UserSignedIn(result.XboxUserId, result.Gamertag, result.AgeGroup, result.Privileges, result.WebAccountId);

            return new SignInResult(status);
        }

        private static void UserWatcher_UserRemoved(UserWatcher sender, UserChangedEventArgs args)
        {
            UserImpl signoutUser;
            if (trackingUsers.TryGetValue(args.User.NonRoamableId, out signoutUser))
            {
                signoutUser.UserSignedOut();
            }
        }

        private async Task InitializeProviderAsync()
        {
            if (this.provider != null)
            {
                return;
            }

            if (!Dispatcher.HasThreadAccess)
            {
                // There's no way to wait for a dispatcher call to finish if it's async, so we need to use a TaskCompletionSource.
                TaskCompletionSource<WebAccountProvider> findProviderCompletionSource = new TaskCompletionSource<WebAccountProvider>();

                // We're not on the UI thread, so we'll use the dispatcher to make our call.
                await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, async () => { findProviderCompletionSource.SetResult(await this.FindProvider()); });

                this.provider = await findProviderCompletionSource.Task;
            }
            else
            {
                // Otherwise just go ahead and make the call on this thread.
                this.provider = await this.FindProvider();
            }
        }

        private async Task<WebAccountProvider> FindProvider()
        {
            WebAccountProvider accountProvider;

            if (this.CreationContext == null)
            {
                accountProvider = await WebAuthenticationCoreManager.FindAccountProviderAsync("https://xsts.auth.xboxlive.com");
            }
            else
            {
                accountProvider = await WebAuthenticationCoreManager.FindAccountProviderAsync("https://xsts.auth.xboxlive.com", string.Empty, this.CreationContext);
            }

            if (accountProvider == null)
            {
                throw new XboxException("Unable to find Xbox Live Identity Provider");
            }

            return accountProvider;
        }

        private static bool IsMultiUserApplication
        {
            get
            {
                if (isMultiUserApplication == null)
                {
                    try
                    {
                        bool apiExist = Windows.Foundation.Metadata.ApiInformation.IsMethodPresent("Windows.System.UserPicker", "IsSupported");
                        isMultiUserApplication = (apiExist && UserPicker.IsSupported());
                    }
                    catch (Exception)
                    {
                        isMultiUserApplication = false;
                    }
                }
                return isMultiUserApplication == true;
            }
        }

        public async Task<TokenAndSignatureResult> InternalGetTokenAndSignatureAsync(string httpMethod, string url, string headers, byte[] body, bool promptForCredentialsIfNeeded, bool forceRefresh)
        {
            var result = await this.InternalGetTokenAndSignatureHelperAsync(httpMethod, url, headers, body, promptForCredentialsIfNeeded, forceRefresh);
            if (result.TokenRequestResult == null || result.TokenRequestResult.ResponseStatus != WebTokenRequestStatus.UserInteractionRequired)
            {
                return result;
            }

            // Failed to get 'xboxlive.com' token, sign out if already sign in (SPOP or user banned).
            // But for sign in path, it's expected.
			if (this.AuthConfig.XboxLiveEndpoint != null && url == this.AuthConfig.XboxLiveEndpoint && this.IsSignedIn)
            {
                this.UserSignedOut();
            }
            else if (url != this.AuthConfig.XboxLiveEndpoint)
            {
                // If it's not asking for xboxlive.com's token, we treat UserInteractionRequired as an error
                string errorMsg = "Failed to get token for endpoint: " + url;
                throw new XboxException(errorMsg);
            }

            return result;
        }

        private async Task<TokenAndSignatureResult> InternalGetTokenAndSignatureHelperAsync(string httpMethod, string url, string headers, byte[] body, bool promptForCredentialsIfNeeded, bool forceRefresh)
        {
            if (this.provider == null)
            {
                throw new XboxException("Xbox Live identity provider is not initialized");
            }

            var request = new WebTokenRequest(this.provider);
            request.Properties.Add("HttpMethod", httpMethod);
            request.Properties.Add("Url", url);
            if (!string.IsNullOrEmpty(headers))
            {
                request.Properties.Add("RequestHeaders", headers);
            }
            if (forceRefresh)
            {
                request.Properties.Add("ForceRefresh", "true");
            }

            if (body != null && body.Length > 0)
            {
                request.Properties.Add("RequestBody", Encoding.UTF8.GetString(body));
            }

            request.Properties.Add("Target", this.AuthConfig.RPSTicketService);
            request.Properties.Add("Policy", this.AuthConfig.RPSTicketPolicy);
            if (promptForCredentialsIfNeeded)
            {
                string pfn = Windows.ApplicationModel.Package.Current.Id.FamilyName;
                request.Properties.Add("PackageFamilyName", pfn);
            }

            TokenAndSignatureResult tokenAndSignatureReturnResult = null;
            var tokenResult = await RequestTokenFromIdpAsync(promptForCredentialsIfNeeded, request);
            tokenAndSignatureReturnResult = this.ConvertWebTokenRequestResult(tokenResult);
            if (tokenAndSignatureReturnResult != null && this.IsSignedIn && tokenAndSignatureReturnResult.XboxUserId != this.XboxUserId)
            {
                this.UserSignedOut();
                throw new XboxException("User has switched");
            }

            return tokenAndSignatureReturnResult;
        }

        private static Task<WebTokenRequestResult> RequestTokenFromIdpAsync(bool promptForCredentialsIfNeeded, WebTokenRequest request)
        {
            if (!promptForCredentialsIfNeeded)
            {
                return WebAuthenticationCoreManager.GetTokenSilentlyAsync(request).AsTask();
            }

            TaskCompletionSource<WebTokenRequestResult> webTokenRequestSource = new TaskCompletionSource<WebTokenRequestResult>();
            IAsyncAction requestTokenTask = Dispatcher.RunAsync(CoreDispatcherPriority.Normal,
                async () =>
                {
                    try
                    {
                        WebTokenRequestResult result = await WebAuthenticationCoreManager.RequestTokenAsync(request);
                        webTokenRequestSource.SetResult(result);
                    }
                    catch (Exception e)
                    {
                        webTokenRequestSource.SetException(e);
                    }
                });

                webTokenRequestSource.Task.Wait();
                if (webTokenRequestSource.Task.Exception != null)
                {
                    throw webTokenRequestSource.Task.Exception;
                }
            return webTokenRequestSource.Task;
        }

        private TokenAndSignatureResult ConvertWebTokenRequestResult(WebTokenRequestResult tokenResult)
        {
            var tokenResponseStatus = tokenResult.ResponseStatus;

            if (tokenResponseStatus == WebTokenRequestStatus.Success)
            {
                if (tokenResult.ResponseData == null || tokenResult.ResponseData.Count == 0)
                {
                    throw new Exception("Invalid idp token response");
                }

                WebTokenResponse response = tokenResult.ResponseData.ElementAt(0);

                string xboxUserId = response.Properties["XboxUserId"];
                string gamertag = response.Properties["Gamertag"];
                string ageGroup = response.Properties["AgeGroup"];
                string environment = response.Properties["Environment"];
                string sandbox = response.Properties["Sandbox"];
                string webAccountId = response.WebAccount.Id;
                string token = response.Token;

                string signature = null;
                if (response.Properties.ContainsKey("Signature"))
                {
                    signature = response.Properties["Signature"];
                }

                string privilege = null;
                if (response.Properties.ContainsKey("Privileges"))
                {
                    privilege = response.Properties["Privileges"];
                }

                if (environment.ToLower() == "prod")
                {
                    environment = null;
                }

                var appConfig = XboxLiveAppConfiguration.Instance;
                appConfig.Sandbox = sandbox;
                appConfig.Environment = environment;

                return new TokenAndSignatureResult
                {
                    WebAccountId = webAccountId,
                    Privileges = privilege,
                    AgeGroup = ageGroup,
                    Gamertag = gamertag,
                    XboxUserId = xboxUserId,
                    Signature = signature,
                    Token = token,
                    TokenRequestResult = tokenResult
                };
            }
            else if (tokenResponseStatus == WebTokenRequestStatus.AccountSwitch)
            {
                this.UserSignedOut();
                throw new XboxException("User has switched");
            }
            else if (tokenResponseStatus == WebTokenRequestStatus.ProviderError)
            {
                string errorMsg = "Provider error: " + tokenResult.ResponseError.ErrorMessage  + ", Error Code: " + tokenResult.ResponseError.ErrorCode.ToString("X");
                throw new XboxException(errorMsg);
            }
            else
            {
                return new TokenAndSignatureResult()
                {
                    TokenRequestResult = tokenResult
                };
            }

        }

        private void UserSignedIn(string xboxUserId, string gamertag, string ageGroup, string privileges, string webAccountId)
        {
            lock (this.userImplLock)
            {
                this.XboxUserId = xboxUserId;
                this.Gamertag = gamertag;
                this.AgeGroup = ageGroup;
                this.Privileges = privileges;
                this.WebAccountId = webAccountId;

                this.IsSignedIn = true;
            }

            this.OnSignInCompleted();

            // We use user watcher for MUA, if it's SUA we use own checker for sign out event.
            if (!IsMultiUserApplication)
            {
                this.threadPoolTimer = ThreadPoolTimer.CreatePeriodicTimer(
                    source => { this.CheckUserSignedOut(); },
                    TimeSpan.FromSeconds(10)
                );
            }
            else
            {
                trackingUsers.TryAdd(this.CreationContext.NonRoamableId, this);
            }
        }

        private void UserSignedOut()
        {
            if (!this.IsSignedIn)
            {
                return;
            }

            lock (this.userImplLock)
            {
                this.IsSignedIn = false;
            }

            this.OnSignOutCompleted();

            lock (this.userImplLock)
            {
                // Check again on isSignedIn flag, in case users signed in again in signOutHandlers callback,
                // so we don't clean up the properties. 
                if (!this.IsSignedIn)
                {
                    this.XboxUserId = null;
                    this.Gamertag = null;
                    this.AgeGroup = null;
                    this.Privileges = null;
                    this.WebAccountId = null;

                    if (this.CreationContext != null)
                    {
                        UserImpl outResult;
                        trackingUsers.TryRemove(this.CreationContext.NonRoamableId, out outResult);
                    }

                    if (this.threadPoolTimer != null)
                    {
                        this.threadPoolTimer.Cancel();
                    }
                }
            }
        }

        private void CheckUserSignedOut()
        {
            if (!this.IsSignedIn) return;

            try
            {
                var signedInAccount = WebAuthenticationCoreManager.FindAccountAsync(this.provider, this.WebAccountId);
                if (signedInAccount == null)
                {
                    this.UserSignedOut();
                }
            }
            catch (Exception)
            {
                this.UserSignedOut();
            }
        }

        private static SignInStatus ConvertWebTokenRequestStatus(WebTokenRequestStatus status)
        {
            switch (status)
            {
                case WebTokenRequestStatus.Success:
                    return SignInStatus.Success;
                case WebTokenRequestStatus.UserCancel:
                    return SignInStatus.UserCancel;
                case WebTokenRequestStatus.UserInteractionRequired:
                    return SignInStatus.UserInteractionRequired;
                case WebTokenRequestStatus.AccountSwitch:
                case WebTokenRequestStatus.AccountProviderNotAvailable:
                case WebTokenRequestStatus.ProviderError:
                    return SignInStatus.ProviderError;
                default:
                    throw new ArgumentOutOfRangeException("status");
            }
        }

        protected virtual void OnSignInCompleted()
        {
            var onSignInCompleted = this.SignInCompleted;
            if (onSignInCompleted != null)
            {
                onSignInCompleted(this, new EventArgs());
            }
        }

        protected virtual void OnSignOutCompleted()
        {
            var onSignOutCompleted = this.SignOutCompleted;
            if (onSignOutCompleted != null)
            {
                onSignOutCompleted(this, new EventArgs());
            }
        }
    }
}