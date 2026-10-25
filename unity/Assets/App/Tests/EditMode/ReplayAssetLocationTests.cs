using System.IO;
using App.Capture;
using NUnit.Framework;

namespace App.Tests
{
    public class ReplayAssetLocationTests
    {
        [Test]
        public void AndroidDefaultUsesApkUrlWithoutFilesystemExistenceCheck()
        {
            const string assets = "jar:file:///data/app/demo/base.apk!/assets";
            var actual = ReplayAssetLoader.Resolve("../fixtures/replay/demo_session.json",
                "/data/app/demo/base.apk", assets, true);
            Assert.AreEqual(assets + "/fixtures/replay/demo_session.json", actual);
            Assert.IsTrue(ReplayAssetLoader.IsAssetUrl(actual));
        }

        [Test]
        public void ExplicitDesktopFileRemainsSynchronous()
        {
            var file = Path.GetTempFileName();
            try { Assert.AreEqual(file, ReplayAssetLoader.Resolve(file, "/unused", "/unused", false)); }
            finally { File.Delete(file); }
        }

        [TestCase("jar:file:///data/app/base.apk!/assets/fixtures/replay/demo.json", true)]
        [TestCase("file:///tmp/invented.json", true)]
        [TestCase("https://example.com/private.json", false)]
        [TestCase("/tmp/invented.json", false)]
        [TestCase(null, false)]
        public void AssetUrlsAreLocalOnly(string path, bool expected) =>
            Assert.AreEqual(expected, ReplayAssetLoader.IsAssetUrl(path));
    }
}
