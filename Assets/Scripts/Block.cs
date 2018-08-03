using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// MonoBehaviour wrapping block data, operations and animations
/// </summary>
public class Block : MonoBehaviour, ICloneable {

    // block raw data
    public int index { get; private set; }
    public DateTime timestamp { get; private set; }
    public string hash { get; private set; }
    public string previousHash { get; private set; }
    public Transaction[] data { get; private set; }
    public int nonce { get; private set; }
    public int difficulty { get; private set; }

    // game object components/animations
    [SerializeField]
    private SpriteRenderer blockSprite;
    [SerializeField]
    private Canvas animatedHashPrefab;

    void Start()
    {
        Color c = blockSprite.color;
        c.a = 1;
        blockSprite.color = c;
    }

    public void SetColor(Color c)
    {
        blockSprite.color = c;
    }

    /// <summary>
    /// Init the block's state as the genesis block
    /// </summary>
    /// <param name="nodeAddress">node address (wallet's public key)</param>
    /// <param name="difficulty">starting difficulty</param>
    public void InitAsGenesis(string nodeAddress, int difficulty)
    {
        Transaction genesisTx = Transaction.CreateCoinbaseTransaction(nodeAddress, 0);

        Init(0, DateTime.Now, null, new Transaction[] { genesisTx }, difficulty);
        SetHash(ComputeHash());
    }

    /// <summary>
    /// Init the block's state
    /// </summary>
    /// <param name="index">index of the block in the chain</param>
    /// <param name="timestamp">block creation time</param>
    /// <param name="previousHash">hash of the previous block in the chain</param>
    /// <param name="data">block's data (transactions)</param>
    /// <param name="difficulty">current network difficulty</param>
    public void Init(int index, DateTime timestamp, string previousHash, Transaction[] data, int difficulty)
    {
        this.index = index;
        this.timestamp = timestamp;
        this.previousHash = previousHash;
        this.data = data;
        this.difficulty = difficulty;
        this.nonce = 0;
    }

    /// <summary>
    /// Clone (instantiate) the attached game object and script's state
    /// </summary>
    /// <returns></returns>
    public object Clone()
    {
        Block res = Instantiate(this);

        res.index = index;
        res.timestamp = timestamp;
        res.hash = hash;
        res.previousHash = previousHash;
        res.data = data;
        res.difficulty = difficulty;
        res.nonce = nonce;

        return res;
    }

    public override string ToString()
    {
        return String.Format("Block #{0} [previous hash : {1}, timestamp : {2}, data : {3}, hash : {4}, difficulty : {5}]", index, previousHash, timestamp, data, hash, difficulty);
    }

    /// <summary>
    /// Mine the block with unity animations
    /// </summary>
    /// <param name="onMined">callback upon block successfuly found</param>
    /// <returns></returns>
    public IEnumerator MineBlock(Node.Callback onMined)
    {
        Canvas animatedHash;
        Color plainColor = blockSprite.color;
        Color fadedColor = new Color(plainColor.r, plainColor.g, plainColor.b, 0.2f);
        do
        {
            yield return new WaitForSeconds(1 / 20f); // limit the mining speed to make the animation more readable
            nonce++;
            hash = ComputeHash();
            blockSprite.color = Color.Lerp(plainColor, fadedColor, Time.time % 1);
            animatedHash = Instantiate(animatedHashPrefab, transform, false);
            animatedHash.GetComponentInChildren<Text>().text = hash;
        } while (!Cryptography.HashMatchesDifficulty(hash, difficulty));

        animatedHash.GetComponent<Animator>().SetTrigger("MatchDifficulty");
        animatedHash.GetComponentInChildren<Text>().color = Color.green;

        blockSprite.color = plainColor;

        onMined();
    }

    public string ComputeHash()
    {
        return Cryptography.CalculateHash(index.ToString() + previousHash + timestamp.ToString() + string.Join(",", data.Select(t => t.ToString()).ToArray()) + difficulty.ToString() + nonce.ToString());
    }

    public void SetHash(string hash)
    {
        this.hash = hash;
    }

    public bool IsValidGenesis()
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

    public bool IsHashValid()
    {
        if (hash == null) return false;
        string computedHash = ComputeHash();

        return hash.Equals(computedHash);
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

    public bool IsTimestampValid(Block prevBlock)
    {
        return prevBlock.timestamp < timestamp.AddSeconds(60) && timestamp < DateTime.Now.AddSeconds(60);
    }

    /// <summary>
    /// Process the transactions on the current unspent tx outs (~balances) of the chain
    /// and return its updated version
    /// </summary>
    /// <param name="unspentTxOuts">current unspent tx outs of the chain</param>
    /// <returns>Updated unspent tx outs after processing the transactions</returns>
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
