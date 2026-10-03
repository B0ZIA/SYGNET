using System;
using System.Collections.Generic;

namespace Sygnet.Core
{
    public enum VerifyStatus { Verified, VerifiedOtherArea, Expired, Incomplete, Forged, Duplicate, Malformed }

    public sealed class VerificationResult
    {
        public VerifyStatus Status;
        public string ReasonCode;        // np. "BAD_SIGNATURE:1" – format jak w sygnet_ref.py
        public Payload Payload;          // null przy Malformed
        public ParsedFrame Frame;        // null przy Malformed
        public string IssuerName;        // z certyfikatu; null, gdy wydawca nieznany
        public int[] SignerIds = Array.Empty<int>();
        public string[] SignerNames = Array.Empty<string>();
        public byte[] RawFrame;          // do „Przekaż dalej”

        public bool IsAuthentic => Status == VerifyStatus.Verified || Status == VerifyStatus.VerifiedOtherArea;

        /// <summary>Nazwa statusu jak w PROTOCOL.md / testvectors.json, np. "VERIFIED_OTHER_AREA".</summary>
        public string StatusName => Verifier.StatusName(Status);

        public override string ToString() => StatusName + " " + ReasonCode;
    }

    /// <summary>
    /// Weryfikacja ramki wg PROTOCOL.md §7 – kolejność kroków jest obowiązkowa i odwzorowuje verify_frame
    /// z tools/sygnet_ref.py. <see cref="Verify"/> nie zmienia stanu; po odbiorze wywołaj <see cref="Commit"/>.
    /// </summary>
    public static class Verifier
    {
        public const int ClockSkewSeconds = 300;

        static readonly HashSet<int> NoRevoked = new HashSet<int>();
        static readonly HashSet<(int issuer, int seq)> NoSeen = new HashSet<(int issuer, int seq)>();

        public static VerificationResult Verify(byte[] frame, TrustStore trust, long nowUnix, int userArea,
            ISet<int> revoked, ISet<(int issuer, int seq)> seen)
        {
            revoked = revoked ?? NoRevoked;
            seen = seen ?? NoSeen;

            // 1. MAGIC, długość, CRC
            if (!Sygnet.Core.Frame.TryParse(frame, out var f, out var error))
                return new VerificationResult { Status = VerifyStatus.Malformed, ReasonCode = error, RawFrame = frame };

            var p = f.Payload;
            var r = new VerificationResult
            {
                Payload = p,
                Frame = f,
                RawFrame = frame,
                IssuerName = trust.NameOf(p.IssuerId),
                SignerIds = new int[f.Signatures.Count],
                SignerNames = new string[f.Signatures.Count],
            };
            for (int i = 0; i < f.Signatures.Count; i++)
            {
                r.SignerIds[i] = f.Signatures[i].SignerId;
                r.SignerNames[i] = trust.NameOf(f.Signatures[i].SignerId);
            }

            // 2. wersja
            if (p.Version != Payload.CurrentVersion) return Done(r, VerifyStatus.Malformed, "BAD_VERSION");

            // 3. podpisy obecne, pierwszy = wydawca
            if (f.Signatures.Count == 0) return Done(r, VerifyStatus.Forged, "NO_SIGNATURE");
            if (f.Signatures[0].SignerId != p.IssuerId) return Done(r, VerifyStatus.Forged, "PRIMARY_SIGNER_MISMATCH");

            // 4. każdy podpis
            var validSigners = new HashSet<int>();
            foreach (var s in f.Signatures)
            {
                int sid = s.SignerId;
                IssuerCert cert = null;   // null = ROOT: klucz wbudowany, scopes [0]
                if (sid != TrustStore.RootId)
                {
                    if (!trust.TryGet(sid, out cert)) return Done(r, VerifyStatus.Forged, "UNKNOWN_ISSUER:" + sid);
                    if (revoked.Contains(sid)) return Done(r, VerifyStatus.Forged, "REVOKED_ISSUER:" + sid);
                    if (!cert.IsValidAt(nowUnix)) return Done(r, VerifyStatus.Forged, "CERT_EXPIRED:" + sid);
                }
                var pub = cert == null ? trust.RootPublicKey : cert.PublicKey;
                if (!Ed25519.Verify(pub, f.PayloadBytes, s.Signature))
                    return Done(r, VerifyStatus.Forged, "BAD_SIGNATURE:" + sid);
                if (cert != null && !cert.CoversArea(p.AreaCode))
                    return Done(r, VerifyStatus.Forged, "UNAUTHORIZED_AREA:" + sid);
                validSigners.Add(sid);
            }

            // 5. KEY_REVOKE tylko od ROOT, ROOT tylko KEY_REVOKE
            if (p.Type == AlertTypes.KeyRevoke && p.IssuerId != TrustStore.RootId)
                return Done(r, VerifyStatus.Forged, "REVOKE_NOT_ROOT");
            if (p.IssuerId == TrustStore.RootId && p.Type != AlertTypes.KeyRevoke)
                return Done(r, VerifyStatus.Forged, "ROOT_ONLY_REVOKE");

            // 6. typ krytyczny wymaga 2 RÓŻNYCH poprawnych podpisów
            if (AlertTypes.IsCritical(p.Type) && validSigners.Count < 2)
                return Done(r, VerifyStatus.Incomplete, "DUAL_SIGNATURE_REQUIRED");

            // 7–8. czas
            if (p.Timestamp > nowUnix + ClockSkewSeconds) return Done(r, VerifyStatus.Forged, "TIMESTAMP_IN_FUTURE");
            if (nowUnix > p.ValidUntil) return Done(r, VerifyStatus.Expired, "EXPIRED");

            // 9. już otrzymany
            if (seen.Contains((p.IssuerId, p.Sequence))) return Done(r, VerifyStatus.Duplicate, "ALREADY_RECEIVED");

            // 10–11. obszar użytkownika
            if (!Areas.Covers(p.AreaCode, userArea)) return Done(r, VerifyStatus.VerifiedOtherArea, "OTHER_AREA");
            return Done(r, VerifyStatus.Verified, "OK");
        }

        /// <summary>
        /// Skutki odbioru (PROTOCOL.md §7, ostatni akapit): po VERIFIED / VERIFIED_OTHER_AREA dodaje (issuer, seq) do
        /// <paramref name="seen"/>, a dla KEY_REVOKE dodaje wskazanego wydawcę do <paramref name="revoked"/>.
        /// Zwraca true, gdy zmienił się zbiór unieważnionych (aplikacja musi go wtedy zapisać trwale).
        /// </summary>
        public static bool Commit(VerificationResult result, ISet<int> revoked, ISet<(int issuer, int seq)> seen)
        {
            if (!result.IsAuthentic) return false;
            var p = result.Payload;
            seen.Add((p.IssuerId, p.Sequence));
            if (p.Type == AlertTypes.KeyRevoke && p.Note.Length == 2)
                return revoked.Add(Bytes.ReadU16(p.Note, 0));
            return false;
        }

        /// <summary>ID wydawcy unieważnianego przez komunikat KEY_REVOKE albo -1.</summary>
        public static int RevokedIssuerId(Payload p) =>
            p != null && p.Type == AlertTypes.KeyRevoke && p.Note.Length == 2 ? Bytes.ReadU16(p.Note, 0) : -1;

        public static string StatusName(VerifyStatus s)
        {
            switch (s)
            {
                case VerifyStatus.Verified: return "VERIFIED";
                case VerifyStatus.VerifiedOtherArea: return "VERIFIED_OTHER_AREA";
                case VerifyStatus.Expired: return "EXPIRED";
                case VerifyStatus.Incomplete: return "INCOMPLETE";
                case VerifyStatus.Forged: return "FORGED";
                case VerifyStatus.Duplicate: return "DUPLICATE";
                default: return "MALFORMED";
            }
        }

        /// <summary>Odwrotność <see cref="StatusName"/> (np. status zapisany w skrzynce); nieznany → Malformed.</summary>
        public static VerifyStatus ParseStatus(string name)
        {
            foreach (VerifyStatus s in Enum.GetValues(typeof(VerifyStatus)))
                if (StatusName(s) == name) return s;
            return VerifyStatus.Malformed;
        }

        static VerificationResult Done(VerificationResult r, VerifyStatus status, string reason)
        {
            r.Status = status;
            r.ReasonCode = reason;
            return r;
        }
    }
}
