// Copyright (c) Microsoft Corporation
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Microsoft.Xbox.Services.System
{
    using global::System;
    using global::System.Collections.Generic;
    using global::System.Threading.Tasks;
    using Microsoft.Xbox.Services;

    public class TitleCallableUI
    {
        /// <summary>
        /// Shows UI displaying the profile card for a specified user.
        /// </summary>
        /// <param name="user">XboxLiveUser that identifies the user to show the UI on behalf of.</param>
        /// <param name="targetXboxUserId">The Xbox User ID to show information about.</param>
        /// <returns>
        /// An interface for tracking the progress of the asynchronous call.
        /// The operation completes when the UI is closed.
        /// </returns>
        public static Task ShowProfileCardUIAsync(XboxLiveUser user, string targetXboxUserId)
        {
            return Microsoft.Xbox.Services.WinRT.TitleCallableUI.ShowProfileCardUIAsync(
                    targetXboxUserId,
                    user.WindowsSystemUser
                    ).AsTask();
        }

        /// <summary>
        /// Checks if the current user has a specific privilege
        /// </summary>
        /// /// /// <param name="user">XboxLiveUser that identifies the user to show the UI on behalf of.</param>
        /// <param name="privilege">The privilege to check.</param>
        /// <returns>
        /// A boolean which is true if the current user has the privilege.
        /// </returns>
        public static bool CheckPrivilegeSilently(XboxLiveUser user, GamingPrivilege privilege)
        {
            string scope;
            string policy;
            GetPrivilegeScopePolicy(out scope, out policy);

            return Microsoft.Xbox.Services.WinRT.TitleCallableUI.CheckPrivilegeSilently(
                    (Microsoft.Xbox.Services.WinRT.GamingPrivilege)privilege,
                    user.WindowsSystemUser,
                    scope,
                    policy
                    );
        }

        /// <summary>
        /// Checks if the current user has a specific privilege and if it doesn't, it shows UI 
        /// </summary>
        /// /// <param name="user">XboxLiveUser that identifies the user to show the UI on behalf of.</param>
        /// <param name="privilege">The privilege to check.</param>
        /// <param name="friendlyMessage">Text to display in addition to the stock text about the privilege</param>
        /// <returns>
        /// An interface for tracking the progress of the asynchronous call.
        /// The operation completes when the UI is closed.
        /// A boolean which is true if the current user has the privilege.
        /// </returns>
        public static Task<bool> CheckPrivilegeWithUIAsync(XboxLiveUser user, GamingPrivilege privilege, string friendlyMessage)
        {
            string scope;
            string policy;
            GetPrivilegeScopePolicy(out scope, out policy);

            return Microsoft.Xbox.Services.WinRT.TitleCallableUI.CheckPrivilegeWithUIAsync(
                    (Microsoft.Xbox.Services.WinRT.GamingPrivilege)privilege,
                    friendlyMessage,
                    user.WindowsSystemUser,
                    scope,
                    policy
                    ).AsTask();
        }

        private static void GetPrivilegeScopePolicy(out string scope, out string policy)
        {
            var appConfig = XboxLiveAppConfiguration.Instance;
            var authConfig = new AuthConfig
            {
                Sandbox = appConfig.Sandbox,
                EnvrionmentPrefix = appConfig.EnvironmentPrefix,
                Envrionment = appConfig.Environment,
                UseCompactTicket = appConfig.UseFirstPartyToken
            };

            scope = authConfig.RPSTicketService;
            policy = authConfig.RPSTicketPolicy;
        }
    }
}
