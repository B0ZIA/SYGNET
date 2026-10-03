using System;
using System.Collections.Generic;

namespace Sygnet.Core
{
    /// <summary>
    /// Zaufani wydawcy. Każdy certyfikat jest weryfikowany WBUDOWANYM kluczem ROOT; nazwa i zakres wydawcy
    /// pochodzą wyłącznie z podpisanych bajtów certyfikatu (PROTOCOL.md §5.4).
    /// </summary>
    public sealed class TrustStore
    {
        public const int RootId = 0;
        public const string RootName = "ROOT – Klucz główny SYGNET";

        readonly Dictionary<int, IssuerCert> issuers = new Dictionary<int, IssuerCert>();
        readonly List<string> rejected = new List<string>();

        public byte[] RootPublicKey { get; }
        public string RootFingerprint { get; }

        /// <summary>Odcisk ROOT zadeklarowany w pliku JSON (informacyjnie; liczy się klucz wbudowany).</summary>
        public string DeclaredRootFingerprint { get; private set; }

        public IReadOnlyDictionary<int, IssuerCert> Issuers => issuers;

        /// <summary>Opisy odrzuconych certyfikatów – aplikacja je loguje.</summary>
        public IReadOnlyList<string> Rejected => rejected;

        public TrustStore(byte[] rootPublicKey)
        {
            if (rootPublicKey == null || rootPublicKey.Length != Ed25519.PublicKeySize)
                throw new ArgumentException("Klucz ROOT musi mieć 32 B");
            RootPublicKey = rootPublicKey;
            RootFingerprint = Ed25519.Fingerprint(rootPublicKey);
        }

        /// <summary>Dodaje certyfikat, jeśli podpis ROOT jest poprawny. Zwraca false i loguje powód w <see cref="Rejected"/>.</summary>
        public bool TryAdd(byte[] cert, byte[] rootSig)
        {
            if (!Ed25519.Verify(RootPublicKey, cert, rootSig))
                return Reject("zły podpis ROOT");

            IssuerCert c;
            try
            {
                c = IssuerCert.Parse(cert);
            }
            catch (FormatException e)
            {
                return Reject("niepoprawny format: " + e.Message);
            }

            if (c.CertVersion != IssuerCert.CurrentVersion)
                return Reject("nieobsługiwana wersja certyfikatu " + c.CertVersion, c.IssuerId);
            if (c.IssuerId == RootId)
                return Reject("certyfikat dla ROOT (ROOT jest wbudowany)", c.IssuerId);
            if (issuers.ContainsKey(c.IssuerId))
                return Reject("zduplikowany wydawca", c.IssuerId);

            issuers[c.IssuerId] = c;
            return true;
        }

        public bool TryGet(int issuerId, out IssuerCert cert) => issuers.TryGetValue(issuerId, out cert);

        /// <summary>Nazwa z certyfikatu (albo ROOT); null dla nieznanego wydawcy.</summary>
        public string NameOf(int issuerId)
        {
            if (issuerId == RootId) return RootName;
            return issuers.TryGetValue(issuerId, out var c) ? c.Name : null;
        }

        public string FingerprintOf(int issuerId)
        {
            if (issuerId == RootId) return RootFingerprint;
            return issuers.TryGetValue(issuerId, out var c) ? c.Fingerprint : null;
        }

        /// <summary>
        /// Ładuje <c>trust_store.json</c>: { "version": 1, "root_fingerprint": "...", "issuers": [ { "cert_b64", "root_sig_b64" } ] }.
        /// Rzuca <see cref="FormatException"/> tylko dla zepsutej struktury pliku; złe certyfikaty trafiają do <see cref="Rejected"/>.
        /// </summary>
        public static TrustStore FromJson(string json, byte[] rootPublicKey)
        {
            var store = new TrustStore(rootPublicKey);
            if (!(MiniJson.Parse(json) is Dictionary<string, object> root))
                throw new FormatException("trust_store: oczekiwano obiektu JSON");
            if (!(root.TryGetValue("version", out var v) && v is long ver && ver == 1))
                throw new FormatException("trust_store: nieobsługiwana wersja");
            if (root.TryGetValue("root_fingerprint", out var fp)) store.DeclaredRootFingerprint = fp as string;
            if (store.DeclaredRootFingerprint != null && store.DeclaredRootFingerprint != store.RootFingerprint)
                store.rejected.Add("Odcisk ROOT w pliku (" + store.DeclaredRootFingerprint +
                                   ") różni się od wbudowanego (" + store.RootFingerprint + ")");
            if (!(root.TryGetValue("issuers", out var list) && list is List<object> items))
                throw new FormatException("trust_store: brak listy issuers");

            for (int i = 0; i < items.Count; i++)
            {
                byte[] cert, sig;
                try
                {
                    var e = (Dictionary<string, object>)items[i];
                    cert = Convert.FromBase64String((string)e["cert_b64"]);
                    sig = Convert.FromBase64String((string)e["root_sig_b64"]);
                }
                catch (Exception ex) when (ex is InvalidCastException || ex is KeyNotFoundException ||
                                           ex is FormatException || ex is ArgumentNullException)
                {
                    store.rejected.Add("issuers[" + i + "]: niepoprawny wpis JSON");
                    continue;
                }
                if (!store.TryAdd(cert, sig))
                    store.rejected[store.rejected.Count - 1] = "issuers[" + i + "]: " + store.rejected[store.rejected.Count - 1];
            }
            return store;
        }

        bool Reject(string why, int issuerId = -1)
        {
            rejected.Add(issuerId >= 0 ? "wydawca " + issuerId + ": " + why : why);
            return false;
        }
    }
}
