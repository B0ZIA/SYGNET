using System.Collections.Generic;
using System.Linq;
using System.Text;
using NUnit.Framework;
using Sygnet.Core;

namespace Sygnet.Tests
{
    /// <summary>Zgodność bajt w bajt z testvectors.json (PROTOCOL.md §9, CLIENT_UNITY.md §6).</summary>
    public class ProtocolTests
    {
        static IEnumerable<string> Vectors => TestData.VectorNames;

        /// <summary>Kto podpisał który wektor (seedy testowe). TV6 to TV1 z podmienionym bajtem – nie da się go podpisać.</summary>
        static readonly Dictionary<string, (int id, string seed)[]> Signers = new Dictionary<string, (int, string)[]>
        {
            ["TV1_air_raid_single"] = new[] { (1, "1") },
            ["TV2_evacuation_dual"] = new[] { (3, "3"), (5, "5") },
            ["TV3_forged_hacker_as_1"] = new[] { (1, "hacker") },
            ["TV4_evacuation_single"] = new[] { (3, "3") },
            ["TV5_unauthorized_area"] = new[] { (6, "6") },
        };

        static IEnumerable<string> SignableVectors => Signers.Keys;

        [Test]
        public void Crc16_CheckValue()
        {
            Assert.AreEqual(0x29B1, Crc16.Compute(Encoding.ASCII.GetBytes("123456789")));
            var check = (Dictionary<string, object>)TestData.Json["crc16_check"];
            Assert.AreEqual((string)check["crc_hex"], Crc16.Compute(Encoding.ASCII.GetBytes((string)check["input_ascii"])).ToString("X4"));
        }

        [Test]
        public void RootFingerprint_MatchesSpec()
        {
            Assert.AreEqual("6A38-03D5-F059-902A", Ed25519.Fingerprint(TestData.RootPub));
            Assert.AreEqual(TestData.RootFingerprint, Ed25519.Fingerprint(TestData.RootPub));
            CollectionAssert.AreEqual(TestData.RootPub, Ed25519.PublicKeyFromSeed(TestData.Seed("0")));
        }

        [Test]
        public void TV1_PayloadHex_MatchesSpec()
        {
            const string spec = "0100010105b96ac1a7e000780001001d536368726f6e3a206d6574726f20c59a7769c499746f6b727a79736b61";
            Assert.AreEqual(spec, Bytes.ToHex(TestData.Get("TV1_air_raid_single").Payload));
            var p = Payload.Parse(Bytes.FromHex(spec));
            Assert.AreEqual(1, p.IssuerId);
            Assert.AreEqual(AlertTypes.AirRaid, p.Type);
            Assert.AreEqual(1465, p.AreaCode);
            Assert.AreEqual(120, p.ValidMinutes);
            Assert.AreEqual(1, p.Sequence);
            Assert.AreEqual("Schron: metro Świętokrzyska", p.NoteText);
        }

        [TestCaseSource(nameof(Vectors))]
        public void Payload_ParseToBytes_RoundTrip(string name)
        {
            var v = TestData.Get(name);
            CollectionAssert.AreEqual(v.Payload, Payload.Parse(v.Payload).ToBytes());
        }

        [TestCaseSource(nameof(Vectors))]
        public void Frame_Parse_ExtractsPayloadAndRebuilds(string name)
        {
            var v = TestData.Get(name);
            Assert.AreEqual(v.FrameLen, v.Frame.Length);
            Assert.IsTrue(Frame.TryParse(v.Frame, out var f, out var err), err);
            CollectionAssert.AreEqual(v.Payload, f.PayloadBytes);
            CollectionAssert.AreEqual(v.Frame, Frame.Build(f.PayloadBytes, f.Signatures.ToList()));
        }

        [TestCaseSource(nameof(SignableVectors))]
        public void Frame_SignedFromTestSeeds_IsByteIdentical(string name)
        {
            var v = TestData.Get(name);
            var sigs = Signers[name].Select(s => new SignatureEntry(s.id, Ed25519.Sign(TestData.Seed(s.seed), v.Payload))).ToList();
            Assert.AreEqual(Bytes.ToHex(v.Frame), Bytes.ToHex(Frame.Build(v.Payload, sigs)));
        }

        [TestCaseSource(nameof(Vectors))]
        public void QrText_RoundTrip(string name)
        {
            var v = TestData.Get(name);
            Assert.AreEqual(v.QrText, Frame.ToQrText(v.Frame));
            Assert.IsTrue(Frame.TryFromQrText(v.QrText, out var back));
            CollectionAssert.AreEqual(v.Frame, back);
        }

        [Test]
        public void QrText_RejectsNonSygnet()
        {
            Assert.IsFalse(Frame.TryFromQrText("https://example.com/phish", out _));
            Assert.IsFalse(Frame.TryFromQrText("SYG1:", out _));
            Assert.IsFalse(Frame.TryFromQrText("SYG1:@@@@", out _));
            Assert.IsFalse(Frame.TryFromQrText(null, out _));
        }

        [TestCaseSource(nameof(Vectors))]
        public void Verify_Now(string name)
        {
            var v = TestData.Get(name);
            var r = Verifier.Verify(v.Frame, TestData.Trust, TestData.NowForTests, TestData.UserArea, null, null);
            Assert.AreEqual(v.ExpectedStatus, r.StatusName, r.ToString());
            Assert.AreEqual(v.Reason, r.ReasonCode);
        }

        [TestCaseSource(nameof(Vectors))]
        public void Verify_ThreeDaysLater(string name)
        {
            var v = TestData.Get(name);
            var r = Verifier.Verify(v.Frame, TestData.Trust, TestData.NowExpired, TestData.UserArea, null, null);
            Assert.AreEqual(v.ExpectedStatus3DaysLater, r.StatusName, r.ToString());
        }

        [Test]
        public void TrustStore_AcceptsAllTestCertificates()
        {
            var t = TestData.Trust;
            CollectionAssert.IsEmpty(t.Rejected, string.Join("\n", t.Rejected));
            CollectionAssert.AreEquivalent(new[] { 1, 3, 5, 6 }, t.Issuers.Keys);
            Assert.AreEqual("Dowództwo Operacyjne RSZ", t.NameOf(1));
            Assert.AreEqual("Wojewoda Mazowiecki", t.NameOf(3));
            Assert.AreEqual("Komendant Wojewódzki PSP Mazowsze", t.NameOf(5));
            Assert.AreEqual("Prezydent m.st. Warszawy", t.NameOf(6));
            CollectionAssert.AreEqual(new[] { 0 }, t.Issuers[1].Scopes);
            CollectionAssert.AreEqual(new[] { 14 }, t.Issuers[3].Scopes);
            CollectionAssert.AreEqual(new[] { 1465 }, t.Issuers[6].Scopes);
            foreach (var id in t.Issuers.Keys)
                CollectionAssert.AreEqual(TestData.IssuerPub(id), t.Issuers[id].PublicKey, "pubkey wydawcy " + id);
            Assert.IsTrue(t.Issuers[1].IsValidAt(TestData.NowForTests));
        }

        [Test]
        public void TrustStore_RejectsTamperedCertificate()
        {
            var entries = TestData.TrustStoreEntries;
            var cert = (byte[])entries[1].cert.Clone();
            cert[cert.Length - 1] ^= 0x01;                       // zmiana ostatniej litery nazwy
            entries[1] = (cert, entries[1].sig);
            var t = TrustStore.FromJson(TestData.ToJson(entries), TestData.RootPub);
            Assert.AreEqual(3, t.Issuers.Count);
            Assert.IsFalse(t.Issuers.ContainsKey(3));
            Assert.AreEqual(1, t.Rejected.Count);
            StringAssert.Contains("podpis ROOT", t.Rejected[0]);
        }

        [Test]
        public void TrustStore_RejectsTamperedRootSignature()
        {
            var entries = TestData.TrustStoreEntries;
            var sig = (byte[])entries[0].sig.Clone();
            sig[10] ^= 0x80;
            entries[0] = (entries[0].cert, sig);
            var t = TrustStore.FromJson(TestData.ToJson(entries), TestData.RootPub);
            Assert.IsFalse(t.Issuers.ContainsKey(1));
            Assert.AreEqual(3, t.Issuers.Count);
        }

        [Test]
        public void TrustStore_WithWrongRootKey_RejectsEverything()
        {
            var otherRoot = Ed25519.PublicKeyFromSeed(TestData.Seed("hacker"));
            var t = TrustStore.FromJson(TestData.ToJson(TestData.TrustStoreEntries), otherRoot);
            Assert.AreEqual(0, t.Issuers.Count);
            Assert.AreEqual(5, t.Rejected.Count);                // 4 certy + niezgodny odcisk ROOT
        }

        [Test]
        public void Areas_Covers()
        {
            Assert.IsTrue(Areas.Covers(0, 1465));
            Assert.IsTrue(Areas.Covers(14, 1465));
            Assert.IsTrue(Areas.Covers(1465, 1465));
            Assert.IsFalse(Areas.Covers(14, 1261));
            Assert.IsFalse(Areas.Covers(1465, 14));
            Assert.IsFalse(Areas.Covers(1465, 0));
            Assert.IsFalse(Areas.Covers(1261, 1465));
        }

        [Test]
        public void AlertTypes_OnlyEvacuationIsCritical()
        {
            for (int t = 0; t <= 255; t++)
                Assert.AreEqual(t == AlertTypes.Evacuation, AlertTypes.IsCritical(t), "typ " + t);
        }

        [Test]
        public void IssuerCert_BuildParse_RoundTrip()
        {
            var pub = TestData.IssuerPub(6);
            var bytes = IssuerCert.Build(6, pub, 1767225600, 1830297600, new[] { 1465 }, "Prezydent m.st. Warszawy");
            CollectionAssert.AreEqual(TestData.TrustStoreEntries[3].cert, bytes);
            var c = IssuerCert.Parse(bytes);
            Assert.AreEqual(6, c.IssuerId);
            Assert.AreEqual("Prezydent m.st. Warszawy", c.Name);
        }
    }
}
