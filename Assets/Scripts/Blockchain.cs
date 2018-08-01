using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

public class Blockchain {

    public static int GetAccumulatedDifficulty(List<Block> chain)
    {
        return (int)chain.Select(b => Math.Pow(2,b.difficulty)).Sum();
    }

    public TransactionPool transactionPool { get; private set; }

    // difficulty consensus constants
    const int BLOCK_GENERATION_INTERVAL = 10;
    const int DIFFICULTY_ADJUSTMENT_INTERVAL = 10;

    private int difficulty;
    private List<Block> blocks = new List<Block>();
    private Transaction.UnspentTxOut[] unspentTxOuts = new Transaction.UnspentTxOut[] { };

    private DateTime genesis = DateTime.Now;

    public Blockchain(string nodeAddress, int difficulty)
    {
        this.difficulty = difficulty;
        Block genesisBlock = Block.CreateGenesisBlock(nodeAddress, difficulty);
        unspentTxOuts = genesisBlock.ProcessTransactions(unspentTxOuts);
        blocks.Add(genesisBlock);
        transactionPool = new TransactionPool();
    }

    public override string ToString()
    {
        StringBuilder builder = new StringBuilder();

        foreach (Block b in blocks)
        {
            builder.Append(b.ToString()).Append("\n");
        }

        return builder.ToString();
    }

    public Block LatestBlock()
    {
        return blocks.Last();
    }

    public Transaction.UnspentTxOut[] GetCurrentUnspentTxOuts()
    {
        return (Transaction.UnspentTxOut[])unspentTxOuts.Clone(); // UnspentTxOut are immutable
    }

    /// <summary>
    /// Returning a clone to emulate the copy over the websocket (nodes should be isolated from each other)
    /// </summary>
    /// <returns></returns>
    public List<Block> GetCloneChain()
    {
        return blocks.Select(b => (Block)b.Clone()).ToList();
    }

    public bool IsValidChain()
    {
        return ValidateChain(blocks) != null;
    }

    public bool SendTransaction(string senderAddress, string receiverAddress, CspParameters privateKeyCointainer, int amount)
    {
        Transaction tx = Transaction.CreateTransaction(senderAddress, receiverAddress, privateKeyCointainer, amount, unspentTxOuts, transactionPool);
        if (tx == null) return false;

        return AddToPool(tx);
    }

    public bool AddToPool(Transaction tx)
    {
       return transactionPool.Add(tx, unspentTxOuts);
    }

    public IEnumerator GenerateNextblock(string senderAddress)
    {
        List<Transaction> blockData = new List<Transaction>();
        Transaction coinBaseTx = Transaction.CreateCoinbaseTransaction(senderAddress, LatestBlock().index + 1);
        blockData.Add(coinBaseTx);
        blockData.AddRange(transactionPool.GetTransactions());

        return GenerateRawNextblock(blockData);
    }

    public IEnumerator GenerateRawNextblock(IEnumerable<Transaction> data)
    {
        Block prev = LatestBlock();
        int difficulty = GetDifficulty();
        int nextIndex = prev.index + 1;
        DateTime nextTimestamp = DateTime.Now;

        int nonce = 0;
        string hash = null;

        Block newBlock = new Block(nextIndex, nextTimestamp, prev.hash, data.ToArray(), difficulty, nonce);

        do
        {
            if (nonce % 25 == 0) yield return null;
            newBlock.IncrementNonce();
            hash = newBlock.ComputeHash();

        } while (!Cryptography.HashMatchesDifficulty(hash, difficulty));

        newBlock.SetHash(hash);

        AddBlock(newBlock);

        yield return null;
    }

    public void AddBlock(Block b)
    {
        if (b == null) return;
        if (!b.IsValidNewBlock(LatestBlock())) return;
        Transaction.UnspentTxOut[] updatedUnspentTxOuts = b.ProcessTransactions(unspentTxOuts);
        if (updatedUnspentTxOuts == null) return;

        blocks.Add(b);

        unspentTxOuts = updatedUnspentTxOuts;
        transactionPool.Update(unspentTxOuts);
    }

    public int GetAccumulatedDifficulty()
    {
        return GetAccumulatedDifficulty(blocks);
    }

    public void ReplaceChain(List<Block> newBlocks)
    {
        Transaction.UnspentTxOut[] newUnspentTxOuts = ValidateChain(newBlocks);

        if (newUnspentTxOuts == null) return;
        if (GetAccumulatedDifficulty(newBlocks) <= GetAccumulatedDifficulty(blocks)) return;

        Debug.Log("Received blockchain is valid. Replacing current blockchain with received blockchain");

        blocks = newBlocks;
        unspentTxOuts = newUnspentTxOuts;
        transactionPool.Update(unspentTxOuts);
    }

    private int GetDifficulty()
    {
        Block last = LatestBlock();

        if (last.index % DIFFICULTY_ADJUSTMENT_INTERVAL != 0 || last.index == 0) return last.difficulty;

        Block previousAdjustementBlock = blocks[blocks.Count - DIFFICULTY_ADJUSTMENT_INTERVAL];
        int timeExpected = BLOCK_GENERATION_INTERVAL * DIFFICULTY_ADJUSTMENT_INTERVAL;
        int timeTaken = (int)((last.timestamp - previousAdjustementBlock.timestamp).TotalSeconds);

        if (timeTaken < timeExpected / 2) return previousAdjustementBlock.difficulty + 1;
        else if (timeTaken > timeExpected * 2) return previousAdjustementBlock.difficulty - 1;
        else return last.difficulty;
    }

    private bool IsFirstBlockValid(List<Block> chain)
    {
        Block b = chain.First();
        if (b == null) return false;

        return b.IsValidFirstBlock();
    }

    private Transaction.UnspentTxOut[] ValidateChain(List<Block> chain)
    {
        if (!IsFirstBlockValid(chain))
        {
            Debug.LogWarning("First block is not valid");
            return null;
        }

        Transaction.UnspentTxOut[] chainUnspentTxOuts = new Transaction.UnspentTxOut[] { };
        for (int i = 0; i < chain.Count; i++)
        {
            Block block = chain[i];

            if (i != 0 && !block.IsValidNewBlock(chain[i - 1]))
            {
                Debug.LogWarning("Block #" + block.index + " is invalid");
                return null;
            }

            chainUnspentTxOuts = block.ProcessTransactions(chainUnspentTxOuts);

            if(chainUnspentTxOuts == null)
            {
                Debug.LogError("Invalid transactions in blockchain");
                return null;
            }
        }

        return chainUnspentTxOuts;
    }
}