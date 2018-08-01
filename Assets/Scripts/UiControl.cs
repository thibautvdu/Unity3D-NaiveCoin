using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class UiControl : MonoBehaviour {

    [SerializeField] private GameObject NodeUI;

	// Use this for initialization
	void Start () {
		
	}
	
	// Update is called once per frame
	void Update () {
		
	}

    public void AddNode()
    {
        Instantiate(NodeUI, gameObject.transform);
    }
}
