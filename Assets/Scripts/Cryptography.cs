using System;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

public static class Cryptography {
    private static UnicodeEncoding encoder = new UnicodeEncoding();

    public static string CalculateHash(string s)
    {
        if (s == null) return null;

        HashAlgorithm hasher = new SHA256Managed();

        byte[] bytes = hasher.ComputeHash(Encoding.ASCII.GetBytes(s));

        string hash = BitConverter.ToString(bytes).Replace("-", "");

        return hash;
    }

    public static bool HashMatchesDifficulty(string hash, int difficulty)
    {
        return hash.Substring(0, difficulty).Equals(new string('0', difficulty));
    }

    public static void GeneratePrivateKey(string containerId)
    {
        CspParameters cp = new CspParameters();
        cp.KeyContainerName = containerId;
        var provider = new RSACryptoServiceProvider(cp);
    }

    public static string SignHashWithPrivateKey(CspParameters pkCointainer, string data)
    {
        using (var provider = new RSACryptoServiceProvider(pkCointainer))
        {
            byte[] dataBytes = encoder.GetBytes(data);
            byte[] signedBytes = provider.SignData(dataBytes, new SHA1CryptoServiceProvider());

            return encoder.GetString(signedBytes);
        }
    }

    public static string GetPublicKey(CspParameters pkCointainer)
    {
        using (var provider = new RSACryptoServiceProvider(pkCointainer))
        {

            return provider.ToXmlString(false);
        }
    }

    public static bool VerifySignature(string signature, string data, string publicKeyXml)
    {
        using (var provider = new RSACryptoServiceProvider())
        {
            provider.FromXmlString(publicKeyXml);
            return provider.VerifyData(encoder.GetBytes(data), new SHA1CryptoServiceProvider(), encoder.GetBytes(signature));
        }
    }
}
