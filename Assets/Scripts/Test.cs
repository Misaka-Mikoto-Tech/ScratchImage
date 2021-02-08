using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class Test : MonoBehaviour
{
    public Button btnReset;
    public ScratchImage scratchImage;

    void Start()
    {
        btnReset.onClick.AddListener(() => scratchImage.ResetMask());    
    }
}
