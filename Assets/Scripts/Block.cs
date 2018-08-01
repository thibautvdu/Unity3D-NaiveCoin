using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class Block : ICloneable {

    public int index { get; private set; }
    public DateTime timestamp { get; private set; }
    public string hash { get; private set; }
    public string previousHash { get; private set; }
    public Transaction[] data { get; private set; }
    public int nonce { get; private set; }
    public int difficulty { get; private set; }

    public static Block CreateGenesisBlock(string nodeAddress, int difficulty)
    {
        Transaction genesisTx = Transaction.CreateCoinbaseTransaction(nodeAddress, 0);

        Block b = new Block(0, DateTime.Now, null, new Transaction[] { genesisTx }, difficulty, 0);
        b.SetHash(b.ComputeHash());

        return b;
    }

    public Block(Block b)
    {
        this.index = b.index;
        this.timestamp = b.timestamp;
        this.previousHash = b.previousHash;
        this.data = b.data;
        this.hash = b.hash;
        this.difficulty = b.difficulty;
        this.nonce = b.nonce;
    }

    public Block(int index, DateTime timestamp, string previousHash, Transaction[] data, int difficulty, int nonce)
    {
        this.index = index;
        this.timestamp = timestamp;
        this.previousHash = previousHash;
        this.data = data;
        this.difficulty = difficulty;
        this.nonce = nonce;
    }

    public string ComputeHash()
    {
        return Cryptography.CalculateHash(index.ToString() + previousHash + timestamp.ToString() + string.Join(",", data.Select(t => t.ToString()).ToArray()) + difficulty.ToString() + nonce.ToString());
    }

    public void SetHash(string hash)
    {
        this.hash = hash;
    }

    public void IncrementNonce()
    {
        nonce++;
    }

    public override string ToString()
    {
        return String.Format("Block #{0} [previous hash : {1}, timestamp : {2}, data : {3}, hash : {4}, difficulty : {5}]", index, previousHash, timestamp, data, hash, difficulty);
    }

    public object Clone()
    {
        return new Block(this);
    }

    public bool IsHashValid()
    {
        if (hash == null) return false;
        string computedHash = ComputeHash();

        return hash.Equals(computedHash);
    }

    public bool IsValidFirstBlock()
    {
        if (index != 0) return false;
        if (previousHash != null) return false;

        return IsHashValid();
    }

    public bool IsValidNewBlock(Block prevBlock)
    {
        if (prevBlock == null) return false;
        if (prevBlock.index + 1 != index) return false;
        if (previousHash == null || previousHash != prevBlock.hash) return false;
        if (!IsTimestampValid(prevBlock)) return false;

        return IsHashValid();
    }

    private bool IsTimestampValid(Block prevBlock)
    {
        return prevBlock.timestamp < timestamp.AddSeconds(60) && timestamp < DateTime.Now.AddSeconds(60);
    }

    public bool AreTransactionsValid(Transaction.UnspentTxOut[] unspentTxOuts)
    {
        Transaction coinBaseTx = data[0];
        if (!coinBaseTx.IsAValidCoinBaseTx(index))
        {
            return false;
        }

        // check for duplicates txIns : each txIn should be only included once
        IEnumerable<Transaction.TxIn> allTxIns = data.SelectMany(t => t.txIns);
        if (allTxIns.Any(ti => allTxIns.Count(ti2 => ti2.txOutTxId == ti.txOutTxId && ti2.txOutIndex == ti.txOutIndex) > 1))
        {
            Debug.LogError("Contains duplicate txIns");
            return false;
        }

        for (int i = 1; i < data.Length; i++)
        {
            if (!data[i].IsValid(unspentTxOuts)) return false;
        }

        return true;
    }

    public Transaction.UnspentTxOut[] ProcessTransactions(Transaction.UnspentTxOut[] unspentTxOuts)
    {
        if (!AreTransactionsValid(unspentTxOuts))
        {
            Debug.LogError(" Invalid block transactions");
            return null;
        }

        return UpdateUnspentTxOuts(unspentTxOuts);
    }

    private Transaction.UnspentTxOut[] UpdateUnspentTxOuts(Transaction.UnspentTxOut[] unspentTxOuts)
    {
        List<Transaction.UnspentTxOut> newUnspentTxOuts = data.Select(
            t => t.txOuts.Select((txo, index)
                 => new Transaction.UnspentTxOut(t.id, index, txo.address, txo.amount)
            )).SelectMany(uto => uto).ToList();

        List<Transaction.TxIn> allTxIns = data.SelectMany(t => t.txIns).ToList();

        List<Transaction.UnspentTxOut> updatedUnspentTxOuts = unspentTxOuts.Where(txo => !allTxIns.Any(txi => txi.txOutTxId == txo.txId && txi.txOutIndex == txo.txOutIndex)).ToList();
        updatedUnspentTxOuts.AddRange(newUnspentTxOuts);

        return updatedUnspentTxOuts.ToArray();
    }
}
