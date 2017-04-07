// Copyright (c) Microsoft Corporation
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
// 
namespace Microsoft.Xbox.Services
{
    using global::System;

    public class SignOutCompletedEventArgs : EventArgs
    {
        public SignOutCompletedEventArgs(WeakReference user)
        {
            IXboxLiveUser xblUser = user.Target as IXboxLiveUser;
            if (xblUser != null)
            {
                this.User = xblUser;
            }
        }

        public IXboxLiveUser User { get; private set; }
    }
}