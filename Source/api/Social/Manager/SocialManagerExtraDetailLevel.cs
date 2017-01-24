// -----------------------------------------------------------------------
//  <copyright file="SocialManagerExtraDetailLevel.cs" company="Microsoft">
//      Copyright (c) Microsoft. All rights reserved.
//      Licensed under the MIT license. See LICENSE file in the project root for full license information.
//  </copyright>
// -----------------------------------------------------------------------

namespace Microsoft.Xbox.Services.Social.Manager
{
    using global::System;

    [Flags]
    public enum SocialManagerExtraDetailLevel
    {
        NoExtraDetail = 0x0,
        TitleHistoryLevel = 0x1,
        PreferredColorLevel = 0x2,
    }
}