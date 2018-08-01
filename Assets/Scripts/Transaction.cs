using System.Text;
using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using System;
using System.Security.Cryptography;

public class Transaction : ICloneable {
    public static readonly int COINBASE_AMOUNT = 50;

    // Immutable
    public class UnspentTxOut
    {
        public readonly string txId;
        public readonly int txOutIndex;
        public readonly string address;
        public readonly int amount;

        public UnspentTxOut(string txId, int txOutIndex, string address, int amount)
        {
            this.txId = txId;
            this.txOutIndex = txOutIndex;
            this.address = address;
            this.amount = amount;
        }
    }

    public class TxIn : ICloneable
    {
        public readonly string txOutTxId;
        public readonly int txOutIndex;
        public string signature { get; private set; }

        public TxIn(string txId, int index)
        {
            txOutIndex = index;
            txOutTxId = txId;
            signature = "";
        }

        public TxIn(TxIn other)
        {
            txOutTxId = other.txOutTxId;
            txOutIndex = other.txOutIndex;
            signature = other.signature;
        }

        public object Clone()
        {
            return new TxIn(this);
        }

        public UnspentTxOut GetCorrespondingUnspentTxOut(IEnumerable<UnspentTxOut> unspentTxOuts)
        {
            return unspentTxOuts.FirstOrDefault(uto => uto.txOutIndex == txOutIndex && uto.txId == txOutTxId);
        }

        public int GetAmount(IEnumerable<UnspentTxOut> unspentTxOuts)
        {
            UnspentTxOut correspondingUnspentTxOut = GetCorrespondingUnspentTxOut(unspentTxOuts);

            if (correspondingUnspentTxOut == null) throw new Exception("tried to get amount of a spent or inexisting tx out");

            return correspondingUnspentTxOut.amount;
        }

        public bool IsValid(string txId, IEnumerable<UnspentTxOut> unspentTxOuts)
        {
            UnspentTxOut referencedUnspentTxOut = unspentTxOuts.FirstOrDefault(uto => uto.txId == txOutTxId & uto.txOutIndex == txOutIndex);

            if (referencedUnspentTxOut == null)
            {
                Debug.LogError("Referenced TxOut not found : " + ToString());
                return false;
            }

            string address = referencedUnspentTxOut.address;
            return Cryptography.VerifySignature(signature, txId, address);
        }

        public void ComputeAndSetSignature(string txId, CspParameters privateKeyCointainer, IEnumerable<UnspentTxOut> unspentTxOuts)
        {
            signature = ComputeSignature(txId, privateKeyCointainer, unspentTxOuts);
        }

        public string ComputeSignature(string txId, CspParameters privateKeyCointainer, IEnumerable<UnspentTxOut> unspentTxOuts)
        {
            UnspentTxOut referencedUnspentTxOut = GetCorrespondingUnspentTxOut(unspentTxOuts);

            if (referencedUnspentTxOut == null)
            {
                Debug.LogError("could not find referenced txOut for txIn");
                return null;
            }

            string referencedAddress = referencedUnspentTxOut.address;

            if (Cryptography.GetPublicKey(privateKeyCointainer) != referencedAddress)
            {
                Debug.LogError("trying to sign an input with private key that doesn't match the address of txIn's unspent txOut");
                return null;
            }

            return Cryptography.SignHashWithPrivateKey(privateKeyCointainer, txId);
        }
    }

    // Immutable
    public class TxOut
    {
        public readonly string address;
        public readonly int amount;

        public TxOut(string address, int amount)
        {
            this.address = address;
            this.amount = amount;
        }
    }

    public string id;
    public TxIn[] txIns;
    public TxOut[] txOuts;

    public static Transaction CreateCoinbaseTransaction(string address, int blockIndex)
    {
        Transaction t = new Transaction();
        TxIn txIn = new TxIn("", blockIndex);

        t.txIns = new TxIn[] { txIn };
        t.txOuts = new TxOut[] { new TxOut(address,COINBASE_AMOUNT) };
        t.ComputeAndSetId();

        return t;
    }

    public static Transaction CreateTransaction(string senderAddress, string receiverAddress, CspParameters privateKeyCointainer, int amount, IEnumerable<UnspentTxOut> unspentTxOuts, TransactionPool pool)
    {
        List<UnspentTxOut> myUnspentTxOuts = unspentTxOuts.Where(uto => uto.address == senderAddress).ToList();

        // Remove unspent tx outs already used in any unverified transaction of the pool
        List<TxIn> poolTxIns = pool.GetTransactions().SelectMany(t => t.txIns).ToList();
        myUnspentTxOuts = myUnspentTxOuts.Where(uto => !poolTxIns.Any(ti => ti.txOutTxId == uto.txId && ti.txOutIndex == uto.txOutIndex)).ToList();

        // Find unspent tx outs to fund the transaction
        List<Transaction.UnspentTxOut> unspentTxOutsToUse = new List<Transaction.UnspentTxOut>();
        int currentAmount = 0;
        for (int i = 0; i < myUnspentTxOuts.Count() && currentAmount < amount; i++)
        {
            unspentTxOutsToUse.Add(myUnspentTxOuts.ElementAt(i));
            currentAmount += myUnspentTxOuts.ElementAt(i).amount;
        }

        if (currentAmount < amount)
        {
            Debug.LogError("Insufficiant balance");
            return null;
        }

        int leftOver = currentAmount - amount;

        TxIn[] txIns = unspentTxOutsToUse.Select(uto => new TxIn(uto.txId, uto.txOutIndex)).ToArray();

        // Create corresponding tx outs (with eventual leftover sent back to sender
        TxOut[] txOuts;
        if (leftOver == 0)
            txOuts = new TxOut[] { new TxOut(receiverAddress, amount) };
        else
            txOuts = new TxOut[] { new TxOut(receiverAddress, amount), new TxOut(senderAddress, leftOver) };

        Transaction transaction = new Transaction();
        transaction.txIns = txIns;
        transaction.txOuts = txOuts;
        transaction.ComputeAndSetId();
        transaction.SignTxIns(privateKeyCointainer, unspentTxOuts);

        return transaction;
    }

    public Transaction() { }

    public Transaction(Transaction other)
    {
        id = other.id;
        txIns = other.txIns.Select(ti => (TxIn)ti.Clone()).ToArray();
        txOuts = (TxOut[])other.txOuts.Clone(); // TxOut is immutable
    }

    public object Clone()
    {
        return new Transaction(this);
    }

    public override string ToString()
    {
        StringBuilder content = new StringBuilder();
        content.Append(id);
        foreach (var ti in txIns) content.Append(ti.txOutTxId + ti.txOutIndex + ti.signature);
        foreach (var to in txOuts) content.Append(to.address + to.amount);

        return content.ToString();
    }

    public bool IsValid(IEnumerable<UnspentTxOut> unspentTxOuts)
    {
        if (ComputeId() != id)
        {
            Debug.LogError("Invalid tx id: " + id);
            return false;
        }

        if (txIns.Any(ti => !ti.IsValid(id, unspentTxOuts)))
        {
            Debug.LogError("some of the txIns are invalid in tx: " + id);
            return false;
        }

        int totalTxInValue = txIns.Select(ti => ti.GetAmount(unspentTxOuts)).Sum();
        int totalTxOutValue = txOuts.Select(to => to.amount).Sum();


        if (totalTxInValue != totalTxOutValue)
        {
            Debug.LogError("totalTxOutValue != totalTxInValue in tx: " + id);
            return false;
        }

        return true;
    }

    public bool IsAValidCoinBaseTx(int blockIndex)
    {
        if (ComputeId() != id)
        {
            Debug.LogError("Invalid tx id: " + id);
            return false;
        }

        if (txIns.Length != 1)
        {
            Debug.LogError("One and only one txIn must be specified in the coinbase transaction");
            return false;
        }

        if (txOuts.Length != 1)
        {
            Debug.LogError("One and only one txOut must be specified in the coinbase transaction");
            return false;
        }

        if (txIns[0].txOutIndex != blockIndex)
        {
            Debug.LogError("The txIn's txOutIndex in coinbase transaction must be the block height");
            return false;
        }

        if (txOuts[0].amount != COINBASE_AMOUNT)
        {
            Debug.LogError("Invalid reward amount in reward transaction");
            return false;
        }

        return true;
    }

    public void ComputeAndSetId()
    {
        id = ComputeId();
    }

    public string ComputeId()
    {
        StringBuilder content = new StringBuilder();
        foreach (var ti in txIns) content.Append(ti.txOutTxId + ti.txOutIndex);
        foreach (var to in txOuts) content.Append(to.address + to.amount);

        return Cryptography.CalculateHash(content.ToString());
    }

    public void SignTxIns(CspParameters privateKeyCointainer, IEnumerable<UnspentTxOut> unspentTxOuts)
    {
        for (int i = 0; i < txIns.Length; i++) txIns[i].ComputeAndSetSignature(id, privateKeyCointainer, unspentTxOuts);
    }
}
