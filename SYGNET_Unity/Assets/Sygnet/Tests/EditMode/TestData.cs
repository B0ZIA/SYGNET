using System.Collections.Generic;
using System.IO;
using System.Linq;
using Sygnet.Core;
using UnityEngine;

namespace Sygnet.Tests
{
    /// <summary>Wektory testowe z Data/testvectors.json (kopia testvectors/ z katalogu głównego repo).</summary>
    public static class TestData
    {
        public static readonly string Dir = Path.Combine(Application.dataPath, "Sygnet", "Tests", "EditMode", "Data");

        static Dictionary<string, object> json;

        public static Dictionary<string, object> Json =>
            json ??= (Dictionary<string, object>)MiniJson.Parse(File.ReadAllText(Path.Combine(Dir, "testvectors.json")));

        public static long NowForTests => (long)Json["now_for_tests"];
        public static long NowExpired => (long)Json["now_expired"];
        public static int UserArea => (int)(long)Json["user_area"];
        public static byte[] RootPub => Bytes.FromHex((string)Json["root_pub_hex"]);
        public static string RootFingerprint => (string)Json["root_fingerprint"];

        public static byte[] Seed(string key) => Bytes.FromHex((string)((Dictionary<string, object>)Json["seeds_hex"])[key]);

        public static byte[] IssuerPub(int id) =>
            Bytes.FromHex((string)((Dictionary<string, object>)Json["issuer_pub_hex"])[id.ToString()]);

        public static Dictionary<string, object> TrustStoreJson => (Dictionary<string, object>)Json["trust_store"];

        public static List<(byte[] cert, byte[] sig)> TrustStoreEntries =>
            ((List<object>)TrustStoreJson["issuers"]).Cast<Dictionary<string, object>>()
            .Select(e => (System.Convert.FromBase64String((string)e["cert_b64"]),
                System.Convert.FromBase64String((string)e["root_sig_b64"])))
            .ToList();

        static TrustStore trust;

        /// <summary>Trust store z wektorów, zbudowany przez <see cref="TrustStore.FromJson"/> z kluczem ROOT z wektorów.</summary>
        public static TrustStore Trust => trust ??= TrustStore.FromJson(ToJson(TrustStoreEntries), RootPub);

        public static IEnumerable<string> VectorNames => ((Dictionary<string, object>)Json["vectors"]).Keys;

        public static Vector Get(string name) => new Vector(name, (Dictionary<string, object>)((Dictionary<string, object>)Json["vectors"])[name]);

        /// <summary>Składa JSON trust store'u (testuje też ścieżkę FromJson).</summary>
        public static string ToJson(IEnumerable<(byte[] cert, byte[] sig)> entries) =>
            "{\"version\":1,\"root_fingerprint\":\"" + RootFingerprint + "\",\"issuers\":[" +
            string.Join(",", entries.Select(e =>
                "{\"cert_b64\":\"" + System.Convert.ToBase64String(e.cert) + "\",\"root_sig_b64\":\"" +
                System.Convert.ToBase64String(e.sig) + "\"}")) +
            "]}";

        public sealed class Vector
        {
            public readonly string Name;
            public readonly byte[] Payload;
            public readonly byte[] Frame;
            public readonly int FrameLen;
            public readonly string QrText;
            public readonly string ExpectedStatus;
            public readonly string Reason;
            public readonly string ExpectedStatus3DaysLater;
            public readonly string Wav;

            public Vector(string name, Dictionary<string, object> d)
            {
                Name = name;
                Payload = Bytes.FromHex((string)d["payload_hex"]);
                Frame = Bytes.FromHex((string)d["frame_hex"]);
                FrameLen = (int)(long)d["frame_len"];
                QrText = (string)d["qr_text"];
                ExpectedStatus = (string)d["expected_status"];
                Reason = (string)d["reason"];
                ExpectedStatus3DaysLater = (string)d["expected_status_3_days_later"];
                Wav = (string)d["wav"];
            }

            public override string ToString() => Name;
        }
    }
}
