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
        private TokenRequestResult CreateSuccessTokenResponse()
        {
            var result = new TokenRequestResult(null);
            result.ResponseStatus = WebTokenRequestStatus.Success;
            result.Token = "token";
            result.WebAccountId = "mock webaccount id";
            result.Properties = new Dictionary<string, string>();
            result.Properties.Add("XboxUserId", "123456");
            result.Properties.Add("Gamertag", "mock gamertag");
            result.Properties.Add("AgeGroup", "Adult");
            result.Properties.Add("Environment", "prod");
            result.Properties.Add("Sandbox", "sandbox");
            result.Properties.Add("Signature", "singature");
            result.Properties.Add("Privileges", "12 3 45 6");

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
        public async Task UserSigninSilently()
        {
            var user = new XboxLiveUser();
            var response = CreateSuccessTokenResponse();
            user.GetImpl().Provider = CreateMockAccountProvider(response, null).Object;

            // Create xbl user with system user
            var silentResult = await user.SignInSilentlyAsync();
            Assert.AreEqual(silentResult.Status, SignInStatus.Success);

            Assert.IsTrue(user.IsSignedIn);
        }

        [TestCategory("XboxLiveUser")]
        [TestMethod]
        public async Task UserSignin()
        {
            var user = new XboxLiveUser();
            var response = CreateSuccessTokenResponse();
            user.GetImpl().Provider = CreateMockAccountProvider(response, response).Object;

            var signinResult = await user.SignInAsync();
            Assert.AreEqual(signinResult.Status, SignInStatus.Success);
        }

        [TestCategory("XboxLiveUser")]
        [TestMethod]
        public async Task UserSilentSigninFail()
        {
            var user1 = new XboxLiveUser();
            var result = new TokenRequestResult(null);
            user1.GetImpl().Provider = CreateMockAccountProvider(result, null).Object;

            // Create xbl user with system user
            var silentResult = await user1.SignInSilentlyAsync();
            Assert.AreEqual(silentResult.Status, SignInStatus.Success);

            var signinResult = await user1.SignInAsync();
            Assert.AreEqual(signinResult.Status, SignInStatus.Success);
        }
    }
}
