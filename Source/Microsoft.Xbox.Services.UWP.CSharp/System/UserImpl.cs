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
        private static bool? isMultiUserSupported;
        private static CoreDispatcher dispatcher;

        private WebAccountProvider provider;
        private readonly object userImplLock = new object();
        private static UserWatcher userWatcher;
        private static ConcurrentDictionary<string, UserImpl> trackingUsers = new ConcurrentDictionary<string, UserImpl>();

        public bool IsSignedIn { get; private set; }
        public string XboxUserId { get; private set; }
        public string Gamertag { get; private set; }
        public string AgeGroup { get; private set; }
        public string Privileges { get; private set; }
        public string WebAccountId { get; private set; }
        public AuthConfig AuthConfig { get; private set; }
        public User CreationContext { get; private set; }
        internal WeakReference<IXboxLiveUser> UserWeakRef{ get; private set; }

        public static CoreDispatcher Dispatcher
        {
            get
            {
                return dispatcher ?? (dispatcher = Windows.ApplicationModel.Core.CoreApplication.MainView.Dispatcher);
            }
        }

        private readonly EventHandler<SignInCompletedEventArgs> signInCompleted;
        private readonly EventHandler<SignOutCompletedEventArgs> signOutCompleted;
        private ThreadPoolTimer threadPoolTimer;

        public UserImpl(EventHandler<SignInCompletedEventArgs> signInCompleted, EventHandler<SignOutCompletedEventArgs> signOutCompleted, User systemUser, XboxLiveUser xboxLiveuser)
        {
            this.signInCompleted = signInCompleted;
            this.signOutCompleted = signOutCompleted;
            this.CreationContext = systemUser;
            this.UserWeakRef = new WeakReference<IXboxLiveUser>(xboxLiveuser);

            var appConfig = XboxLiveAppConfiguration.Instance;
            this.AuthConfig = new AuthConfig
            {
                Sandbox = appConfig.Sandbox,
                EnvrionmentPrefix = appConfig.EnvironmentPrefix,
                Envrionment = appConfig.Environment,
                UseCompactTicket = appConfig.UseFirstPartyToken
            };
        }

        public Task<SignInResult> SignInImpl(bool showUI, bool forceRefresh)
        {
            //Initiate user watcher
            if (this.IsMultiUserApplication())
            {
                if (userWatcher == null)
                {
                    userWatcher = Windows.System.User.CreateWatcher();
                    userWatcher.Removed += UserWatcher_UserRemoved;
                }
            }

            var signInTask = this.InitializeProvider().ContinueWith((task) =>
            {
                var tokenAndSigResult = this.InternalGetTokenAndSignatureHelper(
                    "GET", this.AuthConfig.XboxLiveEndpoint,
                    "",
                    null,
                    showUI,
                    false
                );

                if (tokenAndSigResult != null && tokenAndSigResult.XboxUserId != null && tokenAndSigResult.XboxUserId.Length != 0)
                {
                    if (string.IsNullOrEmpty(tokenAndSigResult.Token))
                    {
                        var xboxUserId = tokenAndSigResult.XboxUserId;
                        // todo: set presence
                    }

                    this.UserSignedIn(tokenAndSigResult.XboxUserId, tokenAndSigResult.Gamertag, tokenAndSigResult.AgeGroup,
                        tokenAndSigResult.Privileges, tokenAndSigResult.WebAccountId);

                    return new SignInResult(SignInStatus.Success);
                }

                return this.ConvertWebTokenRequestStatus(tokenAndSigResult.TokenRequestResult);
            });

            return signInTask;
        }

        private void UserWatcher_UserRemoved(UserWatcher sender, UserChangedEventArgs args)
        {
            UserImpl signoutUser;
            if (UserImpl.trackingUsers.TryGetValue(args.User.NonRoamableId, out signoutUser))
            {
                signoutUser.UserSignedOut();
            }
        }

        private Task InitializeProvider()
        {
            if (this.provider != null)
            {
                return Task.FromResult<object>(null);
            }

            TaskCompletionSource<object> taskCompletion = new TaskCompletionSource<object>();

            // First time initialization. 
            if (this.CreationContext == null && this.IsMultiUserApplication())
            {
                taskCompletion.SetException(new Exception("Xbox Live User object is required to be constructed by a Windows.System.User object in the Multi-User environment."));
            }


            if (!Dispatcher.HasThreadAccess)
            {
                // We're not on the UI thread, so we'll use the dispatcher to make our call.
                IAsyncAction uiTask = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () => this.InitializeProvider(taskCompletion));
            }
            else
            {
                // Otherwise just go ahead and make the call on this thread.
                this.InitializeProvider(taskCompletion);
            }

            return taskCompletion.Task;
        }

        private void InitializeProvider(TaskCompletionSource<object> completionSource)
        {
            IAsyncOperation<WebAccountProvider> providerTask;

            if (this.CreationContext == null)
            {
                providerTask = WebAuthenticationCoreManager.FindAccountProviderAsync("https://xsts.auth.xboxlive.com");
            }
            else
            {
                providerTask = WebAuthenticationCoreManager.FindAccountProviderAsync("https://xsts.auth.xboxlive.com", string.Empty, this.CreationContext);
            }

            providerTask.Completed = (webaccountProviderResult, state) => { this.FindAccountCompleted(webaccountProviderResult, state, completionSource); };
        }

        private void FindAccountCompleted(IAsyncOperation<WebAccountProvider> asyncInfo, AsyncStatus asyncStatus, TaskCompletionSource<object> completionSource)
        {
            this.provider = asyncInfo.GetResults();
            if (this.provider == null)
            {
                completionSource.SetException(new Exception("XBL IDP is not found")); // todo: make xbox live exception
            }

            completionSource.SetResult(null);
        }

        private bool IsMultiUserApplication()
        {
            if (UserImpl.isMultiUserSupported == null)
            {
                try
                {
                    bool APIExist = Windows.Foundation.Metadata.ApiInformation.IsMethodPresent("Windows.System.UserPicker", "IsSupported");
                    UserImpl.isMultiUserSupported = (APIExist && UserPicker.IsSupported()) ? true : false;
                }
                catch (Exception)
                {
                    UserImpl.isMultiUserSupported = false;
                }
            }
            return UserImpl.isMultiUserSupported == true;
        }

        public Task<TokenAndSignatureResult> InternalGetTokenAndSignatureAsync(string httpMethod, string url, string headers, byte[] body, bool promptForCredentialsIfNeeded, bool forceRefresh)
        {
            return Task.Factory.StartNew(() =>
            {
                var result = this.InternalGetTokenAndSignatureHelper(httpMethod, url, headers, body, promptForCredentialsIfNeeded, forceRefresh);
                if (result.TokenRequestResult != null && result.TokenRequestResult.ResponseStatus == WebTokenRequestStatus.UserInteractionRequired)
                {
                    if (this.AuthConfig.XboxLiveEndpoint != null && url == this.AuthConfig.XboxLiveEndpoint && this.IsSignedIn)
                    {
                        this.UserSignedOut();
                    }
                    else if (url != this.AuthConfig.XboxLiveEndpoint)
                    {
                        // todo: throw error
                    }
                }

                return result;
            });
        }

        private TokenAndSignatureResult InternalGetTokenAndSignatureHelper(string httpMethod, string url, string headers, byte[] body, bool promptForCredentialsIfNeeded, bool forceRefresh)
        {
            if (this.provider == null)
            {
                throw new Exception("Xbox Live identity provider is not initialized");
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
            var tokenResult = this.RequestTokenFromIDP(Dispatcher, promptForCredentialsIfNeeded, request);
            try
            {
                tokenAndSignatureReturnResult = this.ConvertWebTokenRequestResult(tokenResult);
                if (tokenAndSignatureReturnResult != null && this.IsSignedIn && tokenAndSignatureReturnResult.XboxUserId != this.XboxUserId)
                {
                    this.UserSignedOut();
                    throw new Exception("User has switched"); // todo: auth_user_switched
                }
            }
            catch (Exception)
            {
                // log
            }

            return tokenAndSignatureReturnResult;
        }

        private WebTokenRequestResult RequestTokenFromIDP(CoreDispatcher coreDispatcher, bool promptForCredentialsIfNeeded, WebTokenRequest request)
        {
            WebTokenRequestResult tokenResult = null;
            if (coreDispatcher != null && promptForCredentialsIfNeeded)
            {
                TaskCompletionSource<object> completionSource = new TaskCompletionSource<object>();
                var requestTokenTask = coreDispatcher.RunAsync(CoreDispatcherPriority.Normal,
                    () =>
                    {
                        WebAuthenticationCoreManager.RequestTokenAsync(request).Completed = (info, status) =>
                        {
                            try
                            {
                                tokenResult = info.GetResults();
                                completionSource.SetResult(null);
                            }
                            catch (Exception e)
                            {
                                completionSource.SetException(e);
                            }
                        };
                    });

                completionSource.Task.Wait();
                if (completionSource.Task.Exception != null)
                {
                    throw completionSource.Task.Exception;
                }
            }
            else
            {
                IAsyncOperation<WebTokenRequestResult> getTokenTask;
                TaskCompletionSource<WebTokenRequestResult> webTokenRequestSource = new TaskCompletionSource<WebTokenRequestResult>();
                if (promptForCredentialsIfNeeded)
                {
                    getTokenTask = WebAuthenticationCoreManager.RequestTokenAsync(request);
                }
                else
                {
                    getTokenTask = WebAuthenticationCoreManager.GetTokenSilentlyAsync(request);
                }

                getTokenTask.Completed += (tokenTask, status) => webTokenRequestSource.SetResult(tokenTask.GetResults());

                tokenResult = webTokenRequestSource.Task.Result;
            }

            return tokenResult;
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
                this.UserSignedOut(); // todo: throw?
            }
            else if (tokenResponseStatus == WebTokenRequestStatus.ProviderError)
            {
                // todo: log error
            }

            return new TokenAndSignatureResult()
            {
                TokenRequestResult = tokenResult
            };
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
                if (this.signInCompleted != null)
                {
                    this.signInCompleted(null, new SignInCompletedEventArgs(xboxUserId));
                }
            }

            // We use user watcher for MUA, if it's SUA we use own checker for sign out event.
            if (!this.IsMultiUserApplication())
            {
                TimeSpan delay = new TimeSpan(0, 0, 10);
                this.threadPoolTimer = ThreadPoolTimer.CreatePeriodicTimer(new TimerElapsedHandler((source) => { this.CheckUserSignedOut(); }),
                    delay
                );
            }
            else
            {
                if (this.CreationContext != null)
                {
                    UserImpl.trackingUsers.TryAdd(this.CreationContext.NonRoamableId, this);
                }
            }
        }

        private void UserSignedOut()
        {
            bool isSignedIn = false;
            lock (this.userImplLock)
            {
                isSignedIn = this.IsSignedIn;
                this.IsSignedIn = false;
            }

            if (isSignedIn)
            {
                if (this.signOutCompleted != null)
                {
                    this.signOutCompleted(this, new SignOutCompletedEventArgs(this.UserWeakRef));
                }
            }

            lock (this.userImplLock)
            {
                // Check again on isSignedIn flag, in case users signed in again in signOutHandlers callback,
                // so we don't clean up the properties. 
                if (!isSignedIn)
                {
                    this.XboxUserId = null;
                    this.Gamertag = null;
                    this.AgeGroup = null;
                    this.Privileges = null;
                    this.WebAccountId = null;

                    if (this.CreationContext != null)
                    {
                        UserImpl outResult;
                        UserImpl.trackingUsers.TryRemove(this.CreationContext.NonRoamableId, out outResult);
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
            try
            {
                if (this.IsSignedIn)
                {
                    var signedInAccount = WebAuthenticationCoreManager.FindAccountAsync(this.provider, this.WebAccountId);
                    if (signedInAccount == null)
                    {
                        this.UserSignedOut();
                    }
                }
            }
            catch (Exception)
            {
                this.UserSignedOut();
            }
        }

        private SignInResult ConvertWebTokenRequestStatus(WebTokenRequestResult tokenResult)
        {
            if (tokenResult.ResponseStatus == WebTokenRequestStatus.UserCancel)
            {
                return new SignInResult(SignInStatus.UserCancel);
            }
            else
            {
                return new SignInResult(SignInStatus.UserInteractionRequired);
            }
        }
    }
}