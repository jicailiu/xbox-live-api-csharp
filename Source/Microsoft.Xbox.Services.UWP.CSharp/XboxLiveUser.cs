// Copyright (c) Microsoft Corporation
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Microsoft.Xbox.Services
{
    using global::System.Threading.Tasks;
    using Microsoft.Xbox.Services.System;

    public partial class XboxLiveUser
    {
        public XboxLiveUser()
        {
            this.userImpl = new UserImpl(SignInCompleted, SignOutCompleted, null);
        }

        public XboxLiveUser(Windows.System.User systemUser)
        {
            this.userImpl = new UserImpl(SignInCompleted, SignOutCompleted, systemUser);
        }

        public Task RefreshToken()
        {
            return this.userImpl.InternalGetTokenAndSignatureAsync("GET", this.userImpl.AuthConfig.XboxLiveEndpoint, null, null, false, true).ContinueWith((taskAndSignatureResultTask) =>
            {
                if (taskAndSignatureResultTask.Exception != null)
                {
                    throw taskAndSignatureResultTask.Exception;
                }
            });
        }
    }
}