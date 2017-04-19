// Copyright (c) Microsoft Corporation
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Microsoft.Xbox.Services.UWP.UnitTests
{
    using global::System;
    using Windows.System;
    using Microsoft.VisualStudio.TestPlatform.UnitTestFramework;
    using Microsoft.Xbox.Services.System;
    using global::System.Linq;
    using Moq;
    using global::System.Threading.Tasks;
    using UITestMethod = Microsoft.VisualStudio.TestPlatform.UnitTestFramework.AppContainer.UITestMethodAttribute;
    using Windows.Security.Authentication.Web.Core;
    using global::System.Collections.Generic;

    [TestClass]
    public class UserTest
    {
        private const string mockXuid = "123456";
        private const string mockGamerTag = "mock gamertag";
        private const string mockAgeGroup = "Adult";
        private const string mockEnvironment = "prod";
        private const string mockSandbox = "ASDFAS.1";
        private const string mockSignature = "mock signature";
        private const string mockPrivileges = "12 34 56 78 9";
        private const string mockWebAccountId = "mock web account id";
        private const string mockToken = "mock token";
        private const int mockErrorcode = 9999;
        private const string mockErrorMessage = "mock error message";

        private TokenRequestResult CreateSuccessTokenResponse()
        {
            var result = new TokenRequestResult(null);
            result.ResponseStatus = WebTokenRequestStatus.Success;
            result.Token = mockToken;
            result.WebAccountId = mockWebAccountId;
            result.Properties = new Dictionary<string, string>();
            result.Properties.Add("XboxUserId", mockXuid);
            result.Properties.Add("Gamertag", mockGamerTag);
            result.Properties.Add("AgeGroup", mockAgeGroup);
            result.Properties.Add("Environment", mockEnvironment);
            result.Properties.Add("Sandbox", mockSandbox);
            result.Properties.Add("Signature", mockSignature);
            result.Properties.Add("Privileges", mockPrivileges);

            return result;
        }

        private Mock<AccountProvider> CreateMockAccountProvider(TokenRequestResult silentResult, TokenRequestResult uiResult)
        {
            var provider = new Mock<AccountProvider>();
            if (silentResult != null)
            {
                provider
                .Setup(o => o.GetTokenSilentlyAsync(It.IsAny<WebTokenRequest>()))
                .ReturnsAsync(silentResult);
            }

            if (uiResult != null)
            {
                provider
                .Setup(o => o.RequestTokenAsync(It.IsAny<WebTokenRequest>()))
                .Callback(()=> 
                {
                    // Make sure it is called on the UI thread with a coreWindow.
                    // Calling API can only be called on UI thread.
                    var resourceContext = Windows.ApplicationModel.Resources.Core.ResourceContext.GetForCurrentView();
                })
                .ReturnsAsync(uiResult);
            }

            return provider;
        }

        [TestCategory("XboxLiveUser")]
        [TestMethod]
        public async Task CreateUser()
        {
            // default constructor for SUA
            var user1 = new XboxLiveUser();

            // Create xbl user with system user
            var users = await User.FindAllAsync();
            users = users.Where(user => (user.Type != Windows.System.UserType.LocalGuest || user.Type != Windows.System.UserType.RemoteGuest)).ToList();

            Assert.IsTrue(users.Count > 0);
            var systemUser = users[0];
            // default constructor
            var xbluser = new XboxLiveUser(systemUser);
            Assert.AreEqual(systemUser.NonRoamableId, xbluser.WindowsSystemUser.NonRoamableId);
        }

        [TestCategory("XboxLiveUser")]
        [TestMethod]
        public async Task UserSigninSilentlySuccess()
        {
            var user = new XboxLiveUser();
            Assert.IsFalse(user.IsSignedIn);
            var response = CreateSuccessTokenResponse();
            user.GetImpl().Provider = CreateMockAccountProvider(response, null).Object;

            // Create xbl user with system user
            var silentResult = await user.SignInSilentlyAsync();
            Assert.AreEqual(silentResult.Status, SignInStatus.Success);

            Assert.IsTrue(user.IsSignedIn);
            Assert.AreEqual(user.Gamertag, mockGamerTag);
            Assert.AreEqual(user.XboxUserId, mockXuid);
            Assert.AreEqual(user.AgeGroup, mockAgeGroup);
            Assert.AreEqual(user.Privileges, mockPrivileges);
            Assert.AreEqual(user.WebAccountId, mockWebAccountId);
        }

        [TestCategory("XboxLiveUser")]
        [TestMethod]
        public async Task UserSigninWithUiSuccess()
        {
            var user = new XboxLiveUser();
            Assert.IsFalse(user.IsSignedIn);

            var response = CreateSuccessTokenResponse();
            user.GetImpl().Provider = CreateMockAccountProvider(null, response).Object;

            var signinResult = await user.SignInAsync();
            Assert.AreEqual(signinResult.Status, SignInStatus.Success);
            Assert.IsTrue(user.IsSignedIn);
            Assert.AreEqual(user.Gamertag, mockGamerTag);
            Assert.AreEqual(user.XboxUserId, mockXuid);
            Assert.AreEqual(user.AgeGroup, mockAgeGroup);
            Assert.AreEqual(user.Privileges, mockPrivileges);
            Assert.AreEqual(user.WebAccountId, mockWebAccountId);
        }

        [TestCategory("XboxLiveUser")]
        [TestMethod]
        public async Task UserSigninSilentlyUserInteractionRequired()
        {
                var user = new XboxLiveUser();
                var result = new TokenRequestResult(null);
                result.ResponseStatus = WebTokenRequestStatus.UserInteractionRequired;
                user.GetImpl().Provider = CreateMockAccountProvider(result, null).Object;

                var signinResult = await user.SignInSilentlyAsync();
                Assert.AreEqual(signinResult.Status, SignInStatus.UserInteractionRequired);
                Assert.IsFalse(user.IsSignedIn);
        }

        [TestCategory("XboxLiveUser")]
        [TestMethod]
        public async Task UserSigninUIUserCancel()
        {
            var user = new XboxLiveUser();
            var result = new TokenRequestResult(null);
            result.ResponseStatus = WebTokenRequestStatus.UserCancel;
            user.GetImpl().Provider = CreateMockAccountProvider(null, result).Object;

            var signinResult = await user.SignInAsync();
            Assert.AreEqual(signinResult.Status, SignInStatus.UserCancel);
            Assert.IsFalse(user.IsSignedIn);
        }

        [TestCategory("XboxLiveUser")]
        [TestMethod]
        public async Task UserSigninProviderError()
        // provider error 
        {
            var user = new XboxLiveUser();
            var result = new TokenRequestResult(null);
            result.ResponseStatus = WebTokenRequestStatus.ProviderError;
            result.ResponseError = new WebProviderError(mockErrorcode, mockErrorMessage);
            user.GetImpl().Provider = CreateMockAccountProvider(result, result).Object;

            // ProviderError will convert to exception
            try
            {
                var silentResult = await user.SignInSilentlyAsync();
            }
            catch (XboxException ex)
            {
                Assert.AreEqual(ex.HResult, mockErrorcode);
                Assert.IsFalse(string.IsNullOrEmpty(ex.Message));
            }
            Assert.IsFalse(user.IsSignedIn);

            // ProviderError will convert to exception
            try
            {
                var signinResult = await user.SignInAsync();
            }
            catch(XboxException ex)
            {
                Assert.AreEqual(ex.HResult, mockErrorcode);
                Assert.IsFalse(string.IsNullOrEmpty(ex.Message));
            }
            Assert.IsFalse(user.IsSignedIn);
        }

        [TestCategory("XboxLiveUser")]
        [TestMethod]
        public async Task UserSignOut()
        // provider error 
        {
            var user = new XboxLiveUser();
            user.GetImpl().Provider = CreateMockAccountProvider(result, result).Object;


        }
    }
}
