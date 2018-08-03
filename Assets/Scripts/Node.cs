using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// We are using unity messages in place of a websocket for P2P communication between nodes
/// </summary>
public class Node : MonoBehaviour {
    public enum MessageType { QUERY_LATEST, QUERY_ALL, RESPONSE_BLOCKCHAIN, QUERY_TRANSACTION_POOL, RESPONSE_TRANSACTION_POOL }

    public struct Message
    {
        public Node sender;
        public MessageType type;
        public object data;
    }

    /// <summary>
    /// Quick hack : override Debug.Log to add the node # automatically
    /// </summary>
    internal class DebugHack
    {
        int num;

        public DebugHack(int num)
        {
            this.num = num;
        }

        public void Log(string s)
        {
            UnityEngine.Debug.Log("node #" + num + " " + s);
        }

        public void LogWarning(string s)
        {
            UnityEngine.Debug.LogWarning("node #" + num + " " + s);
        }

        public void LogError(string s)
        {
            UnityEngine.Debug.LogError("node #" + num + " " + s);
        }
    }

    public delegate void Callback();

    public static int NbInstance = 0;

    public int NodeId { get; private set; }
    public string NodeAddress
    {
        get
        {
            return wallet.PublicKey;
        }
    }

    private Wallet wallet;
    private List<Node> peers;

    [SerializeField]
    private Text nodeLabel;
    [SerializeField]
    private Blockchain blockchain;

    private IEnumerator miningCoroutine;
    private DebugHack Debug;

    void Start()
    {
        NodeId = NbInstance++;
        wallet = new Wallet(NodeId);
        blockchain.Init(wallet.PublicKey, 2);

        peers = FindObjectsOfType<Node>().ToList();
        peers.Remove(this);

        Debug = new DebugHack(NodeId);

        QueryLatest();
        QueryTransactionPool();

        FindObjectOfType<UiControl>().NewNodeEvent(this);
        nodeLabel.text = "#" + NodeId;
    }

    void OnMouseDown()
    {
        FindObjectOfType<UiControl>().NodeSelectedEvent(wallet.PublicKey);
    }

    /// <summary>
    /// Get the balance associated with the node's wallet's public key
    /// </summary>
    /// <returns></returns>
    public int GetNodeBalance()
    {
        return blockchain.GetBalance(wallet.PublicKey);
    }

    /// <summary>
    /// Unity event, in a real world scenario that would be listening over network
    /// </summary>
    /// <param name="m"></param>
    public void PeerMessage(Message m)
    {
        if (peers == null) return; // not ready yet

        if (!peers.Contains(m.sender))
        {
            Debug.Log("New peer connected");
            peers.Add(m.sender);
        }

        switch (m.type)
        {
            case MessageType.QUERY_LATEST:
                {
                    Message answer = new Message { sender = this, type = MessageType.RESPONSE_BLOCKCHAIN, data = new List<Block> { blockchain.CloneLatestBlock() } };
                    m.sender.SendMessage("PeerMessage", answer);
                }
                break;
            case MessageType.QUERY_ALL:
                {
                    Message answer = new Message { sender = this, type = MessageType.RESPONSE_BLOCKCHAIN, data = blockchain.CloneChain() };
                    m.sender.SendMessage("PeerMessage", answer);
                }
                break;
            case MessageType.QUERY_TRANSACTION_POOL:
                {
                    Message answer = new Message { sender = this, type = MessageType.RESPONSE_TRANSACTION_POOL, data = blockchain.GetPooledTransactions() };
                    m.sender.SendMessage("PeerMessage", answer);
                }
                break;
            case MessageType.RESPONSE_BLOCKCHAIN:
                {
                    List<Block> receivedBlocks = (List<Block>)m.data;

                    if(receivedBlocks == null || receivedBlocks.Count == 0)
                    {
                        Debug.LogWarning("Received empty blockchain data from peer");
                        return;
                    }

                    if(receivedBlocks.Count == 1)
                    {
                        if(receivedBlocks.Last().previousHash == blockchain.LastBlock().hash && receivedBlocks.Last().index == blockchain.LastBlock().index + 1)
                        {
                            Debug.Log("Received a new block");
                            blockchain.StopMining();
                            blockchain.AddBlock(receivedBlocks.Last());
                        }
                        else if(receivedBlocks.Last().index != 0) // only query if not genesis
                        {
                            Debug.Log("Received a new block but the hash or index doesn't match. Querying the whole thing");
                            Message request = new Message { sender = this, type = MessageType.QUERY_ALL, data = null };
                            m.sender.SendMessage("PeerMessage", request);
                        }
                        else // replace genesis with the exising one
                        {
                            Debug.Log("Replace with existing genesis");
                            blockchain.ReplaceChain(receivedBlocks);
                        }
                    }
                    else
                    {
                        if(Blockchain.GetAccumulatedDifficulty(receivedBlocks) > blockchain.GetAccumulatedDifficulty())
                        {
                            Debug.Log("Received a new chain and its accumulated difficulty is higher, replacing...");
                            blockchain.ReplaceChain(receivedBlocks);
                        }
                        else
                        {
                            Debug.Log("Received a new chain and whith lower accumulated difficulty, doing nothing");
                        }
                    }
                }
                break;
            case MessageType.RESPONSE_TRANSACTION_POOL:
                {
                    Transaction[] receivedPoolTxs = (Transaction[])m.data;

                    if(receivedPoolTxs == null)
                    {
                        Debug.LogError("Received null, expection transaction pool");
                        return;
                    }

                    foreach(Transaction tx in receivedPoolTxs)
                    {
                        blockchain.AddToPool(tx);
                    }
                }
                break;
        }
    }

    /// <summary>
    /// Ask the whole network for the latest block
    /// </summary>
    public void QueryLatest()
    {
        Message broadcast = new Message { sender = this, type = MessageType.QUERY_LATEST, data = null };
        peers.ForEach(n => n.SendMessage("PeerMessage", broadcast));
    }

    /// <summary>
    /// Ask the whole network for the current unconfirmed transaction pool
    /// </summary>
    public void QueryTransactionPool()
    {
        Message broadcast = new Message { sender = this, type = MessageType.QUERY_TRANSACTION_POOL, data = null };
        peers.ForEach(n => n.SendMessage("PeerMessage", broadcast));
    }

    /// <summary>
    /// Broadcast the latest block to the network
    /// </summary>
    public void BroadcastLatest()
    {
        peers.ForEach(n => 
            n.SendMessage(
                "PeerMessage", 
                new Message {
                    sender = this, 
                    type = MessageType.RESPONSE_BLOCKCHAIN, 
                    data = new List<Block> { blockchain.CloneLatestBlock() }
                }
            )
        );
    }

    /// <summary>
    /// Broadcast the unconfirmed transaction pool to the network
    /// </summary>
    public void BroadcastTransactionPool()
    {
        Message broadcast = new Message { sender = this, type = MessageType.RESPONSE_TRANSACTION_POOL, data = blockchain.GetPooledTransactions()};
        peers.ForEach(n => n.SendMessage("PeerMessage", broadcast));
    }

    public void PrintBlocks()
    {
        Debug.Log(blockchain.ToString());
    }

    /// <summary>
    /// Register a transaction to be sent in the transaction pool
    /// </summary>
    /// <param name="address"></param>
    /// <param name="amount"></param>
    public void SendTransaction(string address, int amount)
    {
        if(blockchain.SendTransaction(wallet.PublicKey, address, wallet.PrivateKeyCointainer, amount))
            BroadcastTransactionPool();
    }

    /// <summary>
    /// Start mining the next block
    /// </summary>
    public void MineBlock()
    {
        blockchain.GenerateNextblock(wallet.PublicKey, () =>
        {
            BroadcastLatest();
        });
    }
}
