using System.Collections.Generic;
using System.Linq;
using System.Text;
using NUnit.Framework;
using Sygnet.Core;

namespace Sygnet.Tests
{
    /// <summary>Gałęzie PROTOCOL.md §7, których nie pokrywają wektory TV1–TV6.</summary>
    public class VerifierTests
    {
        static long Ts => Payload.Parse(TestData.Get("TV1_air_raid_single").Payload).Timestamp;
        static long Now => TestData.NowForTests;
        static TrustStore Trust => TestData.Trust;
        static byte[] TV1 => TestData.Get("TV1_air_raid_single").Frame;

        static Payload P(int issuer, int type, int area, long ts, int validMinutes = 120, int seq = 1, byte[] note = null) =>
            new Payload
            {
                IssuerId = issuer, Type = type, AreaCode = area, Timestamp = ts, ValidMinutes = validMinutes,
                Sequence = seq, Note = note ?? Encoding.UTF8.GetBytes("test"),
            };

        static byte[] Signed(Payload p, params (int id, string seed)[] signers)
        {
            var pb = p.ToBytes();
            return Frame.Build(pb, signers.Select(s => new SignatureEntry(s.id, Ed25519.Sign(TestData.Seed(s.seed), pb))).ToList());
        }

        static VerificationResult V(byte[] frame, long? now = null, int userArea = 1465,
            ISet<int> revoked = null, ISet<(int, int)> seen = null) =>
            Verifier.Verify(frame, Trust, now ?? Now, userArea, revoked, seen);

        static void AssertResult(VerificationResult r, VerifyStatus status, string reason)
        {
            Assert.AreEqual(status, r.Status, r.ToString());
            Assert.AreEqual(reason, r.ReasonCode, r.ToString());
        }

        // ── MALFORMED ──

        [Test]
        public void Malformed_BadCrc()
        {
            var f = (byte[])TV1.Clone();
            f[30] ^= 0x01;                                      // bez przeliczenia CRC
            AssertResult(V(f), VerifyStatus.Malformed, "BAD_CRC");
        }

        [Test]
        public void Malformed_BadMagicTruncatedEmpty()
        {
            var f = (byte[])TV1.Clone();
            f[0] = (byte)'X';
            AssertResult(V(f), VerifyStatus.Malformed, "BAD_MAGIC");
            AssertResult(V(TV1.Take(50).ToArray()), VerifyStatus.Malformed, "BAD_LENGTH");
            AssertResult(V(new byte[0]), VerifyStatus.Malformed, "BAD_MAGIC");
            AssertResult(V(null), VerifyStatus.Malformed, "BAD_MAGIC");
            AssertResult(V(new byte[] { 0x53, 0x47, 0, 2, 0, 0 }), VerifyStatus.Malformed, "BAD_CRC");
        }

        [Test]
        public void Malformed_InconsistentSignatureCount()
        {
            var f = (byte[])TV1.Clone();
            f[4 + 16 + 29] = 2;                                 // sig_count = 2, a w ramce jest 1 podpis
            Bytes.WriteU16(f, f.Length - 2, Crc16.Compute(f, 0, f.Length - 2));
            AssertResult(V(f), VerifyStatus.Malformed, "BAD_LENGTH");
        }

        [Test]
        public void Malformed_UnknownVersion()
        {
            var p = P(1, AlertTypes.AirRaid, 1465, Ts);
            p.Version = 2;
            AssertResult(V(Signed(p, (1, "1"))), VerifyStatus.Malformed, "BAD_VERSION");
        }

        // ── FORGED ──

        [Test]
        public void Forged_NoSignature()
        {
            var f = Frame.Build(P(1, AlertTypes.AirRaid, 1465, Ts).ToBytes(), new List<SignatureEntry>());
            AssertResult(V(f), VerifyStatus.Forged, "NO_SIGNATURE");
        }

        [Test]
        public void Forged_PrimarySignerMismatch()
        {
            AssertResult(V(Signed(P(1, AlertTypes.AirRaid, 1465, Ts), (3, "3"))), VerifyStatus.Forged, "PRIMARY_SIGNER_MISMATCH");
        }

        [Test]
        public void Forged_UnknownIssuer()
        {
            AssertResult(V(Signed(P(9, AlertTypes.AirRaid, 1465, Ts), (9, "hacker"))), VerifyStatus.Forged, "UNKNOWN_ISSUER:9");
        }

        [Test]
        public void Forged_RevokedIssuer()
        {
            AssertResult(V(TV1, revoked: new HashSet<int> { 1 }), VerifyStatus.Forged, "REVOKED_ISSUER:1");
        }

        [Test]
        public void Forged_CertExpired()
        {
            const long afterCerts = 1830297601;                 // 2028-01-01 + 1 s
            var f = Signed(P(1, AlertTypes.AirRaid, 1465, afterCerts - 60), (1, "1"));
            AssertResult(V(f, afterCerts), VerifyStatus.Forged, "CERT_EXPIRED:1");
        }

        [Test]
        public void Forged_SecondSignatureBad()
        {
            var p = P(3, AlertTypes.Evacuation, 1465, Ts);
            var f = Signed(p, (3, "3"), (5, "hacker"));
            AssertResult(V(f), VerifyStatus.Forged, "BAD_SIGNATURE:5");
        }

        [Test]
        public void Forged_TimestampInFuture_WithFiveMinuteTolerance()
        {
            AssertResult(V(Signed(P(1, AlertTypes.AirRaid, 1465, Now + 301), (1, "1"))), VerifyStatus.Forged, "TIMESTAMP_IN_FUTURE");
            AssertResult(V(Signed(P(1, AlertTypes.AirRaid, 1465, Now + 300), (1, "1"))), VerifyStatus.Verified, "OK");
        }

        [Test]
        public void Forged_RevokeNotFromRoot()
        {
            var p = P(1, AlertTypes.KeyRevoke, 0, Ts, note: new byte[] { 0, 3 });
            AssertResult(V(Signed(p, (1, "1"))), VerifyStatus.Forged, "REVOKE_NOT_ROOT");
        }

        [Test]
        public void Forged_RootSendingNormalAlert()
        {
            AssertResult(V(Signed(P(0, AlertTypes.AirRaid, 1465, Ts), (0, "0"))), VerifyStatus.Forged, "ROOT_ONLY_REVOKE");
        }

        // ── INCOMPLETE ──

        [Test]
        public void Incomplete_SameSignerTwiceIsNotDual()
        {
            var f = Signed(P(3, AlertTypes.Evacuation, 1465, Ts), (3, "3"), (3, "3"));
            AssertResult(V(f), VerifyStatus.Incomplete, "DUAL_SIGNATURE_REQUIRED");
        }

        // ── EXPIRED / DUPLICATE / OTHER_AREA ──

        [Test]
        public void Expired_ExactBoundary()
        {
            var f = Signed(P(1, AlertTypes.AirRaid, 1465, Ts, validMinutes: 10), (1, "1"));
            AssertResult(V(f, Ts + 600), VerifyStatus.Verified, "OK");
            AssertResult(V(f, Ts + 601), VerifyStatus.Expired, "EXPIRED");
        }

        [Test]
        public void Duplicate_AfterCommit()
        {
            var seen = new HashSet<(int, int)>();
            var revoked = new HashSet<int>();
            var r = V(TV1, seen: seen);
            AssertResult(r, VerifyStatus.Verified, "OK");
            Assert.IsFalse(Verifier.Commit(r, revoked, seen));
            CollectionAssert.Contains(seen, (1, 1));
            AssertResult(V(TV1, seen: seen), VerifyStatus.Duplicate, "ALREADY_RECEIVED");
        }

        [Test]
        public void Commit_IgnoresNonAuthentic()
        {
            var seen = new HashSet<(int, int)>();
            var r = V(TestData.Get("TV3_forged_hacker_as_1").Frame);
            Assert.IsFalse(Verifier.Commit(r, new HashSet<int>(), seen));
            CollectionAssert.IsEmpty(seen);
        }

        [Test]
        public void OtherArea_UserInKrakowOrWholeVoivodeship()
        {
            AssertResult(V(TV1, userArea: 1261), VerifyStatus.VerifiedOtherArea, "OTHER_AREA");
            AssertResult(V(TV1, userArea: 14), VerifyStatus.VerifiedOtherArea, "OTHER_AREA");
            var national = Signed(P(1, AlertTypes.AirRaid, 0, Ts), (1, "1"));
            AssertResult(V(national, userArea: 1261), VerifyStatus.Verified, "OK");
            var voivodeship = Signed(P(3, AlertTypes.Chemical, 14, Ts), (3, "3"));
            AssertResult(V(voivodeship, userArea: 1465), VerifyStatus.Verified, "OK");
        }

        // ── KEY_REVOKE ──

        [Test]
        public void KeyRevoke_FromRoot_RevokesIssuerAfterCommit()
        {
            var seen = new HashSet<(int, int)>();
            var revoked = new HashSet<int>();
            var revoke = Signed(P(0, AlertTypes.KeyRevoke, 0, Ts, seq: 1, note: new byte[] { 0, 1 }), (0, "0"));

            var r = V(revoke, seen: seen, revoked: revoked);
            AssertResult(r, VerifyStatus.Verified, "OK");
            Assert.AreEqual(1, Verifier.RevokedIssuerId(r.Payload));
            Assert.IsTrue(Verifier.Commit(r, revoked, seen));
            CollectionAssert.Contains(revoked, 1);

            AssertResult(V(TV1, seen: seen, revoked: revoked), VerifyStatus.Forged, "REVOKED_ISSUER:1");
        }

        // ── pola wyniku ──

        [Test]
        public void Result_CarriesNamesFromCertificates()
        {
            var r = V(TestData.Get("TV2_evacuation_dual").Frame);
            Assert.AreEqual("Wojewoda Mazowiecki", r.IssuerName);
            CollectionAssert.AreEqual(new[] { 3, 5 }, r.SignerIds);
            CollectionAssert.AreEqual(new[] { "Wojewoda Mazowiecki", "Komendant Wojewódzki PSP Mazowsze" }, r.SignerNames);
            Assert.AreEqual("Kierunek: Grodzisk Maz.", r.Payload.NoteText);
            CollectionAssert.AreEqual(TestData.Get("TV2_evacuation_dual").Frame, r.RawFrame);
        }

        [Test]
        public void Messages_ExplainForgery()
        {
            var r = V(TestData.Get("TV3_forged_hacker_as_1").Frame);
            StringAssert.Contains("Dowództwo Operacyjne RSZ", Messages.Reason(r, Trust));
            var r5 = V(TestData.Get("TV5_unauthorized_area").Frame);
            StringAssert.StartsWith("Prezydent m.st. Warszawy nie ma uprawnień", Messages.Reason(r5, Trust));
        }
    }
}
