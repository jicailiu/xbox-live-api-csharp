using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Microsoft.Xbox.Services.UnitTests.Stats
{
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using Microsoft.Xbox.Services.Stats.Manager;

    [TestClass]
    public class StatsManagerTests : TestBase
    {
        [TestMethod]
        public void GetInstance()
        {
            IStatsManager sm = StatsManager.Instance;
            Assert.IsNotNull(sm);
        }

        [TestMethod]
        public void AddLocalUser()
        {
            StatsManager.Instance.AddLocalUser(this.user);
        }
    }
}
