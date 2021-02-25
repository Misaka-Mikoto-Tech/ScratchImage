using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;


public class Test : MonoBehaviour
{
    public Button btnReset;
    public Text txtFillPercent;
    public Text txtAvgVal;
    public ScratchImage scratchImage;

    void Start()
    {
        btnReset.onClick.AddListener(() => scratchImage.ResetMask());
        StartCoroutine(GetStatsInfo());
    }

    WaitForSeconds _wait0_1 = new WaitForSeconds(0.1f);
    IEnumerator GetStatsInfo()
    {
        while(true)
        {
            yield return _wait0_1;

            var data = scratchImage.GetStatData();
            txtFillPercent.text = $"填充率: {data.fillPercent: 0.00}";
            txtAvgVal.text = $"平均值: {data.avgVal: 0.00}";
        }
    }
}
