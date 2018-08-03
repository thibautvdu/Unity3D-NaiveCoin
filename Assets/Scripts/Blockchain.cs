using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

/// <summary>
/// MonoBehaviour wrapping blockchain data, operations as well
/// as rendering in the scene
/// </summary>
public class Blockchain : MonoBehaviour {
    // difficulty consensus constants
    const int BLOCK_GENERATION_INTERVAL = 10;
    const int DIFFICULTY_ADJUSTMENT_INTERVAL = 10;

    // blockchain's content
    private List<Block> blocks = new List<Block>();
    private Transaction.UnspentTxOut[] unspentTxOuts = new Transaction.UnspentTxOut[] { };
    private TransactionPool transactionPool;

    // mining related variables
    private Block currentlyMining;
    private IEnumerator miningCoroutine;

    [SerializeField]
    private Block blockPrefab;
    private Color chainColor;

    public static int GetAccumulatedDifficulty(List<Block> chain)
    {
        return (int)chain.Select(b => Math.Pow(2, b.difficulty)).Sum();
    }

    /// <summary>
    /// Init the blockchain with a given difficulty and set to use a given public key for the genesis
    /// Note : if a blockchain if one or more block already exists on the network, the genesis will be overwriten with it
    /// </summary>
    /// <param name="nodeAddress">the public key of the node for the genesis block</param>
    /// <param name="difficulty">the starting difficulty of the chain</param>
    public void Init(string nodeAddress, int difficulty, Color chainColor)
    {
        this.chainColor = chainColor;

        Block genesisBlock = Instantiate(blockPrefab);
        genesisBlock.SetColor(chainColor);
        genesisBlock.InitAsGenesis(nodeAddress, difficulty);

        unspentTxOuts = genesisBlock.ProcessTransactions(unspentTxOuts);
        blocks.Add(genesisBlock);

        PlaceBlock(genesisBlock);

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

    public Block LastBlock ()
    {
        return blocks.Last();
    }

    public Transaction[] GetPooledTransactions()
    {
        return transactionPool.GetTransactions();
    }

    /// <summary>
    /// Returning a clone to emulate the copy over the websocket (nodes should be isolated from each other)
    /// Instantiate Unity gameobject
    /// </summary>
    /// <returns>Block instantiated in the scene</returns>
    public Block CloneLatestBlock()
    {
        return (Block)blocks.Last().Clone();
    }

    /// <summary>
    /// Returning a clone to emulate the copy over the websocket (nodes should be isolated from each other)
    /// Instantiate Unity gameobjects
    /// </summary>
    /// <returns>Blocks instantiated in the scene</returns>
    public List<Block> CloneChain()
    {
        List<Block> clone = new List<Block>();

        foreach(Block b in blocks)
        {
            clone.Add((Block)b.Clone());
        }

        return clone;
    }

    public int GetBalance(string publicKey)
    {
        return unspentTxOuts.Where(uto => uto.address == publicKey).Sum(uto => uto.amount);
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

    public void GenerateNextblock(string senderAddress, Node.Callback callback)
    {
        List<Transaction> blockData = new List<Transaction>();
        Transaction coinBaseTx = Transaction.CreateCoinbaseTransaction(senderAddress, blocks.Last().index + 1);
        blockData.Add(coinBaseTx);
        blockData.AddRange(transactionPool.GetTransactions());

        GenerateRawNextblock(blockData, callback);
    }

    public void GenerateRawNextblock(IEnumerable<Transaction> data, Node.Callback callback)
    {
        Block prev = blocks.Last();
        int difficulty = GetUpdatedDifficulty();
        int nextIndex = prev.index + 1;
        DateTime nextTimestamp = DateTime.Now;

        currentlyMining = Instantiate(blockPrefab);
        currentlyMining.SetColor(chainColor);
        currentlyMining.Init(nextIndex, nextTimestamp, prev.hash, data.ToArray(), difficulty);
        PlaceBlock(currentlyMining);

        miningCoroutine = currentlyMining.MineBlock(() =>
        {
            AddBlock(currentlyMining);
            currentlyMining = null;
            callback();
        });

        StartCoroutine(miningCoroutine);
    }

    public void StopMining()
    {
        if(miningCoroutine != null)
        {
            StopCoroutine(miningCoroutine);
            miningCoroutine = null;
        }

        if(currentlyMining != null) Destroy(currentlyMining.gameObject);
    }

    public void AddBlock(Block b)
    {
        if (b == null) return;

        if (!b.IsValidNewBlock(blocks.Last()))
        {
            Destroy(b.gameObject);
            return;
        }

        Transaction.UnspentTxOut[] updatedUnspentTxOuts = b.ProcessTransactions(unspentTxOuts);
        if (updatedUnspentTxOuts == null)
        {
            Destroy(b.gameObject);
            return;
        }

        blocks.Add(b);

        PlaceBlock(b);

        unspentTxOuts = updatedUnspentTxOuts;
        transactionPool.Update(unspentTxOuts);
    }

    public void PlaceBlock(Block b)
    {
        b.gameObject.transform.SetParent(transform, false);
        b.gameObject.transform.localPosition = new Vector3(b.index * b.transform.localScale.x, 0);
    }

    public void ReplaceChain(List<Block> newBlocks)
    {
        Transaction.UnspentTxOut[] newUnspentTxOuts = ValidateChain(newBlocks);

        if (newUnspentTxOuts == null)
        {
            newBlocks.ForEach(b => Destroy(b.gameObject));
            return;
        }

        if (GetAccumulatedDifficulty(newBlocks) <= GetAccumulatedDifficulty(blocks) && !(newBlocks.Count == 1 && newBlocks.Last().index == 0))
        {
            newBlocks.ForEach(b => Destroy(b.gameObject));
            return;
        }

        Debug.Log("Received blockchain is valid. Replacing current blockchain with received blockchain");

        blocks.ForEach(b => Destroy(b.gameObject));
        blocks = newBlocks;
        blocks.ForEach(b => PlaceBlock(b));

        unspentTxOuts = newUnspentTxOuts;
        transactionPool.Update(unspentTxOuts);
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

    private bool IsFirstBlockValid(List<Block> chain)
    {
        Block b = chain.First();
        if (b == null) return false;

        return b.IsValidGenesis();
    }

    public int GetAccumulatedDifficulty()
    {
        return GetAccumulatedDifficulty(blocks);
    }

    private int GetUpdatedDifficulty()
    {
        Block last = blocks.Last();

        if (last.index % DIFFICULTY_ADJUSTMENT_INTERVAL != 0 || last.index == 0) return last.difficulty;

        Block previousAdjustementBlock = blocks[blocks.Count - DIFFICULTY_ADJUSTMENT_INTERVAL];
        int timeExpected = BLOCK_GENERATION_INTERVAL * DIFFICULTY_ADJUSTMENT_INTERVAL;
        int timeTaken = (int)((last.timestamp - previousAdjustementBlock.timestamp).TotalSeconds);

        if (timeTaken < timeExpected / 2) return previousAdjustementBlock.difficulty + 1;
        else if (timeTaken > timeExpected * 2) return previousAdjustementBlock.difficulty - 1;
        else return last.difficulty;
    }

}