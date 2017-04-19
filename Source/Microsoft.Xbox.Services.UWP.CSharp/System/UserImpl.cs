// Copyright (c) Microsoft Corporation
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Microsoft.Xbox.Services.System
{
    using Windows.Foundation;
    using Windows.Security.Authentication.Web.Core;
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

        private readonly object userImplLock = new object();
        private static UserWatcher userWatcher;
        private static ConcurrentDictionary<string, UserImpl> trackingUsers = new ConcurrentDictionary<string, UserImpl>();

        internal AccountProvider Provider { get; set; } = new AccountProvider();

        public bool IsSignedIn { get; private set; }
        public string XboxUserId { get; private set; }
        public string Gamertag { get; private set; }
        public string AgeGroup { get; private set; }
        public string Privileges { get; private set; }
        public string WebAccountId { get; private set; }
        public AuthConfig AuthConfig { get; private set; }
        public User CreationContext { get; private set; }
        internal WeakReference UserWeakReference { get; private set; }

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
            if (systemUser == null && IsMultiUserApplication())
            {
                throw(new XboxException("Xbox Live User object is required to be constructed by a Windows.System.User object for a multi-user application."));
            }

            //Initiate user watcher
            if (IsMultiUserApplication())
            {
                if (userWatcher == null)
                {
                    userWatcher = Windows.System.User.CreateWatcher();
                    userWatcher.Removed += UserWatcher_UserRemoved;
                }
            }

            this.signInCompleted = signInCompleted;
            this.signOutCompleted = signOutCompleted;
            this.CreationContext = systemUser;
            this.UserWeakReference = new WeakReference(xboxLiveuser);

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
            
            var signInTask = Task.Run( async () => 
            {
                await this.Provider.InitializeProvider(this.CreationContext);

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

                return this.ConvertWebTokenRequestStatus(tokenAndSigResult.TokenRequestResultStatus);
            });

            return signInTask;
        }

        static private void UserWatcher_UserRemoved(UserWatcher sender, UserChangedEventArgs args)
        {
            UserImpl signoutUser;
            if (UserImpl.trackingUsers.TryGetValue(args.User.NonRoamableId, out signoutUser))
            {
                signoutUser.UserSignedOut();
            }
        }

        static private bool IsMultiUserApplication()
        {
            if (isMultiUserSupported == null)
            {
                try
                {
                    bool apiExist = Windows.Foundation.Metadata.ApiInformation.IsMethodPresent("Windows.System.UserPicker", "IsSupported");
                    isMultiUserSupported = (apiExist && UserPicker.IsSupported());
                }
                catch (Exception)
                {
                    isMultiUserSupported = false;
                }
            }
            return isMultiUserSupported == true;
        }

        public Task<TokenAndSignatureResult> InternalGetTokenAndSignatureAsync(string httpMethod, string url, string headers, byte[] body, bool promptForCredentialsIfNeeded, bool forceRefresh)
        {
            return Task.Factory.StartNew(() =>
            {
                var result = this.InternalGetTokenAndSignatureHelper(httpMethod, url, headers, body, promptForCredentialsIfNeeded, forceRefresh);
                if (result.TokenRequestResultStatus == WebTokenRequestStatus.UserInteractionRequired)
                {
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
                }

                return result;
            });
        }

        private TokenAndSignatureResult InternalGetTokenAndSignatureHelper(string httpMethod, string url, string headers, byte[] body, bool promptForCredentialsIfNeeded, bool forceRefresh)
        {
            var request = this.Provider.CreateWebTokenRequest();
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
            tokenAndSignatureReturnResult = this.ConvertWebTokenRequestResult(tokenResult);
            if (tokenAndSignatureReturnResult != null && this.IsSignedIn && tokenAndSignatureReturnResult.XboxUserId != this.XboxUserId)
            {
                this.UserSignedOut();
                throw new XboxException("User has switched"); // todo: auth_user_switched
            }

            return tokenAndSignatureReturnResult;
        }

        private TokenRequestResult RequestTokenFromIDP(CoreDispatcher coreDispatcher, bool promptForCredentialsIfNeeded, WebTokenRequest request)
        {
            TokenRequestResult tokenResult = null;
            if (coreDispatcher != null && promptForCredentialsIfNeeded)
            {
                TaskCompletionSource<object> completionSource = new TaskCompletionSource<object>();
                var requestTokenTask = coreDispatcher.RunAsync(CoreDispatcherPriority.Normal,
                    () =>
                    {
                        this.Provider.RequestTokenAsync(request).ContinueWith( (task) =>
                        {
                            try
                            {
                                tokenResult = task.Result;
                                completionSource.SetResult(null);
                            }
                            catch (Exception e)
                            {
                                completionSource.SetException(e);
                            }
                        });
                    });

                completionSource.Task.Wait();
                if (completionSource.Task.Exception != null)
                {
                    throw completionSource.Task.Exception;
                }
            }
            else
            {
                Task<TokenRequestResult> getTokenTask;
                TaskCompletionSource<TokenRequestResult> webTokenRequestSource = new TaskCompletionSource<TokenRequestResult>();
                if (promptForCredentialsIfNeeded)
                {
                    getTokenTask = this.Provider.RequestTokenAsync(request);
                }
                else
                {
                    getTokenTask = this.Provider.GetTokenSilentlyAsync(request);
                }

                getTokenTask.ContinueWith((tokenTask) => webTokenRequestSource.SetResult(tokenTask.Result));

                webTokenRequestSource.Task.Wait();
                if (webTokenRequestSource.Task.Exception != null)
                {
                    throw webTokenRequestSource.Task.Exception;
                }
                tokenResult = webTokenRequestSource.Task.Result;
            }

            return tokenResult;
        }

        private TokenAndSignatureResult ConvertWebTokenRequestResult(TokenRequestResult tokenResult)
        {
            var tokenResponseStatus = tokenResult.ResponseStatus;

            if (tokenResponseStatus == WebTokenRequestStatus.Success)
            {

                string xboxUserId = tokenResult.Properties["XboxUserId"];
                string gamertag = tokenResult.Properties["Gamertag"];
                string ageGroup = tokenResult.Properties["AgeGroup"];
                string environment = tokenResult.Properties["Environment"];
                string sandbox = tokenResult.Properties["Sandbox"];
                string webAccountId = tokenResult.WebAccountId;
                string token = tokenResult.Token;

                string signature = null;
                if (tokenResult.Properties.ContainsKey("Signature"))
                {
                    signature = tokenResult.Properties["Signature"];
                }

                string privilege = null;
                if (tokenResult.Properties.ContainsKey("Privileges"))
                {
                    privilege = tokenResult.Properties["Privileges"];
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
                    TokenRequestResultStatus = tokenResult.ResponseStatus
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
                    TokenRequestResultStatus = tokenResult.ResponseStatus
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
                if (this.signInCompleted != null)
                {
                    this.signInCompleted(null, new SignInCompletedEventArgs(this.UserWeakReference));
                }
            }

            // We use user watcher for MUA, if it's SUA we use own checker for sign out event.
            if (!IsMultiUserApplication())
            {
                TimeSpan delay = new TimeSpan(0, 0, 10);
                this.threadPoolTimer = ThreadPoolTimer.CreatePeriodicTimer(new TimerElapsedHandler((source) => { this.CheckUserSignedOut(); }),
                    delay
                );
            }
            else
            {
                UserImpl.trackingUsers.TryAdd(this.CreationContext.NonRoamableId, this);
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
                    this.signOutCompleted(this, new SignOutCompletedEventArgs(this.UserWeakReference));
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
                    var signedInAccount = this.Provider.FindAccountAsync(this.WebAccountId);
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

        private SignInResult ConvertWebTokenRequestStatus(WebTokenRequestStatus tokenResultStatus)
        {
            if (tokenResultStatus == WebTokenRequestStatus.UserCancel)
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