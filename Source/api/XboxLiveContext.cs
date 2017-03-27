// Copyright (c) Microsoft Corporation
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Microsoft.Xbox.Services
{
    using global::System.IO;

    public partial class XboxLiveContext
    {
        public XboxLiveContext(XboxLiveUser user)
        {
            this.User = user;

            try
            {
                this.AppConfig = XboxLiveAppConfiguration.Instance;
            }
            catch (FileLoadException)
            {
                this.AppConfig = null;
            }
            this.Settings = new XboxLiveContextSettings();
        }

        public XboxLiveAppConfiguration AppConfig { get; private set; }

        public XboxLiveContextSettings Settings { get; private set; }

        public XboxLiveUser User { get; private set; }
    }
}