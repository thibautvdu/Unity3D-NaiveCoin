using System.Security.Cryptography;


/// <summary>
/// Minimalist wallet using CspParameters to hide the private key
/// each node got one and can retrieve it from one session to another through an unique id
/// </summary>
public class Wallet {
    public string PublicKey { get; private set; }
    public CspParameters PrivateKeyCointainer { get; private set; }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="id">unique id to retrieve or create a private key container</param>
    public Wallet(int id)
    {
        PrivateKeyCointainer = new CspParameters();
        PrivateKeyCointainer.KeyContainerName = "private_key" + id;
        PublicKey = Cryptography.GetPublicKey(PrivateKeyCointainer);
    }
}
