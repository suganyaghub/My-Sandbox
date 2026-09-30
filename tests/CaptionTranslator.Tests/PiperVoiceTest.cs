using CaptionTranslator.Speech.Piper;

namespace CaptionTranslator.Tests
{
    [TestClass]
    public class PiperVoiceTest
    {
        [TestMethod]
        [DataRow(0, 1f)]
        [DataRow(10, 0.5f)]
        [DataRow(-10, 2f)]
        [DataRow(20, 0.5f)]
        [DataRow(2, 0.8706f)]
        public void RateToLengthScale_Rate_ReturnsLengthScale(int rate, float expected)
        {
            Assert.AreEqual(expected, PiperVoice.RateToLengthScale(rate), 0.001f);
        }
    }
}
