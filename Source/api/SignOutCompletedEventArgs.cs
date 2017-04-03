// Copyright (c) Microsoft Corporation
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
// 
namespace Microsoft.Xbox.Services
{
    using global::System;

    public class SignOutCompletedEventArgs : EventArgs
    {
        public SignOutCompletedEventArgs(WeakReference<IXboxLiveUser> user)
        {
            this.User = user;
        }

        public WeakReference<IXboxLiveUser> User { get; private set; }
    }
}