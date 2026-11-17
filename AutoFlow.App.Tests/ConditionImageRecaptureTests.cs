using System.IO;
using AutoFlow.App.Services;
using AutoFlow.Core.Scripting.Conditions;
using Newtonsoft.Json;
using OpenCvSharp;
using Xunit;

namespace AutoFlow.App.Tests;

public sealed class ConditionImageRecaptureTests
{
    [Fact]
    public void RecaptureUsesConditionReferenceAndPreservesMetadata()
    {
        var root = Path.Combine(Path.GetTempPath(), "AutoFlowRecapture_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "image.png");
            using var frame = new Mat(80, 100, MatType.CV_8UC3, Scalar.Black);
            using (var region = new Mat(frame, new Rect(20, 10, 30, 20))) region.SetTo(Scalar.White);
            using (var oldImage = new Mat(2, 2, MatType.CV_8UC3, Scalar.Black)) oldImage.SaveImage(path);
            var condition = new VisualConditionSpec
            {
                Id = "Test.Image", TemplatePath = "image.png",
                ReferenceOuterWidth = 200, ReferenceOuterHeight = 160,
                RegionX1 = 40, RegionY1 = 20, RegionX2 = 100, RegionY2 = 60
            };
            var before = JsonConvert.SerializeObject(condition);
            var metadataPath = Path.Combine(root, "condition.json");
            File.WriteAllText(metadataPath, before);
            Assert.Equal(path, ConditionImageRecapture.Save(frame, condition, root));
            using var saved = Cv2.ImRead(path);
            Assert.Equal(new Size(30, 20), saved.Size());
            Assert.Equal(255d, Cv2.Mean(saved).Val0, precision: 6);
            Assert.Equal(before, JsonConvert.SerializeObject(condition));
            Assert.Equal(before, File.ReadAllText(metadataPath));

            var bytes = File.ReadAllBytes(path);
            condition.ReferenceOuterWidth = 0;
            Assert.Throws<InvalidOperationException>(() => ConditionImageRecapture.Save(frame, condition, root));
            Assert.Equal(bytes, File.ReadAllBytes(path));
        }
        finally { Directory.Delete(root, true); }
    }
}
