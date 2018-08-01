using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEngine;

public class Wallet {
    public string PublicKey { get; private set; }
    public CspParameters PrivateKeyCointainer { get; private set; }

    public Wallet(int id)
    {
        PrivateKeyCointainer = new CspParameters();
        PrivateKeyCointainer.KeyContainerName = "private_key" + id;
        InitWallet();
    }

    public void InitWallet()
    {
        PublicKey = Cryptography.GetPublicKey(PrivateKeyCointainer);
    }

    public int GetBalance(IEnumerable<Transaction.UnspentTxOut> unspentTxOuts)
    {
        return unspentTxOuts.Where(uto => uto.address == PublicKey).Sum(uto => uto.amount);
    }
}
