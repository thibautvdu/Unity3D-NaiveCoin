using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

public class UiControl : MonoBehaviour {
    [SerializeField]
    private Image menuBackground;
    [SerializeField]
    private Text balance;
    [SerializeField]
    private Dropdown receiverNode;
    [SerializeField]
    private InputField amount;

    private List<Node> currentNodes = new List<Node>();
    private Node selectedNode = null;

    /// <summary>
    /// BETTER : replace update with an event listener pattern
    /// </summary>
    void FixedUpdate()
    {
        if (selectedNode == null) balance.text = "no node selected";
        else balance.text = "Node #"  + selectedNode.NodeId + ", balance : " + selectedNode.GetNodeBalance();
    }

    /// <summary>
    /// New node in the scene
    /// </summary>
    /// <param name="newNode"></param>
    public void NewNodeEvent(Node newNode)
    {
        currentNodes.Add(newNode);
        List<Dropdown.OptionData> options = new List<Dropdown.OptionData>() { new Dropdown.OptionData("Select receiver") };
        options.AddRange(currentNodes.Select(n => new Dropdown.OptionData("Node #" + n.NodeId)).ToList());
        receiverNode.options = options;
        receiverNode.value = 0;
    }

    /// <summary>
    /// Clic on a node
    /// </summary>
    /// <param name="address"></param>
    public void NodeSelectedEvent(string address)
    {
        selectedNode = currentNodes.FirstOrDefault(n => n.NodeAddress == address);
        if(selectedNode == null)
        {
            Debug.LogError("couldn't retrieve the node from address in the scene");
        }
    }

    /// <summary>
    /// Tell all the nodes to try to mine the next block
    /// </summary>
    public void Mine()
    {
        currentNodes.ForEach(n => n.MineBlock());
    }

    /// <summary>
    /// Send the transaction according to the current form's values
    /// </summary>
    public void Send()
    {
        if(selectedNode == null)
        {
            Debug.LogError("click on the node you wish to use");
            return;
        }

        if(receiverNode.value == 0)
        {
            Debug.LogError("select a receiver node");
            return;
        }

        if(string.IsNullOrEmpty(amount.text))
        {
            Debug.LogError("enter amount to send (can be 0)");
            return;
        }

        selectedNode.SendTransaction(currentNodes[receiverNode.value-1].NodeAddress, int.Parse(amount.text));
    }
}
