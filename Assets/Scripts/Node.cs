using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// We are using unity messages in place of a websocket for P2P communication between nodes
/// </summary>
public class Node : MonoBehaviour {
    public static int NbInstance = 0;

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

    public readonly int NodeId;
    public string NodeAddress
    {
        get
        {
            return wallet.PublicKey;
        }
    }

    [SerializeField]
    private Text balanceUIText;
    [SerializeField]
    private InputField receiverIdField;
    [SerializeField]
    private InputField amountField;

    private DebugHack Debug;
    private List<Node> peers;
    private Blockchain blockchain;
    private Wallet wallet;

    public int NodeBalance
    {
        get
        {
            return wallet.GetBalance(blockchain.GetCurrentUnspentTxOuts());
        }
    }

    public Node()
    {
        NodeId = NbInstance++;
        Debug = new DebugHack(NodeId);
    }

    public void PeerMessage(Message m)
    {
        if (!peers.Contains(m.sender))
        {
            Debug.Log("New peer connected");
            peers.Add(m.sender);
        }

        switch (m.type)
        {
            case MessageType.QUERY_LATEST:
                {
                    Message answer = new Message { sender = this, type = MessageType.RESPONSE_BLOCKCHAIN, data = new List<Block> { blockchain.LatestBlock() } };
                    m.sender.SendMessage("PeerMessage", answer);
                }
                break;
            case MessageType.QUERY_ALL:
                {
                    Message answer = new Message { sender = this, type = MessageType.RESPONSE_BLOCKCHAIN, data = blockchain.GetCloneChain() };
                    m.sender.SendMessage("PeerMessage", answer);
                }
                break;
            case MessageType.QUERY_TRANSACTION_POOL:
                {
                    Message answer = new Message { sender = this, type = MessageType.RESPONSE_TRANSACTION_POOL, data = blockchain.transactionPool.GetTransactions() };
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
                        if(receivedBlocks.Last().previousHash == blockchain.LatestBlock().hash && receivedBlocks.Last().index == blockchain.LatestBlock().index + 1)
                        {
                            Debug.Log("Received a new block");
                            blockchain.AddBlock(receivedBlocks.Last());
                        }
                        else
                        {
                            Debug.Log("Received a new block but the hash or index doesn't match. Querying the whole thing");
                            Message request = new Message { sender = this, type = MessageType.QUERY_ALL, data = null };
                            m.sender.SendMessage("PeerMessage", request);
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

    public void QueryLatest()
    {
        Message broadcast = new Message { sender = this, type = MessageType.QUERY_LATEST, data = null };
        peers.ForEach(n => n.SendMessage("PeerMessage", broadcast));
    }

    public void QueryTransactionPool()
    {
        Message broadcast = new Message { sender = this, type = MessageType.QUERY_TRANSACTION_POOL, data = null };
        peers.ForEach(n => n.SendMessage("PeerMessage", broadcast));
    }

    public void BroadcastLatest()
    {
        Message broadcast = new Message { sender = this, type = MessageType.RESPONSE_BLOCKCHAIN, data = new List<Block> { blockchain.LatestBlock() } };
        peers.ForEach(n => n.SendMessage("PeerMessage", broadcast));
    }

    public void BroadcastTransactionPool()
    {
        Message broadcast = new Message { sender = this, type = MessageType.RESPONSE_TRANSACTION_POOL, data = blockchain.transactionPool.GetTransactions()};
        peers.ForEach(n => n.SendMessage("PeerMessage", broadcast));
    }

    public void PrintBlocks()
    {
        Debug.Log(blockchain.ToString());
    }

    public void SendTransaction()
    {
        int nodeId = int.Parse(receiverIdField.text);
        int amount = int.Parse(amountField.text);
        Node receiver;

        if (nodeId != this.NodeId)
        {
            receiver = peers.FirstOrDefault(p => p.NodeId == nodeId);
            if (receiver == null)
            {
                Debug.LogError("No registered peer for that id");
                return;
            }
        }
        else
        {
            receiver = this;
        }

        SendTransaction(receiver.NodeAddress, amount);
    }

    public void SendTransaction(string address, int amount)
    {
        if(blockchain.SendTransaction(wallet.PublicKey, address, wallet.PrivateKeyCointainer, amount))
            BroadcastTransactionPool();
    }

    public void MineBlock()
    {
        StartCoroutine(MineBlockCor());
    }

    private IEnumerator MineBlockCor()
    {
        yield return StartCoroutine(blockchain.GenerateNextblock(wallet.PublicKey));
        BroadcastLatest();
    }

    private void InitP2P()
    {
        peers = FindObjectsOfType<Node>().ToList();
        peers.Remove(this);
    }

    // Use this for initialization
    void Start () {
        wallet = new Wallet(NodeId);

        InitP2P();

        blockchain = new Blockchain(NodeAddress,2);

        QueryLatest();
        StartCoroutine(WaitAndQueryPool());
    }

    private IEnumerator WaitAndQueryPool()
    {
        yield return new WaitForSeconds(4);
        QueryTransactionPool();
    }

    void FixedUpdate()
    {
        balanceUIText.text = NodeBalance.ToString();
    }
}
