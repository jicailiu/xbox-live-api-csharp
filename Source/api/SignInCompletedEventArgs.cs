// Copyright (c) Microsoft Corporation
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Microsoft.Xbox.Services
{
    using global::System;

    public class SignInCompletedEventArgs : EventArgs
    {
        public SignInCompletedEventArgs(string xboxLiveUserId)
        {
            this.XboxLiveUserId = xboxLiveUserId;
        }

        public string XboxLiveUserId { get; private set; }
    }
}