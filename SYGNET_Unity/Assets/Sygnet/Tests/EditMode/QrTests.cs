using System;
using NUnit.Framework;
using Sygnet.App;
using Sygnet.Core;
using UnityEngine;
using ZXing;
using ZXing.QrCode;
using ZXing.QrCode.Internal;

namespace Sygnet.Tests
{
    /// <summary>Ścieżka QR bez kamery: ZXing generuje kod (poziom M), skaner dekoduje klatkę w układzie tekstury Unity.</summary>
    public class QrTests
    {
        /// <summary>QR jak z plakatu, wklejony w większą „klatkę kamery” 1280×720, zapisaną od dołu jak Texture2D.</summary>
        static Color32[] CameraFrame(string text, int w, int h, int qrSize, bool mirrorRows = true)
        {
            var pd = new BarcodeWriterPixelData
            {
                Format = BarcodeFormat.QR_CODE,
                Options = new QrCodeEncodingOptions { ErrorCorrection = ErrorCorrectionLevel.M, Margin = 4, Width = qrSize, Height = qrSize },
            }.Write(text);
            var px = new Color32[w * h];
            for (int i = 0; i < px.Length; i++) px[i] = new Color32(200, 200, 200, 255);
            int ox = (w - pd.Width) / 2, oy = (h - pd.Height) / 2;
            for (int y = 0; y < pd.Height; y++)
            for (int x = 0; x < pd.Width; x++)
            {
                int s = (y * pd.Width + x) * 4;                                     // BGRA, od góry
                int ty = mirrorRows ? h - 1 - (oy + y) : oy + y;                    // tekstura Unity: od dołu
                px[ty * w + ox + x] = new Color32(pd.Pixels[s + 2], pd.Pixels[s + 1], pd.Pixels[s], 255);
            }
            return px;
        }

        [TestCase("TV1_air_raid_single")]
        [TestCase("TV2_evacuation_dual")]
        public void Scanner_DecodesVectorQr_AndVerifies(string name)
        {
            var v = TestData.Get(name);
            const int w = 1280, h = 720;
            var text = QrScanner.DecodeFrame(QrScanner.CreateReader(), CameraFrame(v.QrText, w, h, 600), new byte[w * h], w, h);
            Assert.AreEqual(v.QrText, text);
            Assert.IsTrue(Frame.TryFromQrText(text, out var frame));
            var r = Verifier.Verify(frame, TestData.Trust, TestData.NowForTests, TestData.UserArea, null, null);
            Assert.AreEqual(v.ExpectedStatus, r.StatusName);
        }

        [Test]
        public void Scanner_LargestFrameFitsInQrLevelM()
        {
            // ramka z 2 podpisami i 60 B dopisku = 215 B → tekst QR ~292 znaków
            var p = new Payload { IssuerId = 3, Type = AlertTypes.Evacuation, AreaCode = 1465, Timestamp = 1, ValidMinutes = 1, Sequence = 1, Note = new byte[60] };
            var f = Frame.Build(p.ToBytes(), new[] { new SignatureEntry(3, new byte[64]), new SignatureEntry(5, new byte[64]) });
            Assert.AreEqual(215, f.Length);
            var qr = Frame.ToQrText(f);
            const int w = 1280, h = 720;
            Assert.AreEqual(qr, QrScanner.DecodeFrame(QrScanner.CreateReader(), CameraFrame(qr, w, h, 680), new byte[w * h], w, h));
        }

        [Test]
        public void Scanner_ReturnsRawTextForForeignQr_AppRejectsIt()
        {
            const string url = "https://zlosliwa-strona.example/ewakuacja";
            const int w = 640, h = 480;
            var text = QrScanner.DecodeFrame(QrScanner.CreateReader(), CameraFrame(url, w, h, 400), new byte[w * h], w, h);
            Assert.AreEqual(url, text);
            Assert.IsFalse(Frame.TryFromQrText(text, out _));
        }
    }
}
