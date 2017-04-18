// Copyright (c) Microsoft Corporation
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Microsoft.Xbox.Services.UWP.UnitTests
{
    using global::System;
    using Windows.System;
    using Microsoft.VisualStudio.TestPlatform.UnitTestFramework;
    using Microsoft.Xbox.Services.System;
    using global::System.Linq;
    using global::System.Threading.Tasks;
    using UITestMethod = Microsoft.VisualStudio.TestPlatform.UnitTestFramework.AppContainer.UITestMethodAttribute;


    [TestClass]
    public class UserTest
    {
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
    }
}
