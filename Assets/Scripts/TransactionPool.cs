using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class TransactionPool {
    private List<Transaction> transactions = new List<Transaction>();

    public Transaction[] GetTransactions()
    {
        return transactions.Select(t => (Transaction)t.Clone()).ToArray();
    }

    public bool Add(Transaction tx, Transaction.UnspentTxOut[] unspentTxOuts)
    {
        if(transactions.Any(t => t.id == tx.id))
        {
            Debug.Log("Transaction already exist in pool");
            return false;
        }

        if(!tx.IsValid(unspentTxOuts))
        {
            Debug.LogError("Trying to add invalid tx to pool");
            return false;
        }

        if(!TxInAvailable(tx))
        {
            Debug.LogError("Trying to add invalid tx to pool");
            return false;
        }

        Debug.Log("Adding to tx pool : " + tx.id);
        transactions.Add(tx);

        return true;
    }

    public void Update(Transaction.UnspentTxOut[] unspentTxOuts)
    {
        transactions.RemoveAll(
            t => t.txIns.Any(
                ti => !unspentTxOuts.Any(
                    uto => uto.txId == ti.txOutTxId && uto.txOutIndex == ti.txOutIndex
                )
            )
        );
    }

    private bool TxInAvailable(Transaction tx)
    {
        List<Transaction.TxIn> poolTxIns = transactions.SelectMany(t => t.txIns).ToList();

        foreach(Transaction.TxIn txIn in tx.txIns)
        {
            if(poolTxIns.Any(ti => ti.txOutTxId == txIn.txOutTxId && ti.txOutIndex == txIn.txOutIndex))
            {
                Debug.LogError("TxIn already in the pool");
                return false;
            }
        }

        return true;
    }
}
